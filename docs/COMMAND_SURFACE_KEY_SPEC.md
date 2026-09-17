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

**Status: implemented (DMX DIRECT ADDRESSING slice).** Grammar: `DMX <Universe.Address> (THRU <Universe.Address>)? (AT number | FULL | RELEASE)?`. `Universe.Address` is one resolved `CommandToken.DmxAddress` (Universe, Address) pair - the operator's own `AT`-value/Recall `.` remain fully separate and undisturbed, resolved via `CommandComposition.ExpectedNext` (never a Razor-level string hack). `SetDmxAddressCommand`/`ReleaseDmxAddressCommand` (`DmxConsole.Application.Commands.Programmer`) write/release raw `(Universe, Address)` pairs directly through `Programmer.SetChannel`/`ClearChannel` - which is already fixture-agnostic by design - so a DMX-addressed value is genuinely Editor-owned with normal provenance/Undo/Release, independent of any Fixture Profile. Same-Universe ranges only in v1; a cross-Universe range is honestly rejected, never silently crossed or clamped. Address validated to 1-512. **Operator-facing Universe numbering is 1-based** (`DMX 1.1` addresses the first Universe) - translated to the engine's internal 0-based `universeId` exactly once, in `CommandComposer.ToInternalUniverseId`, at the single point the resolved `(Universe, Channel)` pairs are built; Universe validated to ≥1 (operator-facing) - Universe 0 and negative Universes are honestly rejected, never silently reinterpreted. **Resolved (was a known gap):** a Universe with zero patched fixtures anywhere now reaches real `GetEffectiveValue`/output too - `DmxAddressCommandBase.Execute` allocates the target Universe itself via the new `IUniverseAllocator`/`ConsoleContext.UniverseAllocator` (implemented by `DmxOutputEngine`, idempotent, never touches `Patch`/`PatchedFixture` - no dummy fixture or fake patch entry is ever created) before writing to the Programmer, so DMX direct addressing is now the explicit allocation mechanism for a Universe nothing has ever patched into. Object/domain context is per-command only, never sticky - the next bare numeric command always defaults back to `FIXTURE`.

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

### PARAMETER RELEASE (third granularity, below RELEASE and FAMILY RELEASE)

A third, finer level: releasing one explicitly addressed semantic parameter, leaving every other parameter — including siblings in the same family — untouched.

```
PAN RELEASE
= release Pan only, leaving Tilt and other Position parameters untouched.

ZOOM RELEASE
= release Zoom only, leaving Focus/Iris/etc. untouched.
```

PARAMETER means the complete semantic fixture parameter, not necessarily one raw DMX byte — a coarse/fine parameter (e.g. 16-bit Pan = Pan + PanFine) releases as one atomic unit; addressing either half releases the whole parameter.

`RELEASE`, `FAMILY RELEASE`, and `PARAMETER RELEASE` are three structurally distinct granularities — never collapsed into one another. Only bare `RELEASE` (§ below) arms the `RELEASE ENTER` escalation; `FAMILY RELEASE` and `PARAMETER RELEASE` are self-terminating like any other unambiguous action and never arm it.

Implemented via `ReleaseParameterCommand` (`DmxConsole.Application.Commands.Programmer`), reusing `ProgrammerChannelCommandBase`'s existing snapshot/undo machinery through a new `SelectChannels` override point rather than duplicating it. The parameter's full component set comes from `ChannelTypeExtensions.SemanticComponents` (`DmxConsole.Core`). Keypad entry point: `CommandSurfaceViewModel.PressParameter(ChannelType)` → `CommandToken.Parameter(ChannelType)` → `CommandComposer`'s `"<Parameter> RELEASE"` grammar. No visual keypad button exists yet for picking an individual parameter (the six fixed family keys are the only parameter-family selectors in the current layout) — the Application/grammar layer is complete and tested; a parameter-picker UI (e.g. listing the ChannelTypes actually present on the current selection) is a follow-up UI slice, not built here.

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

**Status: implemented.** `CaptureAllCommand` (`DmxConsole.Application.Commands.Programmer`) - an undoable `IConsoleCommand`, reusing `ProgrammerChannelCommandBase`'s existing snapshot/undo machinery unchanged. For every patched fixture's every channel, if `IEffectiveOutputReader.GetOwner` is non-null (the same "LiveOnStage" ownership test `LiveChannelState` already uses - no new provenance system), the channel's current `GetEffectiveValue` is written into the Programmer. Scoped to the whole patch, never the current Selection (per this section's own wording); gated on ownership, never on Intensity>0, so a fixture live only via Position/Color/Beam/Image/Shape at Intensity 0 is still captured. Self-terminating (`CommandTokenKind.CaptureAll`), never requires ENTER. Command Surface entry point: `CommandSurfaceViewModel.PressCaptureAll()` - deliberately bypasses the generic token-composition completion path (which records a `SelectionCycleState` gesture on every successful dispatch) since CAPTURE ALL never touches Selection at all. Manually verified end-to-end: Cue A (20%) → Cue B (80%) over an 8s fade, paused mid-transition at 52%, CAPTURE ALL captured exactly 52% (never 20 or 80), playback stayed paused on Cue 2 throughout, and RELEASE afterward correctly revealed the underlying paused-at-52% Cue value again with its original provenance.

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

