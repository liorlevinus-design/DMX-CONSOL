# DMX Console — Authoritative Project Rules

This file is the primary working contract for all agents operating on this repository.

Before making changes:
1. Read this file.
2. Read the relevant source/tests.
3. Audit existing behavior before implementing.
4. Do not guess product or architectural decisions.

---

# Core Architecture Invariants

## 1. Selection != Programmer != Playback

These are separate concepts.

### Selection
Represents WHICH fixtures the operator is currently working with.

- Ordered.
- Shared across UI / command line / keyboard / touch / hardware.
- May exist without Programmer values.
- May change independently of Programmer.

### Programmer
Represents temporary parameter values being authored.

- One Programmer state only.
- No separate Programmer panel semantics.
- Programmer is state, not a UI surface.

### Playback
Represents cue/executor output.

Never infer one from another.

---

## 2. Last Selection & CLEAR

Authoritative rule:

Last Selection is the most recent ordered Selection state produced by any selection operation or transformation.

Sources include:
- FIXTURE
- GROUP
- GUI fixture selection
- ODD
- EVEN
- REVERSE
- NEXT / PREVIOUS
- future Selection filters/transforms

`FIXTURE .` recalls Last Selection.

Do not derive Last Selection from Programmer contents.

### CLEAR (decided — see `docs/SPEC_CONFLICTS_FOR_DECISION_1.md` C1)

CLEAR is a fixed physical key with a single, immediate meaning:

- CLEAR immediately clears Current Selection.
- CLEAR also resets/dismisses pending Command Surface state: it clears the CommandComposer /
  command line's in-progress composition, clears any pending numeric entry, and dismisses an
  armed contextual RELEASE state (the Release Panel / awaiting-`ENTER` escalation from §6).
- CLEAR does NOT clear the Programmer. Programmer values are released via RELEASE (§6), not
  CLEAR.
- CLEAR does NOT overwrite Last Selection.
- CLEAR is not Undo and not Backspace. It does not delete show data, does not undo a completed
  Store, does not release playbacks, and does not release Editor/Programmer values.
- There is no `CLEAR CLEAR` behavior. A single CLEAR press clears the entire Current Selection;
  there is no "remove only the last selection gesture" step before it, and no second press is
  required or meaningful.
- CLEAR is a non-undoable console Action (`IConsoleAction`, e.g. `ClearSelectionAction`), not an
  `IConsoleCommand` — selection clearing never enters the Programming Undo stack (see §7).

Backspace (`⌫`) is a separate physical key. It edits pending numeric/command-line input one digit
at a time and never modifies a committed Selection. Backspace and CLEAR are two independent keys
with two independent responsibilities — never multiplexed onto one physical control.

---

## 3. UI Must Not Own Semantics

Different UI surfaces may initiate the same operation, but must never implement its semantics independently.

Correct pattern:

UI / Command Surface / Softkey
→ shared Application operation
→ Core state

Avoid:
- direct Core mutations from Razor/ViewModels
- duplicate logic in multiple panels
- UI-only interpretations of STORE / RELEASE / PATCH / PLAYBACK

---

## 4. Command Grammar Is Shared

Command-line grammar must remain centralized.

Examples:
- STORE
- RELEASE
- PATCH
- AT
- TIME
- future Effects grammar

Do not create parallel parsers.

Context may change token meaning, but semantics must remain shared.

Examples:
- THRU before AT = object range
- THRU after AT = value distribution
- MINUS in object context = subtraction
- MINUS in signed-value context = unary negative sign

TIME semantics (decided — see `docs/SPEC_CONFLICTS_FOR_DECISION_1.md` N4): timing is stored per
parameter/channel only, never per family/AttributeClass. Family-scoped TIME syntax (e.g.
`POSITION TIME 5`, `COLOR TIME 3`) is shorthand only — it expands, at the grammar layer, into the
relevant per-parameter TIME writes from the fixture profile (e.g. `POSITION TIME 5` may resolve to
`PAN TIME 5` and `TILT TIME 5`). Do not introduce `PositionTime`/`ColorTime`/AttributeClass-level
timing storage anywhere in the data model. This reinforces, and must stay consistent with, the
"Known contradiction — do not reintroduce" callout below.

STORE/UPDATE/DELETE routing (decided — see `docs/SPEC_CONFLICTS_FOR_DECISION_1.md` C6): the shared
Command Surface STORE / UPDATE / DELETE operations are authoritative. Contextual per-object
soft keys (Cue, Group, Fixture, Preset, ...) may exist as UI affordances, but every one of them
must route to the same shared Application-layer STORE / UPDATE / DELETE operations described in
this section — never a separately implemented per-context mutation.

