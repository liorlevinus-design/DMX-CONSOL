# DMX-CONSOL Command Surface Key Spec (v1)

Status: authoritative functional specification — v1 grammar/layout definition
Branch: `chatgpt/ux-integration-fixes`
Companion documents: [`OPERATOR_UX_ROADMAP.md`](OPERATOR_UX_ROADMAP.md) (overall operator UX direction), [`ARCHITECTURE.md`](ARCHITECTURE.md) (layering), [`VECTOR_EDITOR_TOOLBAR_REFERENCE.md`](VECTOR_EDITOR_TOOLBAR_REFERENCE.md) (reference material only, not a spec source)

This document is the single source of truth for the Command Surface's key layout, grammar and semantics. It supersedes any inline comment, prior chat discussion, or ad-hoc UI choice that conflicts with it. Where the current implementation disagrees with this document, §23 records the conflict explicitly — the code has not yet been changed to match every rule below purely because this document now states it.

The functional definition below (§1-22) is the operator-facing contract. §23 is this project's own gap analysis against the codebase as of this spec's authoring, required before any implementation work begins.

---

## 1. Core principle

The Command Surface is a fixed, operator-oriented programming surface.

It is not a copy of Vector, Eos or grandMA. We borrow useful interaction ideas, but our grammar and layout are our own.

The operator works with:

**Fixtures / Groups / Families / Presets / Cues / Editor / Playbacks**

Raw DMX remains available explicitly through the DMX key, but is not the normal programming model.

---

## 2. Bare numeric input

Bare numeric input defaults to **FIXTURE**.

Examples:

```
1 ENTER
= FIXTURE 1

1 THRU 10 ENTER
= FIXTURE 1 THRU 10
```

If another object context was explicitly opened, numeric input applies to that object for the current command.

Example:

```
GROUP 3 ENTER
```

After that command terminates, bare numeric input returns to default FIXTURE context.

Do not make object context silently sticky across unrelated completed commands.

---

## 3. Fixed object / access keys

Fixed keys:

- `FIXTURE`
- `GROUP`
- `CUE`
- `DMX`

`DMX` gives direct access to raw DMX addresses / address ranges.

Examples:

```
DMX 1.1 AT 50 ENTER
DMX 1.1 THRU 1.12 AT FULL
```

Support both the chosen `universe.address` format and existing internal address representation without bypassing Application architecture.

DMX commands must still flow through structured console operations, not direct UI writes to output.

---

## 4. Fixed family keys

Physical/fixed family keys placed together:

- `INTENSITY`
- `POSITION`
- `COLOR`
- `BEAM`
- `IMAGE`
- `SHAPE`

These establish the active parameter-family context.

`PRESET` is also a fixed key placed with the family keys, but PRESET is not an independent object type.

Preset recall must always resolve to a family.

Examples:

```
COLOR PRESET 5
POSITION PRESET 3
BEAM PRESET 2
```

If PRESET is invoked without a family context, do not guess. Expose the family choice through contextual soft keys / UI.

---

## 5. Smart Preset Store

`STORE PRESET` opens a semantic Preset Capture surface.

It should inspect the current Editor state and detect which parameter families have actually been touched / are present in the Editor.

For example, if the operator changed:

- Color
- Position
- Zoom / Beam
- Shutter / Image

`STORE PRESET` should offer independent presets for the relevant families.

Each selected family is stored as a separate preset.

Preset scope terminology is already defined elsewhere as:

- Fixture
- Fixture Type
- Compatible

Short labels: `F` / `T` / `C`.

Do not store raw DMX blobs.

Preset naming should support optional smart suggestions based on real profile metadata.

Examples:

- Color: derive a representative color swatch and suggested name when RGB/RGBW/CMY/XY metadata genuinely permits it.
- Beam: names such as "Wide Soft" / "Narrow Sharp" only when profile metadata actually supports that inference.
- Position: do not invent DSC/USL/etc. unless Stage Layout / spatial data supports it.

Suggestions must remain editable and must never silently invent metadata.

---

## 6. HOME

`HOME` is a fixed physical key.

HOME is family-aware.

