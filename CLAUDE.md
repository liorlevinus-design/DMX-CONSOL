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

## 2. Last Selection

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

CLEAR:
- clears Current Selection
- does NOT overwrite Last Selection

`FIXTURE .` recalls Last Selection.

Do not derive Last Selection from Programmer contents.

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

Preset family behavior:
- one Preset object per AttributeClass pool
- multi-family store creates multiple Preset objects
- same number may be used in each pool

UPDATE:
- merge touched values
- preserve untouched existing values

OVERWRITE:
- replace stored content entirely with current store result

CANCEL:
- no mutation

Multi-family STORE must be atomic.

---

## 6. RELEASE

Family-scoped release is Selection-scoped.

Examples:

```
RELEASE → COLOR → ENTER   → release only COLOR values from Programmer for current Selection
RELEASE → ENTER           → release ALL Programmer values for current Selection
RELEASE → RELEASE         → clear entire Programmer globally
```

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

DELETE is a fixed editing key.

Main contextual SOFTKEYS represent console actions/context.

Fixture parameters belong in Encoder/Parameter surfaces, not main fixed softkeys.

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

Precedence: CLAUDE.md > ROADMAP > SPATIAL_PLOT_ARCHITECTURE > KEY_SPEC > ARCHITECTURE.
If documents contradict each other: STOP and report. Never resolve silently.

### Known contradiction — do not reintroduce
`ARCHITECTURE.md` Step E still describes per-`AttributeClass` cue timing (`AttributeTiming`).
That was deliberately removed in H1.6 Slice 2. Cue timing is flat per Cue; the only finer
granularity planned is per-channel (ROADMAP §9a). Never add per-family timing storage.

## Blazor Conventions

- Component pattern: `[Inject]` ViewModel → subscribe to `PropertyChanged` / `CollectionChanged` in `OnInitialized` → `InvokeAsync(StateHasChanged)` → unsubscribe in `Dispose`.
- After any dispatch that can change Programmer values (including global Undo/Redo), call `ProgrammerViewModel.RefreshAllFaders()`. This bug has already happened twice.
- Dark theme, touch-first: minimum 44px hit target.
- No HTTPS redirect — local network tool, by design.

---

# Working Rules

## One Slice At A Time

Do not combine unrelated feature work.

Every task should have:
- clear scope
- audit
- implementation
- tests
- report

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