---

## 5. STORE

STORE is a real console verb.

Pressing STORE alone must never immediately create an object.

Grammar examples:

```
FIXTURE 1 THRU 10 STORE GROUP 1 ENTER
FIXTURE 1 THRU 10 STORE COLOR 2 ENTER
FIXTURE 1 THRU 10 STORE COLOR 2 POSITION 5 ENTER
STORE CUE 5 ENTER
```

Explicit number rule:
- command-line STORE must not auto-number
- missing required number on ENTER produces an error

Examples:

```
STORE GROUP ENTER   → GROUP NUMBER IS MISSING
STORE CUE ENTER     → CUE NUMBER IS MISSING
STORE COLOR ENTER   → PRESET NUMBER IS MISSING
STORE PRESET ENTER  → contextual dialog may ask for family + number
```

Preset family behavior (decided — see `docs/SPEC_CONFLICTS_FOR_DECISION_1.md` C4):
- Preset pools remain per-family / per AttributeClass. There is no single collapsed Preset
  namespace.
- one Preset object per AttributeClass pool
- multi-family store creates multiple Preset objects
- same number may be used in each pool
- `PRESET 5 ENTER` (recall) without an explicit family context must not silently guess which
  pool's Preset 5 to apply. Resolve the family from context (an armed family key) or expose the
  choice; never guess.
- `STORE PRESET` without an explicit family may open a family-selection workflow (see §5's
  earlier `STORE PRESET ENTER` example).

Store conflict terminology (decided — see `docs/SPEC_CONFLICTS_FOR_DECISION_1.md` C7). Exactly
three options, never renamed and never a fourth added without a separate decision:

UPDATE:
- merge newly touched values into existing stored content
- preserve untouched existing values

OVERWRITE:
- replace existing stored content entirely with the current store result

CANCEL:
- no mutation

Multi-family STORE must be atomic.

STORE CUE — Programmer clear (decided; see `docs/SPEC_CONFLICTS_FOR_DECISION.md` N1):
- after a successful STORE CUE, the entire Programmer is cleared automatically
- this clear applies even to values excluded by a Store filter (e.g. selected-fixtures-only or family filter)
- the Store and the Programmer clear are one atomic, undoable transaction
- UNDO restores both the Cue and the Programmer to their exact pre-Store state
- REDO re-applies both the Store and the clear together
- if STORE fails or is cancelled, neither the Cue nor the Programmer changes — no partial mutation
- Selection and Playback are not affected (Selection ≠ Programmer ≠ Playback, see §1)

---

## 6. RELEASE

Canonical workflow (decided; see `docs/SPEC_CONFLICTS_FOR_DECISION.md` C2):

```
RELEASE
→ contextual Release Panel
→ choose family/families
→ ENTER
```

Direct family/parameter-first syntax remains supported as a professional shortcut, and requires ENTER to commit:

```
COLOR RELEASE ENTER
PAN RELEASE ENTER
RED RELEASE ENTER
```

Family-scoped release is Selection-scoped. Examples:

```
RELEASE → COLOR → ENTER   → release only COLOR values from Programmer for current Selection
RELEASE → ENTER           → release ALL Programmer values for current Selection
RELEASE → RELEASE         → clear entire Programmer globally, selection-independent
SHIFT + RELEASE            → release all active playbacks (does not alter Editor values or Selection)
```

Parameter-level release (e.g. `PAN RELEASE ENTER`) must remain supported. It is a third, finer granularity below RELEASE and FAMILY RELEASE and must never be regressed or removed by Release Panel work.

CLEAR dismisses an armed Release state without releasing anything.

Selection-scoped release with no Current Selection performs no mutation and returns a clear message.

Underlying playback/effective values must be revealed after release.

Selection remains active.

---

## 7. Programming Undo vs Structural Show Changes

Programming Undo includes Programmer-oriented changes such as:
- AT
- parameter edits
- HOME
- Preset application/store where appropriate

Structural Show changes such as Patch must NOT enter the normal Programming Undo stack.

Long-term: Programming Undo and Structural Show/Patch history are separate systems.

Do not build a second structural Undo stack unless explicitly requested.