```
HOME
= apply Home values for all relevant families of the current selection.

COLOR HOME
= apply only Color Home.

POSITION HOME
= apply only Position Home.

BEAM HOME
= apply only Beam Home.

IMAGE HOME
SHAPE HOME
INTENSITY HOME
follow the same rule.
```

There are two conceptual Home layers:

1. **Profile Home**
2. **Show Home**

Show Home overrides Profile Home for the relevant family.

```
STORE HOME
= store current Editor state as Show Home.

STORE COLOR HOME
STORE POSITION HOME
STORE BEAM HOME
etc.
= store Show Home only for that family.
```

Do not rewrite cue/preset show data when defining Home.

---

## 7. RELEASE

`RELEASE` is a fixed physical key.

RELEASE is also family-aware.

```
RELEASE
= release Editor/Programmer values for the current selection.

COLOR RELEASE
= release only Color values from the Editor.

POSITION RELEASE
BEAM RELEASE
IMAGE RELEASE
SHAPE RELEASE
INTENSITY RELEASE
follow the same rule.
```

Release means: **remove Editor ownership and expose the next effective source below it.**

It does NOT mean:

- Home
- 0
- Undo
- Stop playback

Example: if Editor owns Color=Blue but Cue playback underneath owns Amber, `COLOR RELEASE` removes the Editor Color values and LIVE should show Amber again with Cue/Executor provenance.

### Special commands

```
RELEASE ENTER
= Clear Entire Editor / Programmer (all fixtures, all families)

SHIFT + RELEASE
= Release All Playbacks
```

Release All Playbacks:

- releases all currently active playback sources
- includes Executors / Cue Lists / playback-driven Effects / future Submasters
- does not delete show data
- does not alter Editor values
- does not alter selection
- is an `IConsoleAction`
- is not Undoable

---

## 8. CAPTURE ALL

Important: do **not** interpret this as "select all patched fixtures".

The intended operator function is:

```
CAPTURE ALL
= capture the current effective LIVE output into the Editor.
```

This is for workflows such as:

- a cue is running or paused mid-transition
- the director/choreographer says "this is the look I want"
- operator presses CAPTURE ALL
- the currently visible stage/output state is brought into the Editor as editable values

CAPTURE ALL must capture the real current effective output state, including values coming from playback and intermediate transition state where available.

It should preserve semantic fixture/attribute values where they can be resolved.

It must not rewrite existing Cue data automatically.

It is an Editor capture operation.

The button name should be `CAPTURE ALL` for now.

---

## 9. Selection / range keys

Fixed keys near the numeric keypad:

- `THRU`
- `+`
- `-`
- `NEXT`
- `LAST`
- `.`

Semantics:

- `THRU` = inclusive range.
- `+` = union/add.
- `-` = remove from selection in selection grammar, or subtraction in value grammar when unambiguous.
- `NEXT` = next item in current ordered selection. In a discrete parameter context, may mean next discrete slot/value when explicitly defined by that context.
- `LAST` = previous item in current ordered selection. **LAST is NOT last-selection recall.**
- `.` (dot key) = contextual recall / decimal.

Examples:

```
FIXTURE .
= last Fixture selection

GROUP .
= last Group selection

AT .
= last entered level/value
```

Inside numeric entry, "." is decimal point.

Keep Selection History, Recall History and Undo History conceptually separate.

---

## 10. ODD / EVEN

`ODD` and `EVEN` are **NOT** fixed physical keys.

They are contextual Soft Keys.

They should appear when the current fixture selection has an ordered fixture sequence that makes odd/even filtering meaningful.

This includes selections created through GROUP.

Example:

```
GROUP 5 resolves to fixtures: 1 2 3 4 5 6 7 8

ODD  => 1 3 5 7
EVEN => 2 4 6 8
```

Operate on resolved ordered fixture selection, not on group IDs.

---

## 11. EFFECT

`EFFECT` is **NOT** a fixed physical key.

It is a contextual Soft Key that becomes available when fixtures are selected, including when the fixture selection came through GROUP.

Do not make EFFECT a permanent fixed key in the main Command Surface.

---

## 12. Value / Editor keys

Fixed:

- `AT`
- `FULL`
- `HOME`
- `RELEASE`

Examples:

```
1 THRU 10 AT 50 ENTER
1 THRU 10 FULL
COLOR HOME
POSITION RELEASE
```

FULL is semantic 100%, not raw DMX 255.

---

## 13. Record / Edit keys

Fixed:

- `STORE`
- `UPDATE`
- `EDIT`
- `COPY`
- `MOVE`
- `DELETE`

Keep STORE and UPDATE distinct. Do not silently convert one to the other.

```
SHIFT + STORE
= open Store Options / advanced Store workflow.
```

Do not invent final Store Options beyond currently supported/roadmapped behavior.

---

## 14. ENTER

`ENTER` is a fixed physical key near the numeric keypad.

**Recommended v1 rule:** a command that ends in a numeric/reference token and could still accept more input requires ENTER to commit.

Examples:

```
12 ENTER
1 THRU 10 ENTER
GROUP 3 ENTER
CUE 12 ENTER
COLOR PRESET 5 ENTER
1 THRU 10 AT 50 ENTER
```

A command ending in an unambiguous Action key may self-terminate.

Examples:

```
1 THRU 10 FULL
COLOR HOME
POSITION RELEASE
SHIFT + RELEASE
```

ENTER also confirms explicit dialogs / confirmations.

Do not use timeouts to guess that numeric input is complete.

If ENTER is pressed while the command is invalid/incomplete, do not guess. Leave the command active and show a clear reason.

---

## 15. CLEAR

`CLEAR` is a fixed physical key.

CLEAR is NOT Undo. CLEAR is NOT Release. CLEAR is NOT playback Back.

### Priority / behavior

**A. While entering a numeric token:** CLEAR behaves like Backspace one digit at a time.

```
AT 57_
CLEAR
=> AT 5_

CLEAR again
=> AT _
```

**B. If there is no active numeric digit token but the current command has a last logical token/gesture:** CLEAR removes the last logical token/selection gesture.

```
1 THRU 10 + GROUP 3
CLEAR
=> 1 THRU 10
```

GROUP 3 must be removed as one gesture, not fixture-by-fixture.

**C. Consecutive selection gestures:**

```
GROUP 1
GROUP 2
GROUP 5

CLEAR
=> remove GROUP 5 gesture only.
```

**D. CLEAR CLEAR** = clear the entire current command / current selection cycle and return to idle command state.

It does NOT:

- delete show data
- undo a Store that already completed
- release playbacks
- release Editor values

For completed state changes use Undo or Release as appropriate.

If a selection exists and no command line is active:

- `CLEAR` = remove last selection gesture.
- `CLEAR CLEAR` = clear entire selection.

Selection clearing should remain undoable if selection mutation is represented as an undoable command in the existing architecture.

---

## 16. Undo / Redo

Fixed:

- `UNDO`
- `REDO`

Undo/Redo apply to editing/persistent Application commands.

They do not undo runtime actions such as:

- GO
- PAUSE
- FLASH
- SHIFT+RELEASE (Release All Playbacks)

Maintain the existing `IConsoleCommand` / `IConsoleAction` distinction.

---

## 17. Macros

Fixed physical keys:

- `MACRO 1`
- `MACRO 2`
- `MACRO 3`
- `MACRO 4`
- `LEARN MACRO`

MACRO 1–4 execute their assigned macros immediately.

```
SHIFT + MACRO 1 = MACRO 5
SHIFT + MACRO 2 = MACRO 6
SHIFT + MACRO 3 = MACRO 7
SHIFT + MACRO 4 = MACRO 8
```

`LEARN MACRO` = start/stop macro recording workflow.

`SHIFT + LEARN MACRO` = Macro Manager / Macro Edit workflow.

Macro recording should capture structured console operations, not raw mouse coordinates or arbitrary UI gestures.

Do not implement unsafe hidden side effects.

---

## 18. SHIFT

`SHIFT` is a fixed modifier key.

Defined v1 mappings:

```
SHIFT + RELEASE      = Release All Playbacks
SHIFT + STORE        = Store Options
SHIFT + MACRO 1-4    = MACRO 5-8
SHIFT + LEARN MACRO  = Macro Manager / Edit
```

