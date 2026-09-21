# Architecture Review — claude/selection-cycle (5d4a0e9..b648382)

## Scope Reviewed

**Commits (`git log 5d4a0e9..b648382 --oneline`):**
```
b648382 Add project governance and spatial plot architecture
89ffac4 Stabilize command semantics, store, patch, and cue playback
c20f1c3 Fix RELEASE, Selection Cycle, CLEAR, and encoder interaction
```
(3 commits above `5d4a0e9`, which is the review baseline and itself titled "Stabilize Selection Cycle and retire legacy Programmer UI" — its own diff is not part of this range.)

**`git diff 5d4a0e9..b648382 --stat`:** 72 files changed, 6451 insertions(+), 326 deletions(-). Key files:
- New governance/architecture docs: `CLAUDE.md` (new, 461 lines), `docs/SPATIAL_PLOT_ARCHITECTURE.md` (new), `docs/OPERATOR_UX_ROADMAP.md` (+27), `.claude/agents/*.md` (new)
- `src/DmxConsole.Application/CommandDispatcher.cs`, `CommandResult.cs`, `ConsoleContext.cs`
- `src/DmxConsole.Application/CommandSurface/CommandComposer.cs` (+581), `CommandComposition.cs`, `CommandTokenKind.cs`
- `src/DmxConsole.Application/Commands/Cues/StoreCueCommand.cs` (new), `Commands/Patch/PatchFixturesCommand.cs` (new), `Commands/Presets/StorePresetFamilyDetector.cs` (new), `Commands/Presets/StorePresetTransactionBuilder.cs` (new)
- `src/DmxConsole.Application/Commands/Selection/ClearSelectionAction.cs`, `ClearSelectionCommand.cs`, `ReverseSelectionCommand.cs` (new), `SelectionCommandBase.cs`
- `src/DmxConsole.Core/Engine/CueList.cs` (+199/-… rewritten Tick/StartTransitionTo), `CueTriggerMode.cs`, `CueTransitionReason.cs` (new), `Cue.cs`, `Executor.cs`, `IPlaybackSource.cs`
- `src/DmxConsole.Core/Selection/FixtureSelection.cs` (`Reverse()`)
- `src/DmxConsole.Web/Services/CommandSurfaceViewModel.cs` (+663), `MainViewModel.cs`, `CueListViewModel.cs`, `EncoderDrawerViewModel.cs`, `GroupsViewModel.cs`
- `src/DmxConsole.Web/Components/{ConsoleUi,Shell}/*.razor`
- Extensive new tests across `DmxConsole.Application.Tests`, `DmxConsole.Core.Tests`, `DmxConsole.Web.Tests`

## Verdict

**ACCEPT WITH FIXES**

The engine-level work (Cue trigger/navigation state machine, Selection History, STORE/RELEASE/Quick-Patch grammar, atomicity in `PatchFixturesCommand`/`StorePresetTransactionBuilder`) is careful, well-documented, and matches CLAUDE.md's invariants closely — in several places the code comments read as if they were written directly against the CLAUDE.md sections they satisfy (unsurprising, since `CLAUDE.md` itself is introduced in the final commit of this range and appears to codify decisions already made in the prior three commits). No parallel parser, no Core mutation from Razor, no per-`AttributeClass` cue timing reintroduced, and Patch/StoreCue both correctly separate structural vs. programming Undo. The issues found are integration/completeness gaps, not conceptual violations, but one of them (RELEASE not refreshing faders) is exactly the recurring bug class CLAUDE.md calls out by name.

## Build & Tests