Selection history is also separate from Programming/Edit Undo (decided — see
`docs/SPEC_CONFLICTS_FOR_DECISION_1.md` C5). Selection changes made through CLEAR are console
Actions (`IConsoleAction`, see §2's CLEAR rule), never `IConsoleCommand`, and never enter the
Programming Undo stack. This is a distinct question from whether other selection-building
operations (FIXTURE, GROUP, ODD/EVEN, REVERSE, etc.) are themselves undoable `IConsoleCommand`s —
that remains governed by the existing `IConsoleCommand`/`IConsoleAction` split (§14); the point
decided here is narrower: CLEAR specifically is not part of Programming Undo.

---

## 8. Quick Patch

Patch is structural.

Requirements:
- atomic
- validate-before-mutate where possible
- rollback-safe
- no partial mutation on failure
- no normal Programming Undo entry

Patch may be recorded/replayed in Macros if the operator intentionally records it.

All Patch entry paths must converge on the same Application operation.

---

## 9. Cue Trigger Semantics

CueTriggerMode is:
- Manual
- AutoFollow
- Wait

Trigger belongs to the TARGET/NEXT cue.

### Manual
Previous cue completes → hold → wait for GO

### AutoFollow
Previous cue completes fully → target cue starts immediately

### Wait
Previous cue completes fully → start wait timer → after WaitTime, target cue starts automatically

Important:
- WaitTime starts AFTER previous cue transition completes
- WaitTime must not overlap the previous fade
- AutoFollow ignores WaitTime

Automatic chaining is allowed only for forward progression.

BACK / GO TO / instant SHIFT navigation must not seed an automatic chain.

No list-level "AUTO CONTINUE" feature (decided — see `docs/SPEC_CONFLICTS_FOR_DECISION_1.md` C8).
Automatic chaining is expressed only through the target cue's own CueTriggerMode above; do not add
a separate Cue List-level auto-continue concept alongside it.

---

## 10. Cue Navigation

Normal GO:
- uses cue timing

Normal BACK:
- existing Back/Pause semantics

SHIFT + GO:
- immediate next cue
- zero-time navigation

SHIFT + BACK:
- immediate previous cue
- zero-time navigation

Instant navigation must not arm automatic trigger chains.

### EDIT vs LOAD vs GO TO (decided — see `docs/SPEC_CONFLICTS_FOR_DECISION_1.md` N2)

`CUE X ENTER`, `LOAD CUE X ENTER`, and `GO TO CUE X ENTER` are three distinct operations. Never
collapse them into one meaning:

```
CUE X ENTER      = EDIT — load stored Cue X content into the Programmer for editing.
LOAD CUE X ENTER = LOAD — load Cue X data/state into the Programmer for reuse (e.g. as a starting
                    point for a new Cue), distinct from opening it for in-place editing.
GO TO CUE X ENTER = playback jump to Cue X.
```

EDIT and LOAD both populate the Programmer; GO TO never does (Selection ≠ Programmer ≠ Playback,
§1). Only a playback path that actually starts a Cue (GO, AutoFollow, Wait, GO TO, BACK,
SHIFT+GO, SHIFT+BACK, or any other valid playback entry) fires that Cue's Macro assignments (see
§15 Macro Assignment To Cues below) — EDIT and LOAD are inspection/authoring operations and must
never fire them.

---

## 11. Value Distribution

Supported:

```
FIXTURE 1 THRU 5 AT 20 THRU 60 ENTER
FIXTURE 1 THRU 5 AT 20 THRU 60 THRU 30 ENTER
```

Any number of control points may be used.

Distribution:
- follows ordered Selection
- never sorts by fixture ID
- uses continuous linear interpolation across control points
- one fixture receives first control-point value
- normal AT single-value behavior remains unchanged

---

## 12. Softkeys vs Fixed Keys

ODD / EVEN / REVERSE belong in contextual SOFTKEYS, not permanent fixed keypad controls.

EFFECT is a fixed Command Surface key (decided — see `docs/SPEC_CONFLICTS_FOR_DECISION_1.md` C3;
see also KEY_SPEC §11). This reverses an earlier draft position that treated EFFECT as a
contextual soft key only.

DELETE is a fixed editing key.

Main contextual SOFTKEYS represent console actions/context.

Fixture parameters belong in Encoder/Parameter surfaces, not main fixed softkeys.

The general design principle behind fixed vs. contextual keys is: avoid duplicated semantics — the
same operation implemented two different ways in two places — not "avoid permanent/fixed keys in
general" (decided — see `docs/SPEC_CONFLICTS_FOR_DECISION_1.md` P3). A key may be fixed when its
meaning is stable across contexts; it must still route to one shared Application operation (§3).