Do not invent meanings for `SHIFT + HOME`, `SHIFT + FULL`, `SHIFT + CLEAR`, `SHIFT + AT`, etc. Leave undefined combinations unassigned.

---

## 19. SETUP

We want SETUP functionality comparable in role to Vector's Setup entry point: a fast gateway to system/show configuration.

It may be represented as:

- a small fixed utility key, or
- a SHIFT-accessed utility

For the first implementation, prefer a small utility key in the command-surface header / utility area rather than consuming a main programming-key position.

SETUP should route to existing/future areas such as:

- Patch
- Fixture Library
- Show settings
- Output / protocols
- Workspace
- User preferences
- Network / universes
- Playback settings

Do not build missing setup subsystems just to populate the screen. Only expose existing destinations and clearly disabled placeholders where appropriate.

---

## 20. Command Surface layout

Use the latest operator-approved visual arrangement as the layout direction.

The important spatial relationships are:

**LEFT ACTION BANK:**

- FIXTURE / GROUP / CUE / DMX
- Store/Edit verbs
- Undo/Redo
- Macro controls
- SHIFT
- CLEAR

**RIGHT COMMAND BANK:**

- family keys together
- PRESET next to family keys
- HOME / RELEASE / FULL together
- NEXT / LAST close to numeric keypad
- numeric keypad
- AT
- THRU
- +
- -
- .
- ENTER

CAPTURE ALL should live in the selection/action area, not the numeric bank.

ODD / EVEN and EFFECT remain contextual Soft Keys in the upper context bar.

SETUP should be visually treated as a utility/system key.

The numeric cluster should approximately follow this concept:

```
    7   8   9      AT
    4   5   6      THRU
    1   2   3      +   -
    0      .       CLEAR   ENTER
```

NEXT / LAST should sit immediately above or adjacent to this cluster.

Do not copy the generated concept image pixel-for-pixel. Use it as spatial guidance only. Preserve the existing DMX-CONSOL visual language and responsive layout system.

---

## 21. Architecture requirements

All input paths must continue to converge on Application-layer semantics.

Do not implement business logic directly in Razor components.

GUI buttons, keyboard shortcuts, future hardware, CLI, macros and voice should invoke the same structured operations.

Maintain:

- Selection
- Programmer/Editor
- Preset
- Cue
- Playback
- Live/Provenance
- CommandDispatcher
- `IConsoleCommand` / `IConsoleAction`

boundaries.

CAPTURE ALL must use the effective-output model, not reimplement merge logic in the UI.

HOME / RELEASE family logic should use semantic attributes/families, never hard-coded raw DMX assumptions.

---

## 22. Implementation order (process reference)

This is the working order this project follows to build v1 against this spec — reference only, not part of the operator-facing contract in §1-21:

1. Inspect current command-surface / toolbar / command parser implementation.
2. Write/update this document with the complete semantics above.
3. Commit the documentation separately.
4. Report any conflicts with current behavior before implementation (§23 below).
5. Implement the visual Command Surface layout.
6. Wire only semantics that already have Application support cleanly.
7. Add missing Application commands/actions only where required by this v1 spec.
8. Do not fake unsupported behavior.
9. Add tests for grammar and high-risk actions (bare numeric defaults to Fixture, object context reset, family HOME, family RELEASE, RELEASE ENTER, SHIFT+RELEASE, CLEAR hierarchy, LAST vs "." recall, ODD/EVEN with Group-resolved selection, CAPTURE ALL, DMX ranges, macro Shift bank).
10. Run full tests.
11. Commit implementation separately.
12. Report commits, files changed, exact test totals, manual verification checklist, remaining unsupported pieces.

---

## 23. Known conflicts / gaps vs. current implementation (authored alongside this spec, before any implementation change)