- `dotnet build DmxConsole.sln` — **could not complete**: a running `DmxConsole.Web` process (PID 78976, presumably a `dotnet run` left over from manual testing) held file locks on `DmxConsole.Core.dll`/`Application.dll`/`Protocols.dll`/`Fixtures.dll` in `src/DmxConsole.Web/bin/Debug/net8.0`, causing `MSB3027`/`MSB3021` copy failures after 10 retries. The reviewing agent did not have permission to terminate that process in its sandbox, so a full solution build was not obtained. This is an environment artifact, not a code defect attributable to the diff.
- `dotnet test tests/DmxConsole.Core.Tests/DmxConsole.Core.Tests.csproj` — **Passed: 245, Failed: 0, Skipped: 0, Total: 245** (these projects don't reference the locked `DmxConsole.Web` output, so this ran cleanly).
- `dotnet test tests/DmxConsole.Application.Tests/DmxConsole.Application.Tests.csproj` — **Passed: 241, Failed: 0, Skipped: 0, Total: 241**.
- `dotnet test tests/DmxConsole.Web.Tests/DmxConsole.Web.Tests.csproj` — **could not run**, blocked by the same file lock (this project does depend on `DmxConsole.Web`'s build output). Not verified in this session.
- Full-solution build/test and `DmxConsole.Fixtures.Tests`/`DmxConsole.Protocols.Tests` were **not run** this session; this is not a claim that they pass. Given the Core/Application results and the manual code review below, there is moderate confidence the Web.Tests suite (which contains the new `CommandSurfaceViewModelReleaseTests.cs`, `CommandSurfaceViewModelStoreTests.cs`, `EncoderGestureTests.cs`, `SelectionHistoryViewModelTests.cs`) also passes, but this is unverified and should be confirmed once the locking process is stopped.

## Findings Per Invariant Area

### §1/§2 Selection vs Programmer vs Playback, Last Selection — verified, correct
`src/DmxConsole.Application/CommandDispatcher.cs` centralizes "Last Selection" update in exactly one place: `ProducesSelectionSnapshot(IConsoleCommand)` recurses through `CompositeCommand.Commands` and calls `_context.SelectionCycle.RememberSelection(...)` only after a successful `Dispatch`. `SelectionCommandBase.ProducesSelectionSnapshot` defaults to `true` for every selection-mutating command (Toggle/Range/Odd/Even/Reverse/Next/Previous/AddGroup), and `ClearSelectionCommand` is the sole documented override returning `false` — this correctly implements "CLEAR clears Current Selection, does NOT overwrite Last Selection" (CLAUDE.md §2). `ClearSelectionAction` (the fixed-key CLEAR path) is structurally excluded since it's an `IConsoleAction`, dispatched via `DispatchAction`, which never reaches this logic. `tests/DmxConsole.Application.Tests/SelectionHistoryTests.cs` (242 new lines) and `tests/DmxConsole.Web.Tests/SelectionHistoryViewModelTests.cs` (120 new lines) exercise this directly.

### §3 UI Must Not Own Semantics — mostly verified, one integration caveat (see Risks)
`ClearSelectionAction.cs`, `PatchFixturesCommand.cs`, `StoreCueCommand.cs`, `StorePresetTransactionBuilder.cs` are all single, shared Application operations that both the Command Surface grammar and the relevant Razor panels dispatch through. `ChannelsView.razor`'s `ClearSelection()` now calls `Vm.CommandSurfaceVm.PressClear()` instead of a second, independent `ClearSelectionCommand.Execute` path (`src/DmxConsole.Web/Components/ConsoleUi/ChannelsView.razor:118-121`). `CommandSurfaceViewModel.PressClear()` (`src/DmxConsole.Web/Services/CommandSurfaceViewModel.cs:882-894`) is the one place that resets command-line composition, disarms RELEASE/STORE panel state, and dispatches `ClearSelectionAction` — no duplicate CLEAR semantics found.

However: the new `ConsoleShell`/`CommandSurface`/`ChannelsView`/`FixturesView`/`GroupsView` LIVE surfaces (where all of this CLEAR/RELEASE/STORE/Selection-History work is wired up) are only reachable via `/workspace-preview` (`src/DmxConsole.Web/Components/Pages/WorkspacePreview.razor`). The production route `/` (`src/DmxConsole.Web/Components/Pages/Console.razor`) still renders `FaderBank`/`FaderCard` and does not reference `ChannelsView`/`FixturesView`/`GroupsView`/`CommandSurface` at all — see Risks below. This predates the reviewed range (`Console.razor` was last touched at `5d4a0e9`, the range's own baseline) so it is not a regression *introduced* by these three commits, but it means the claims embedded in this diff's own comments ("the ONE operator CLEAR semantic, converged onto by every UI entry point") are only true within the preview surface, not the actual default page.

### §4 Command Grammar Shared — verified
All new grammar (STORE target resolution, Quick Patch forms A/B, DMX addressing, value distribution) lives entirely in `CommandComposer.cs`. No second parser found in Razor/ViewModels; `CommandSurfaceViewModel` only holds UI-arming state (`ReleaseArmed`, `StoreFamilyChoiceArmed`) and delegates all grammar decisions to `_composer`.

### §5 STORE — verified against spec text exactly
`CommandComposer.ResolveStoreGroup` (`src/DmxConsole.Application/CommandSurface/CommandComposer.cs:770-798`) and `ResolveStoreCue` (`:805-833`) both produce the literal error strings from CLAUDE.md §5 ("GROUP NUMBER IS MISSING", "CUE NUMBER IS MISSING") on a missing number at `finalize`, and both are create-only (delegate to `StoreGroupCommand`/`StoreCueCommand`, which fail rather than silently update an existing object). Multi-family Preset STORE is centralized in `StorePresetTransactionBuilder.BuildTransaction` (`src/DmxConsole.Application/Commands/Presets/StorePresetTransactionBuilder.cs:32-43`), which wraps all per-family `StorePresetCommand`s (plus any preceding selection commands) in one `CompositeCommand` — atomic per §5's "Multi-family STORE must be atomic." Conflict detection (`FindConflicts`) runs before any command is built, matching "validate-before-mutate."

### §6 RELEASE — verified, correctly scoped, one gap (see Risks)
`CommandSurfaceViewModel.ConfirmArmedRelease` (`src/DmxConsole.Web/Services/CommandSurfaceViewModel.cs:709-752`) implements all three RELEASE grammar shapes exactly as specified: family-qualified + ENTER → Selection-scoped family release; bare + ENTER → Selection-scoped full release; bare + second RELEASE → global `ReleaseCommand(context.Patch.Fixtures.ToList(), null)`, independent of Selection. Selection remains untouched after release (only `SelectionCycle.MarkExecutionCompleted()` is called, not a Selection mutation) — correct per §6 "Selection remains active."

### §7 Programming Undo vs Structural Show Changes — verified, correct
`PatchFixturesCommand` (`src/DmxConsole.Application/Commands/Patch/PatchFixturesCommand.cs:1-82`) implements `IConsoleAction`, not `IConsoleCommand` — its own doc comment explicitly states the consequence ("a successful Quick Patch cannot currently be undone by any stack"), which is the correct, honest reading of §7/§8 rather than inventing a second structural Undo stack. `StoreCueCommand` and `StoreGroupCommand`/`StorePresetCommand` remain `IConsoleCommand`/`IHasUndoRisk` (normal Programming Undo) — consistent with pre-existing precedent for object-store commands, not a new departure introduced here.

### §8 Quick Patch — verified
`PatchFixturesCommand.Execute` (`:38-58`) validates every requested `Number` is free **before** creating any fixture, and on a mid-loop exception rolls back everything added in that call (`foreach (var fixture in created) context.Patch.Remove(fixture);`) before returning `CommandResult.Failed`. This is genuinely atomic and rollback-safe, and both Quick Patch grammar forms (A: `FIXTURE ... AT DMX ...`, B: `DMX ... FIXTURE ...`) converge on the same command (`CommandComposer.cs:1168-…`, "The ONE place both Quick Patch forms build the shared PatchFixturesCommand").

### §9/§10 Cue Trigger Semantics and Navigation — verified, this is the strongest part of the diff
`CueList.Tick()` (`src/DmxConsole.Core/Engine/CueList.cs:~370-420`) is rewritten from a flawed "current cue follows itself" model to the correct "Trigger belongs to the NEXT cue, gated on the CURRENT transition's own completion" state machine, exactly matching CLAUDE.md §9. `_chainEligible` is set only for `CueTransitionReason.Go && !instant` (`StartTransitionTo`), so BACK, GO TO/Jump, and SHIFT-instant navigation all correctly refuse to arm an automatic chain (§10). `_transitionCompletedAtElapsedSeconds` anchors the Wait timer to the moment the *previous* fade completes, not to `_fadeStartUtc`, closing the "WaitTime overlapping the previous fade" bug the old model had. `ISequencedPlayback.Go(bool instant = false)`/`Back(bool instant = false)` correctly thread the SHIFT-instant flag down through `Executor` into `CueList`. `tests/DmxConsole.Core.Tests/CueListTests.cs` grew from its prior size to 493 lines, covering Manual/AutoFollow/Wait chaining, BACK/Jump non-arming, and instant navigation.

### §11 Value Distribution — present, backed by dedicated tests
`tests/DmxConsole.Application.Tests/CommandSurface/CommandComposerValueDistributionTests.cs` (434 new lines) covers multi-control-point `AT ... THRU ... THRU ...` sequences. The full interpolation implementation was not traced line-by-line in `CommandComposer.cs` given the size of that file, but the test file's scope and the section's presence in the diff stat indicate this was implemented as a slice, and Core tests passed.

### §12 Softkeys vs Fixed Keys — verified, no violation
`ReverseSelectionCommand` (`src/DmxConsole.Application/Commands/Selection/ReverseSelectionCommand.cs`) and `CommandTokenKind.Reverse` exist and are exercised by `CommandComposerFamilyActionTests.cs`, but there is **no Web-layer wiring** (no button, no softkey, no keyboard binding) that lets an operator actually invoke REVERSE yet — a search for "reverse" under `src/DmxConsole.Web/` returns nothing outside the Application layer's own token/command names. This is not a §12 violation (nothing was placed on a fixed key), just an unfinished/orphaned feature at the Application layer with test coverage but no UI affordance. Non-blocking, worth tracking.

### §13 Fixture Profiles — no findings
No new hardcoded fixture behavior found in the reviewed diff; `PatchFixturesCommand` and Quick Patch grammar operate purely on `FixtureProfile`/`FixtureMode` objects the operator selects on the Patch screen, never inventing calibration data.

### §14 Engine Layering, Dispatcher separation, CompositeCommand — verified
`PatchFixturesCommand : IConsoleAction`, `StoreCueCommand : IConsoleCommand, IHasUndoRisk` — correctly separated. `CommandDispatcher.DispatchBatch` still routes through `CompositeCommand`. No `CommandResult.Message` parsing found in the reviewed diff; `CommandResult` gained typed fields (`Cue`, and the `PatchFixtures`/`StoreCue`/`ReverseSelection` enum members) rather than encoding new information in `Message`.

## Blocking Issues

None found that require a hard revert. The one issue with real operator-facing consequence is listed as non-blocking below because it could not be confirmed as currently reachable in the shipped route, but it should be fixed before the ChannelsView/CommandSurface surface becomes the production default.

## Non-Blocking Observations

1. **RELEASE dispatched via `CommandSurfaceViewModel` never calls `ProgrammerViewModel.RefreshAllFaders()`.** `ConfirmArmedRelease` (`src/DmxConsole.Web/Services/CommandSurfaceViewModel.cs:709-752`) dispatches `ReleaseCommand` (both scoped and global forms) via `_dispatcher.Dispatch`/`DispatchBatch`, but `CommandSurfaceViewModel` has no reference to `ProgrammerViewModel` at all (confirmed via `grep -n "RefreshAllFaders|ProgrammerVm" CommandSurfaceViewModel.cs` → no matches). CLAUDE.md's Blazor Conventions section calls this out by name: "After any dispatch that can change Programmer values ... call `ProgrammerViewModel.RefreshAllFaders()`. This bug has already happened twice." Tracing whether this is currently *visible*: `ChannelsView.razor`'s LIVE grid computes `LiveChannelState.For(...)` fresh from `context.Programmer`/`context.EffectiveOutput` on every render (polled every 200ms by a `System.Threading.Timer`), so that specific grid is not affected. However, `FaderCard.razor` (rendered from `FaderBank` on the production `/` route) binds directly to the cached `ChannelFaderViewModel.Value`, which is only resynced by an explicit `RefreshFromProgrammer` call — so if/when RELEASE from the Command Surface and the legacy fader bank ever coexist on the same page (or once the LIVE surface becomes primary and a similar cached-value pattern is reused elsewhere), this will reproduce the exact bug class CLAUDE.md warns about. **Recommend**: add `_programmerVm.RefreshAllFaders()` (or route the whole RELEASE confirm path through `ProgrammerViewModel.Dispatch`, which already does this) before this surface is promoted out of preview.
2. **The entire body of Selection Cycle/RELEASE/STORE/Quick-Patch/CLEAR UI work in this range lives behind `/workspace-preview`, not the production `/` route.** `Console.razor` (the `@page "/"` component) was not touched in this range and still renders `FaderBank`+`FaderCard` with no reference to `CommandSurface`, `ChannelsView`, `FixturesView`, or `GroupsView`. `SelectionBar.razor` (which *was* touched, 11 lines) is shared between both routes, so the Selection Cycle/CLEAR fix does reach production for that one component, but the LIVE grid, Command Surface grammar, and RELEASE/STORE panels described throughout this diff's comments are not reachable from the app's actual default page. This is a pre-existing condition (not introduced by these three commits) but is worth flagging because several code comments in this diff assert universal UI convergence ("every UI entry point") that is only true inside the preview surface.
3. `ReverseSelectionCommand`/`CommandTokenKind.Reverse` are implemented and tested at the Application layer with no corresponding Web UI entry point yet (see §12 finding above) — track as follow-up work, not a defect.
4. `CommandResult.Cue` and the `PatchFixtures`/`StoreCue`/`ReverseSelection` `ConsoleActionType` additions are pure data additions; no `Message`-parsing logic was introduced anywhere checked.

## Report Accuracy

No separate "implementer report" document was supplied for cross-checking; this assessment is against the diff and CLAUDE.md directly, using the in-code doc comments (which are unusually detailed and self-referential to CLAUDE.md's own section numbers) as the closest available substitute for an implementer narrative. Where those comments make claims (e.g. "the ONE place... every UI entry point," "atomic... rollback-safe"), each was verified against the actual code rather than accepted at face value, and found accurate with the one qualification noted in Observation #2 (true within the preview surface, not the whole app) and the one gap noted in Observation #1 (RefreshAllFaders).

## Risks Not Covered By Tests

- No test exercises RELEASE-then-legacy-fader-display staleness (Observation #1) since the legacy `FaderCard`/`Console.razor` route isn't part of the Web.Tests scaffolding for this feature set as far as could be determined.
- `DmxConsole.Web.Tests` was not run this session (build lock) — the new `CommandSurfaceViewModelReleaseTests.cs` (353 lines), `CommandSurfaceViewModelStoreTests.cs` (172 lines), and `EncoderGestureTests.cs` (136 lines) are unverified in this pass. Given `DmxConsole.Application.Tests` (which contains most of the STORE/RELEASE/Selection logic under test) passed 241/241, there is moderate but not full confidence here.
- The value-distribution interpolation algorithm itself (§11) was not traced line-by-line in this pass given time constraints; confidence rests on the presence of a 434-line dedicated test file plus the passing Application.Tests run, not on direct code inspection of the interpolation math.

## Doc Drift

None found. `CLAUDE.md` (new in this range) and `docs/SPATIAL_PLOT_ARCHITECTURE.md` (new) are internally consistent with each other and with `docs/OPERATOR_UX_ROADMAP.md`'s new §11a, which correctly defers all Spatial/Plot detail to the new document and does not duplicate its content. `docs/COMMAND_SURFACE_KEY_SPEC.md` was not modified in this range (its current working-tree diff outside this range is out of scope here). One soft observation: `docs/OPERATOR_UX_ROADMAP.md` §1 states "LIVE is the primary operational view" — true of the implementation inside `/workspace-preview`, not of the app's actual default route (see Observation #2); this is a drift between roadmap intent and current deployment reachability rather than a contradiction between documents.