---

## 13. Fixture Profiles

Do not hardcode fixture parameter behavior in UI.

Fixture profile should ultimately define:
- Manufacturer
- Model
- Mode
- Footprint
- Channel offset
- Attribute
- coarse/fine mapping
- default/home
- physical range
- AttributeClass
- capabilities

Encoder assignment, HOME, RELEASE family classification, Presets, Effects should derive from profile data.

Never invent calibration, physical ranges or capability metadata. If metadata is missing, fall back honestly to raw DMX.

---

## 14. Engine Layering and Merge

- `DmxConsole.Core` depends on nothing above it (no UI, no network).
- `DmxConsole.Protocols` and `DmxConsole.Fixtures` never depend on `DmxConsole.Web`.
- Undoable changes → `IConsoleCommand` via `CommandDispatcher.Dispatch`.
- Operational actions (GO / BACK / STOP / PAUSE / RESUME / FLASH) → `IConsoleAction` via `DispatchAction`. Actions never touch the Undo stack.
- Multi-step operations → `DispatchBatch` (`CompositeCommand` is a real transaction with rollback).
- `CommandResult` is structured data first. No logic may parse `Message`.
- Merge: Priority is resolved first, then MergePolicy (HTP = max value, LTP = highest per-channel revision).
- Executor faders scale Intensity only. Position / Color / Beam are gated on/off, never scaled.
- Presets and cues are keyed by `ChannelType` / attributes, not DMX addresses. `PresetRef` resolves at playback time; missing or incompatible → contributes nothing, falls through, never throws.
- Destructive Undo requires an explicit confirmed option id from `PeekUndo`.

---

## 15. Macros

`SHIFT + STORE` = Store Options. `SHIFT + LEARN MACRO` = Macro Manager.

`LEARN MACRO` remains the recording workflow (already implemented — see KEY_SPEC §17/§23.7).
Macro playback must use shared console operations, never independent semantics — recorded steps
replay through the same `Dispatch`/`DispatchAction` calls a live operator press would use.

Macro Manager (decided — see `docs/SPEC_CONFLICTS_FOR_DECISION_1.md` N3) is future work, not yet
built. LEARN MACRO records macros; Macro Manager manages already-recorded macros — the two are
distinct. v1 Macro Manager responsibilities:
- list macros
- show macro number/slot
- show name
- rename
- delete
- inspect recorded operations
- assign/reassign slots
- duplicate/copy
- playback/test
- show where macros are referenced (including Cue assignments, see below)
- indicate structural operations (e.g. Patch) recorded within a macro

Out of scope for v1 (future work beyond v1): import/export; advanced step editing.

### Macro assignment to Cues (decided — see `docs/SPEC_CONFLICTS_FOR_DECISION_1.md` M1/M2/M3)

Macros are independent show objects. Cues store references/assignments to macros, not copies.
Approved grammar (new grammar, not yet implemented — see KEY_SPEC §23 gap inventory):

```
STORE MACRO 1 AT CUE 5 ENTER
STORE MACRO 1+2 AT CUE 1 THRU 5 ENTER
DELETE MACRO 1 AT CUE 5 ENTER
DELETE MACRO 1+2 AT CUE 1 THRU 5 ENTER
```

- `ENTER` is mandatory. No mutation before `ENTER` resolves successfully.
- Assignment/removal across a Cue range must be atomic.