This section is the required "document the conflict before changing behavior" checkpoint. Nothing in this section has been changed yet — it is a factual inventory of where `chatgpt/ux-integration-fixes` (as of this spec's commit) disagrees with, or simply doesn't yet cover, §1-21 above.

### 23.1 Family granularity mismatch (real conflict — needs a decision, not just new code)

The spec's family keys are six: **Intensity / Position / Color / Beam / Image / Shape** (§4, §6, §7). Two different family groupings already exist in the codebase and neither is exactly this list used consistently everywhere:

- **`AttributeClass`** (`DmxConsole.Core/ChannelType.cs`) — four values: `Intensity, Position, Color, Beam, Other`. This is what `ReleaseCommand`/`ProgrammerChannelCommandBase` (Programmer release/clear), `Preset`/`PresetLibrary` (Preset storage and recall), and Cue attribute-timing are all built on today. Under this model, Gobo/Prism (Image) and Shutter/Strobe (Shape) both fall under `Beam` or `Other` — there is no way to Release/Store-Preset "only Image" or "only Shape" today.
- **`EncoderCategory`** (`DmxConsole.Core/EncoderCategory.cs`) — six values, exactly matching this spec's family list (`Intensity, Position, Color, Beam, Image, Shape`), grounded in the same Vector reference this project already used for the Encoder Drawer. It is currently used **only** by the Encoder Drawer's own value-editing/Home-per-channel logic, not by Release, Store, or Preset.

**Consequence:** `COLOR HOME`/`COLOR RELEASE`/`COLOR PRESET n` map cleanly onto existing `AttributeClass.Color`. `IMAGE HOME`/`IMAGE RELEASE`/`SHAPE HOME`/`SHAPE RELEASE` do **not** — there is no existing family-scoped command that can distinguish Image from Shape from Beam today. Implementing §6/§7 faithfully for all six families requires either (a) migrating `ReleaseCommand`/`Preset`/Cue timing from `AttributeClass` to `EncoderCategory`, or (b) building a second, parallel family-scoped command path keyed on `EncoderCategory` specifically for the Command Surface. Given the project's own "one business logic path per operation, never a duplicate" rule (§21 here, and the standing architecture rule), (a) is the architecturally correct direction, but it is a wider-blast-radius change (touches Preset storage format, Cue per-attribute timing, existing tests) than a v1 Command Surface slice should absorb silently. **This needs an explicit decision before implementation touches Release/Preset family scoping** — flagged here rather than resolved unilaterally.

### 23.2 Preset Scope (F/T/C) does not exist yet

`Preset` (`DmxConsole.Core/Presets/Preset.cs`) has `Class` (an `AttributeClass`), `Name`, `Number`, `Values` — no `Scope` field of any kind. The Fixture/Fixture Type/Compatible scope model this spec references (§5) as "already defined elsewhere" is defined only in `OPERATOR_UX_ROADMAP.md`'s prose, not implemented in `DmxConsole.Core`. This is a pre-existing gap (the roadmap itself lists Preset Scope as "not yet to be done"), not something this spec introduces — recorded here because §5's Smart Preset Store depends on it.

### 23.3 Raw DMX addressing does not exist

There is no `CommandTokenKind.Dmx`, no `universe.channel` address grammar in `CommandComposer`, and no Application-layer command that writes to the Programmer by raw `(universe, address)` pair rather than through a `PatchedFixture`/`ChannelType`. `DMX 1.1 AT 50 ENTER` (§3) has no implementation path today.

### 23.4 "Show Home" layer does not exist

Today there is exactly one Home concept: `FixtureChannel.DefaultValue` (Profile Home), consumed directly by `EncoderDrawerViewModel.Home()`. There is no per-show, per-family override layer, and no `STORE HOME` / `STORE COLOR HOME` command. §6's two-layer Home model (Profile Home + Show Home, with Show Home overriding per family) is entirely new.

### 23.5 CAPTURE ALL does not exist

No Application command reads `ConsoleContext.EffectiveOutput` (the effective-merged-output reader already used by Fixtures/Channels LIVE and by `CueList`'s `AllParamsForSelected`/`AllParamsIfActive` Store filters) and writes the resolved per-fixture/per-attribute values back into the Programmer. §8 requires this as a new Editor-capture operation built on the existing effective-output model — the model to read from already exists (`IEffectiveOutputReader`/`ConsoleContext.EffectiveOutput`), the write-back command does not.

### 23.6 Release All Playbacks does not exist

`PlaybackActions.cs` has per-Executor `GoAction`/`BackAction`/`StopAction`/`PauseAction`/`ResumeAction`/`FlashPressAction`/`FlashReleaseAction`, each acting on one `Executor`. There is no action that iterates `ExecutorBank` (and, per §7, playback-driven Effects and future Submasters) and stops/releases all of them in one gesture. `SHIFT + RELEASE` (§7, §18) has no implementation.

### 23.7 Macro system does not exist

No `CommandTokenKind.Macro*`, no macro storage type, no Learn/record workflow, no Macro Manager. `EditorObjectType.Macro` exists as an enum value (reserved, like `Preset`/`Executor`/`Submaster`) but has no registered soft-key tree and no backing service. §17/§18's Macro 1-8 + Learn Macro + Macro Manager are entirely new.

### 23.8 SETUP entry point does not exist

No utility key or route exists today that gathers Patch/Fixture Library/Show settings/Output/Workspace/User preferences/Network/Playback settings behind one gateway. §19 is new; per its own text, this should route to **existing** destinations only (Patch panel, Workspace picker, etc. already exist as Views) plus honestly-disabled placeholders for the rest — not new subsystems.

### 23.9 NEXT / PREVIOUS are wired in the UI but not in the grammar (real gap, not by design)

`CommandSurface.razor` already renders `Next`/`Prev` buttons that call `Sfc.PressToken(CommandTokenKind.Next/Previous)`, and `NextFixtureCommand`/`PreviousFixtureCommand` already exist in `DmxConsole.Application/Commands/Selection/`. But `CommandComposer.Build`'s state machine only recognizes `Number`/`Fixture`/`Group` as a valid first token — pressing Next/Previous today falls into the `else` branch and returns `Incomplete(preview, "Expected a fixture number, Fixture, or Group.")`. The buttons are visibly present but non-functional. This predates this spec; §9's NEXT/LAST semantics give the concrete rule needed to finish wiring them (NEXT/LAST operate on the current ordered selection, not as an object-type prefix).

### 23.10 LAST is currently unimplemented; "." recall already matches spec closely

§9 is explicit that `LAST` (previous item in the ordered selection) is a **different** operation from `.` (last-selection/last-value recall). Today's `CommandTokenKind.Recall` (the `.` key) already implements `FIXTURE .` / `GROUP .` / `AT .` almost exactly as §9 describes (see `CommandComposer.ResolveRecall` and the `AT .` branch in `Build`), including keeping Selection History (`SelectionCycleState`) and Recall (`LastSelection`/`LastGroupNumber`/`LastAtPercent`) as separate concepts already — this part of the codebase already agrees with the spec. There is currently no `LAST` token/command at all (distinct from `Previous`, which moves a single-fixture cursor per `PreviousFixtureCommand` — whether `LAST` in this spec is meant to be the same thing as the existing `Previous`/`PreviousFixtureCommand`, or a distinct "step back in the ordered selection" operation, is worth confirming during implementation rather than assumed).

### 23.11 ODD / EVEN already match the spec's "contextual, not fixed" rule

`fixture.odd` / `fixture.even` are already registered as `ContextAction` soft keys (not fixed Command Surface keys) in `SoftKeyRegistryBuilder`, dispatching to `SelectionViewModel.SelectOddCommand`/`SelectEvenCommand`, which already operate on the resolved ordered `Selection`, not group IDs. This already agrees with §10 — no conflict. It is not yet exposed as a soft key specifically in the "upper context bar" location §20 describes; that is a layout gap, not a semantics gap.

### 23.12 EFFECT soft key does not exist yet

No `EditorToolBar` context currently registers an `EFFECT` key anywhere. §11 requires it as a contextual soft key whenever fixtures are selected (including via GROUP) — new, not a conflict, just unbuilt.

### 23.13 FULL (semantic 100%) does not exist as a token/action

There is no `CommandTokenKind.Full` and no handling for a bare `FULL` self-terminating action in `CommandComposer`. `AdjustIntensityCommand`'s existing `Absolute` operation already supports "set to 100" numerically (`AT 100 ENTER` already works), so the underlying Application-layer capability exists — only the `FULL` token/self-terminating grammar path is missing.

### 23.14 ENTER-optional self-termination is partially supported already

The existing grammar already requires `ENTER` to finalize a plain selection/AT command (`1 THRU 10 AT 50` does not resolve until `ENTER`), matching §14's default rule. There is currently no self-terminating Action-key path at all (no `HOME`, `RELEASE`, `FULL` tokens exist yet to self-terminate) — so §14's "ends in an unambiguous Action key may self-terminate" rule has nothing to test against yet; it becomes relevant only once HOME/RELEASE/FULL tokens are added.

### 23.15 CLEAR hierarchy already matches §15 closely for the cases that exist today

`CommandSurfaceViewModel.PressClear` / `CommandComposer.Push(Clear)` already implement: digit backspace is handled separately via `PressBackspace` (not currently multiplexed onto the same physical Clear key — see below); a Clear with active tokens removes the whole in-progress command line in one step (not literally "last logical token" when a composed multi-clause command line is involved — see caveat below); an empty-line Clear removes the last selection *gesture* as one unit (§15-B/C, already correct, including whole-Group-as-one-gesture); a second consecutive empty-line Clear clears the entire selection (§15-D, already correct: `_clearArmedForFullSelection`).

**Caveat worth flagging, not yet a confirmed conflict:** §15-A describes `CLEAR` itself behaving as digit-backspace while a numeric token is being entered ("AT 57_ / CLEAR => AT 5_"). Today, digit entry and Clear are two separate physical keys/handlers (`PressBackspace` vs `PressClear`) — pressing Clear while mid-digit-entry today does **not** backspace the digit, it takes the empty-line-Clear path against a stale composer state, since `PressClear` unconditionally zeroes `_pendingDigits` without treating that as a backspace step. This needs to be reconciled with §15-A during implementation (most likely: make the digit-backspace behavior the first branch of `PressClear`, mirroring what `PressBackspace` already does for `⌫`, rather than two independently-firing key handlers).

### 23.16 SHIFT does not exist as a modifier concept anywhere

No `Shift` state exists on `CommandSurfaceViewModel`, no visual Shift key, no physical-keyboard Shift-chord handling in `KeyboardCommandMap` beyond the browser's native `KeyboardEventArgs.ShiftKey` (unused today). All of §18's mappings are new.

### 23.17 STORE/UPDATE/DELETE/EDIT/COPY/MOVE already exist per-object-family, generically dispatched

§13's fixed Record/Edit keys already exist as a pattern: `SoftKeyRegistryBuilder` registers `STORE`/`UPDATE`/`DELETE` per context (`Cue`, `Group`, `Fixture`) and `EditorToolBarViewModel.RunContextAction` resolves them generically via `(action, ObjectType)` onto existing RelayCommands — exactly the "same key, context-dependent behavior, never duplicated business logic" rule this spec requires (§21). `EDIT`/`COPY`/`MOVE` are already present as registered-but-`NotImplemented` keys (honest placeholders, not fake buttons) for every object family. This is the closest thing in the codebase today to what this spec asks the whole Command Surface to look like, and should be the template for the family-key/Home/Release wiring described above, not a separate mechanism.

### 23.18 Undo/Redo boundary already matches §16 exactly

`IConsoleCommand` vs `IConsoleAction`, `CommandDispatcher.Dispatch` (pushes to `UndoRedoService`) vs `CommandDispatcher.DispatchAction` (never touches `UndoRedoService`) already implement §16's rule precisely, including for every existing Playback action (`GoAction`/`PauseAction`/`FlashPressAction`/etc., all `IConsoleAction`, none Undoable). No conflict; `SHIFT + RELEASE` (§23.6) should be built as a new `IConsoleAction` following this exact existing pattern.

---

**Summary for implementation planning:** §23.1 (family granularity) is the one conflict that needs an explicit decision before Release/HOME/Preset family-scoping work begins, since it affects existing `AttributeClass`-based code paths (Preset storage, Cue timing) beyond the Command Surface itself. §23.2–§23.8 and §23.12–§23.13, §23.16 are net-new capabilities with no existing conflicting behavior to reconcile — they are additive. §23.9, §23.10, §23.15 are real but narrow gaps/bugs in already-existing Command Surface code that this spec's grammar rules resolve unambiguously. §23.11, §23.17, §23.18 are confirmations that existing code already matches this spec and should be reused, not rebuilt.