## 9A. Parameter-level Value and Time Fans

Added after v1's initial functional definition, per explicit operator instruction — an authoritative addition, not a proposal.

### Principle

Every fixture parameter (channel) supports its own **Fade Time** and **Delay Time**, independent of every other parameter on the same fixture and independent of every other fixture. Cue-level timing (`docs/COMMAND_SURFACE_KEY_SPEC.md` §6/§13's `CueTiming` — `TIME-IN`/`TIME-OUT`/`DELAY-IN`/`DELAY-OUT`) is only the **default** a parameter falls back to when it has no explicit override of its own.

`THRU` is not limited to a two-endpoint range. It also expresses an **ordered multi-point fan**: a sequence of control-point values distributed across the resolved, ordered fixture selection, with linear interpolation between adjacent control points.

### Examples

```
FIXTURE 1 THRU 6 AT INTENSITY 30 THRU 80
= distribute intensity linearly from 30 to 80 across the ordered selection (2 control points, 6 fixtures).

FIXTURE 1 THRU 6 INTENSITY TIME 4 THRU 8
= distribute individual Intensity fade times from 4s to 8s across the same ordered selection.

FIXTURE 1 THRU 10 AT INTENSITY 30 THRU 80 THRU 30
= symmetric multi-point value fan - 3 control points (30, 80, 30). The 80 peak sits at the
  center of the ordered selection; with an even fixture count the center peak is duplicated
  across the two middle fixtures to preserve symmetry (there is no single "middle" position).

FIXTURE 1 THRU 10 INTENSITY TIME 2 THRU 8 THRU 2
= the same multi-point fan behavior applied to individual parameter Fade Times instead of values.
```

### Requirements

- **Values, Fade Time, and Delay Time all distribute through the same shared multi-point fan engine.** One algorithm, parameterized by what quantity is being distributed (a byte-range value, or a `TimeSpan`) — never three separate implementations of "spread N control points across M targets."
- Distribution follows the resolved **selection order** — the exact order fixtures were added to the current selection (`SelectionCycleState`/`FixtureSelection`'s own ordering), including the order fixtures arrived via a resolved **Group** (§10's "operate on resolved ordered fixture selection, not group IDs" applies identically here). Never re-sorted by fixture number.
- Each fixture/parameter **receives and stores its own calculated concrete value/time** — the fan is resolved once, at execution time, into N concrete per-fixture numbers; nothing downstream (Programmer, Cue) stores "a fan," only plain per-channel values and per-channel timing overrides, exactly like any other Programmer write.
- Any number of control points may be expressed through repeated `THRU` (`30 THRU 80 THRU 30` is 3 points defining 2 linear segments; `30 THRU 50 THRU 70 THRU 20` is 4 points / 3 segments, and so on) — the engine is not hardcoded to 2 or 3 points.
- **Odd-length selections get one exact center fixture** (the middle control point's value lands on exactly one fixture). **Even-length selections duplicate the center peak** across the two middle fixtures, so the fan stays symmetric rather than landing the peak arbitrarily on one side.
- **Reverse ranges must work** — both a descending fixture range (`FIXTURE 10 THRU 1`) and a descending control-point value (`80 THRU 30`) distribute correctly; the fan engine works on "N ordered targets, M ordered control points," not on the sign of any particular number.
- **Explicit parameter timing overrides Cue timing** — once a channel has its own Fade/Delay override (from a Time-fan command, or a future single-parameter `INTENSITY TIME 4` with no fan), playback for that specific channel uses the override instead of the owning Cue's flat `CueTiming`, for as long as the override exists.
- **Releasing a parameter-time override falls back to Cue timing** — a family/parameter `RELEASE` (§7) that clears a channel's Editor-owned timing override removes the override entirely; that channel then reverts to whatever `CueTiming` the owning Cue already specifies. This is the same "Release removes Editor ownership, exposes what's underneath" principle §7 already defines, applied to timing instead of value.
- **STORE and UPDATE must preserve individual parameter Fade/Delay values** — recording or updating a Cue while a channel has a live parameter-timing override captures that override into the stored Cue (per-channel, alongside its `CueValue`), not just the channel's value. A Cue that's never had any per-channel override behaves exactly as today (flat `CueTiming` only) — this is a strictly additive, opt-in layer per channel, not a mandatory per-channel field every Cue must populate.
- **Do not implement a separate family-timing storage model.** H1.6 Slice 2 deliberately removed grandMA3-style per-`AttributeClass` Cue timing in favor of one flat `CueTiming` per Cue (`docs/COMMAND_SURFACE_KEY_SPEC.md` §23 and `Cue.cs`'s own doc comment) — that decision stands. A **family-scoped operation** (e.g. `COLOR TIME 5`) is a convenience that applies the *same per-channel override mechanism* to every channel in that family at once; there is no second storage bucket keyed by `AttributeClass` anywhere in the data model. Per-channel is the only storage granularity below the flat Cue default.
- **Do not map generic `Speed` to `Position` without explicit fixture-profile semantics** (already corrected in the family-model migration — restated here since it's directly relevant to any future per-parameter timing UI for movement-adjacent channels).

### Precedence (for a single channel, playback time)

```
1. This channel's own explicit timing override (if the Editor/Cue-stored data has one) - highest priority.
2. The owning Cue's flat CueTiming - the default every channel falls back to.
```

No third layer, no per-family layer — consistent with the point above.

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

### 23.1 Family granularity mismatch — RESOLVED (migrated to one unified six-family `AttributeClass`)

**Status: done, in a commit following this spec's own commit.** The spec's family keys are six: **Intensity / Position / Color / Beam / Image / Shape** (§4, §6, §7). Two different family groupings existed in the codebase at the time this spec was first authored:

- **`AttributeClass`** (`DmxConsole.Core/ChannelType.cs`) — originally four values: `Intensity, Position, Color, Beam, Other`. This is what `ReleaseCommand`/`ProgrammerChannelCommandBase` (Programmer release/clear), `Preset`/`PresetLibrary` (Preset storage and recall), and the Encoder Drawer/Fixtures LIVE grouping now all key off.
- **`EncoderCategory`** (`DmxConsole.Core/EncoderCategory.cs`, now removed) — six values, matching this spec's family list, used only by the Encoder Drawer.

**Resolution (explicit operator direction):** `AttributeClass` was expanded in place to the authoritative six families (`Intensity, Position, Color, Beam, Image, Shape`, plus `Other` as the non-selectable fallback for Macro/ControlFunction/Generic channels) and `EncoderCategory` was deleted — every consumer (Release, Presets, the Encoder Drawer, Fixtures LIVE) now shares this one type. The corrected default `ChannelType → AttributeClass` mapping (per explicit operator correction, overriding this spec's earlier silence on the point):

- **Beam**: Focus, Zoom, **Prism**, **Shutter**, **Strobe** (Prism and Shutter/Strobe are beam effects, never Image/Shape by default)
- **Image**: Gobo, GoboRotation (and, per the correction, "animation and projected-image attributes" generally — no such `ChannelType` exists yet beyond Gobo/GoboRotation)
- **Shape**: no `ChannelType` maps here by default today — nothing in the enum represents a framing-shutter/blade/frame-rotation/keystone mechanism. The family exists (selectable, never hidden), it simply has no member yet. A future fixture-profile-level override (not built) is the intended way to reclassify a specific fixture's Shutter as a framing device instead of a beam effect, per the operator's explicit "fixture-profile semantics may override a default mapping, but do not create another family taxonomy."
- **Position**: Pan, PanFine, Tilt, TiltFine. `Speed` stays `Other`/unclassified by default (explicit operator correction) - a generic "Speed" channel could mean movement speed, color-wheel speed, or gobo-rotation speed depending on the fixture, so it is never guessed into Position (or any family) without explicit fixture-profile semantics that don't exist yet.
- Intensity/Color unchanged.

`ReleaseCommand`/`ProgrammerChannelCommandBase`/`Preset` needed **zero code changes** beyond the enum gaining two members — they were already generic over `AttributeClass`. `ProgrammerViewModel` gained `ClearImageCommand`/`ClearShapeCommand` (mirroring the existing per-family Clear commands) and `ProgrammerPanel.razor`/`PresetViewModel.AttributeClassOptions`/`FaderBank.razor`'s grouping order were extended to expose the two new families — see the implementation commit for the full file list.

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

### 23.19 Parameter-level Value/Time Fans (§9A) — entirely new; no existing engine, storage, or grammar

§9A was added after the rest of this spec's first pass, per explicit operator instruction. Nothing in the codebase today implements any part of it:

- **No shared multi-point fan/interpolation engine exists anywhere.** The closest existing concept is `EffectPhaser.Spread` (`DmxConsole.Core/Effects/EffectPhaser.cs`) — a per-fixture **phase offset** for a running, continuously-evaluated effect. It solves a related-but-different problem (staggering *when* each fixture repeats a cycle) and its math does not directly produce "N linearly-interpolated concrete values across M ordered targets with symmetric center-peak handling for even counts." §9A's fan engine is new, standalone code — it should not be bolted onto `EffectPhaser`.
- **`CommandComposer`'s grammar has no concept of a value fan at all.** Today, `AT <number>` (`CommandComposer.Build`'s `At` branch) applies exactly one numeric value, uniformly, to every resolved target via `AdjustIntensityCommand(targets, Absolute, atValue)` — there is no path where `THRU` appears *after* `AT`, and no path where a family keyword (`INTENSITY`) appears inside the value-clause grammar at all (`INTENSITY`/`POSITION`/`COLOR`/`BEAM` are reserved `CommandTokenKind` values per `CommandTokenKind.cs`'s own comment — "not yet interpreted by the composer"). `FIXTURE 1 THRU 6 AT INTENSITY 30 THRU 80` requires new grammar, not an extension of the existing single-clause `At` handling.
- **No `TIME` token/grammar exists in `CommandComposer`** for parameter-level timing entry (distinct from `SoftKeyRegistryBuilder`'s already-registered-but-`NotImplemented` `cue.time.timein`/etc. soft keys, §23.9's sibling gap — those are Cue-level, entered through a form today, not through the keypad's own grammar).
- **No per-channel timing override storage exists.** `CueValue` (`DmxConsole.Core/Engine/CueValue.cs`) carries `Kind`/`ChannelType`/`AbsoluteValue`/`PresetId` — no `FadeTime`/`DelayTime` fields. `Cue.Timing` (`DmxConsole.Core/Engine/Cue.cs`) is one flat `CueTiming` for the whole Cue (H1.6 Slice 2's deliberate replacement of the earlier per-`AttributeClass` model — see that type's own doc comment) — there is no dictionary or per-channel structure to hang an override off of. `Programmer` (`DmxConsole.Core/Engine/Programmer.cs`) stores only a value + knockout flag per `(universe, channel)` — no timing field either. A per-channel timing override (Editor-held, then optionally captured into a Cue on Store/Update) needs a new data structure at both the Programmer layer (to hold a live, not-yet-stored override) and the Cue/`CueValue` layer (to persist one that was captured) — genuinely new, not a gap in an existing mechanism.
- **Precedence/Release interaction is new but composes cleanly with existing pieces.** §9A's 2-layer precedence (per-channel override → Cue's flat `CueTiming`) and "Release removes the override, falls back to Cue timing" reuse `ReleaseCommand`/`ProgrammerChannelCommandBase`'s existing snapshot-and-restore pattern (§23.1's resolution) in spirit — once a `FadeTime`/`DelayTime` field exists on whatever holds the per-channel override, releasing it is the same "clear this holder for this channel" operation Release already performs for values, just on a second piece of per-channel state.
- **Selection order is already available and correct for this purpose.** `SelectionCycleState`/`FixtureSelection` (§9, §23.10) already preserve operator-chosen order, including through Group resolution — the fan engine can consume `context.Selection.Items` (or an explicitly-resolved ordered list from a `CommandComposer` clause) directly; no new ordering mechanism is needed, only a new consumer of the existing one.
- **This does not reopen the H1.6 Slice 2 decision.** That decision removed *family-level* (`AttributeClass`-keyed) Cue timing in favor of one flat per-Cue default. §9A's per-*channel* override is a different, finer granularity added *underneath* that flat default, not a family-level bucket reintroduced above it — the two decisions are compatible, and this spec (§9A) says so explicitly ("Do not implement a separate family-timing storage model").

---

**Summary for implementation planning:** §23.1 (family granularity) has been resolved by explicit operator direction — `AttributeClass` is now the one authoritative six-family model everywhere, `EncoderCategory` is gone. §23.2–§23.8 and §23.12–§23.13, §23.16 are net-new capabilities with no existing conflicting behavior to reconcile — they are additive. §23.9, §23.10, §23.15 are real but narrow gaps/bugs in already-existing Command Surface code that this spec's grammar rules resolve unambiguously. §23.11, §23.17, §23.18 are confirmations that existing code already matches this spec and should be reused, not rebuilt. §23.19 (Parameter-level Value/Time Fans, §9A) is entirely new — no engine, grammar, or storage exists for it today; per the operator's own specified implementation order, it is built in its own sequence of slices (shared fan engine → parameter timing data model/precedence → value-fan execution → time-fan execution + Store/Update persistence) after the Command Surface layout and already-supported semantics land first.