Cue Macro Assignments fire on Cue entry/start. This means: playback entry caused by GO,
AutoFollow, Wait, GO TO, BACK, SHIFT+GO, SHIFT+BACK, or any other valid playback path that
actually starts the Cue. They do NOT fire on EDIT, LOAD, inspection, or any other non-playback
operation (see §10's EDIT/LOAD/GO TO distinction).

Macro Manager should show which Cues reference each Macro, and allow inspection of those
assignments. An optional future reverse view (which Macros are assigned to a selected Cue) may be
added later.

---

# Project Reference

## Solution Layout

```
src/DmxConsole.Core          Engine: Universe, Patch, Programmer, CueList, Effects, Executors, Presets, Selection
src/DmxConsole.Protocols     IDmxSender: Art-Net, sACN, Enttec USB Pro / Open DMX
src/DmxConsole.Fixtures      Fixture profile library
src/DmxConsole.Application   Commands, Actions, CommandDispatcher, UndoRedoService, ConsoleContext
src/DmxConsole.Web           Blazor Server UI (ViewModels in Services/, components in Components/ConsoleUi/)
tests/*.Tests                xUnit, one test project per layer
```

## Commands

```
dotnet build DmxConsole.sln
dotnet test
dotnet run --project src/DmxConsole.Web
```

## Documents and Precedence

- `CLAUDE.md` (this file) — binding rules. Highest authority.
- `docs/OPERATOR_UX_ROADMAP.md` — UX direction and current priority order.
- `docs/SPATIAL_PLOT_ARCHITECTURE.md` — forward-looking PATCH / 2D Plot / Spatial Database
  architecture and dependency ordering. Product/architecture decisions, not binding rules; sits
  alongside ROADMAP because it is direction-setting for a not-yet-built area, same as ROADMAP.
- `docs/COMMAND_SURFACE_KEY_SPEC.md` — command grammar, keys, value/time fans.
- `docs/ARCHITECTURE.md` — engine history, Steps A–F (Hebrew).
- `docs/UX_PHILOSOPHY.md` — console research and derived principles.
- `docs/SPEC_CONFLICTS_FOR_DECISION.md` / `docs/SPEC_CONFLICTS_FOR_DECISION_1.md` — decision
  records for cross-document conflicts, round 1 and round 2. Decisions recorded there have already
  been applied into CLAUDE.md/KEY_SPEC; the files themselves are the historical record, not a
  second source of binding rules.

Precedence: CLAUDE.md > ROADMAP > SPATIAL_PLOT_ARCHITECTURE > KEY_SPEC > ARCHITECTURE.
If documents contradict each other: STOP and report. Never resolve silently.

### Known contradiction — do not reintroduce
`ARCHITECTURE.md` Step E still describes per-`AttributeClass` cue timing (`AttributeTiming`).
That was deliberately removed in H1.6 Slice 2. Cue timing is flat per Cue; the only finer
granularity planned is per-channel (ROADMAP §9a). Never add per-family timing storage. This
applies identically to TIME grammar (§4 above, decided — round 2 item N4): family-scoped TIME
syntax is shorthand that expands into per-channel writes — there is no `PositionTime`/`ColorTime`
or any other AttributeClass-keyed timing storage, now or later.

## Blazor Conventions

- Component pattern: `[Inject]` ViewModel → subscribe to `PropertyChanged` / `CollectionChanged` in `OnInitialized` → `InvokeAsync(StateHasChanged)` → unsubscribe in `Dispose`.
- After any dispatch that can change Programmer values (including global Undo/Redo), call `ProgrammerViewModel.RefreshAllFaders()`. This bug has already happened twice.
- Dark theme, touch-first: minimum 44px hit target.
- No HTTPS redirect — local network tool, by design.

---

# Working Rules

## One Slice At A Time

Do not combine unrelated feature work. One slice per Claude Code session (decided — see
`docs/SPEC_CONFLICTS_FOR_DECISION_1.md` P6).

Every task should have:
- clear scope
- audit
- implementation
- tests
- report

Development workflow for a slice: implementer → qa-regression → architecture-reviewer → operator
approval → commit. Agents do not commit or push on their own (see Commit / Push Rule below). If a
contradiction or architecture/product ambiguity remains after implementation, mark it DECISION
REQUIRED and stop rather than silently deciding — see Stop-On-Ambiguity Rule below.

---

# Stop-On-Ambiguity Rule

If a real architectural/product choice appears: STOP.

Report:
1. decision required
2. available options
3. consequences
4. recommendation

Do not silently decide.

Examples:
- new Undo boundary
- Cue navigation semantics
- new object identity semantics
- parser architecture changes
- new persistence model
- conflicting operator behaviors
- contradiction between documents

---

# Testing Rules

Never claim tests passed unless they were run.

For each slice:
- focused tests
- regression tests
- full solution test suite before final report

Where appropriate also verify manually in browser.

---

# Commit / Push Rule

Do NOT commit.
Do NOT push.

Unless explicitly instructed by the user.

Never leave `bin/`, `obj/`, `.vs/` or `*-interrupted*.patch` files as changes.

---

# Final Report Format

Every completed task should report:

## Root Cause / Audit
What was wrong or missing.

## Implementation
What changed.

## Architecture
Why the solution fits existing architecture.

## Tests
Focused tests + full suite result (exact counts).

## Files Changed
Short list.

## Remaining Risks / Open Questions
Only real unresolved items.

## Git
State explicitly:
- committed or not
- pushed or not
