# DMX-CONSOL — Spec Conflicts Requiring Decision

**To GPT:** This is a cross-check of the new Command Surface Stabilization spec (our conversation) against what is already in the repository: `CLAUDE.md`, `docs/COMMAND_SURFACE_KEY_SPEC.md` (v1, "authoritative"), and `docs/OPERATOR_UX_ROADMAP.md`.

Claude Code agents are instructed to STOP on contradictions between documents. If the stabilization prompt is sent as-is, they will either stop repeatedly or silently pick one side.

**Please do not resolve these yourself.** For each item, help Shalom decide. After all decisions are made, produce:
1. a consolidated edit list for `CLAUDE.md` and `COMMAND_SURFACE_KEY_SPEC.md` (the docs change **before** any code), and
2. the stabilization work split into small slices, one Claude Code session each.

Each item lists what each source says. The "Recommendation" line is Claude's suggestion only.

---

## A. Direct conflicts — must decide before any implementation

### C1. CLEAR semantics
- **KEY_SPEC §15:** CLEAR is layered — backspaces a digit during numeric entry; otherwise removes the *last selection gesture* (e.g. removes GROUP 5 from GROUP 1 + GROUP 2 + GROUP 5); `CLEAR CLEAR` clears the whole selection. Already implemented this way (§23.15).
- **ROADMAP §7:** still lists "`CLEAR CLEAR` semantics and verification" as work to complete.
- **New spec:** CLEAR = clear Selection immediately, nothing else. No `CLEAR CLEAR`. `⌫` is a separate key for the command line only and must not touch committed Selection.
- **Why it matters:** the new model loses "remove only the last gesture" — there is no way left to undo one mistaken GROUP from an accumulated selection. Also the Macro overwrite confirmation (§23.7) is documented as "the same two-press idiom as CLEAR CLEAR".
- **Recommendation:** adopt the new model, but decide explicitly whether "remove last selection gesture" survives somewhere (e.g. `⌫` when the command line is empty) or is dropped on purpose.

### C2. RELEASE — order and meaning of `RELEASE ENTER`
- **KEY_SPEC §7 (implemented):** family-first, self-terminating: `COLOR RELEASE`, `PAN RELEASE`. Bare `RELEASE` = release Editor for current selection. **`RELEASE ENTER` = clear the ENTIRE Editor (all fixtures).** Parameter-level release (`PAN RELEASE`) is implemented, and the Parameter Picker (commit d06e663) was built on top of the `<Family|Parameter> RELEASE` grammar.
- **CLAUDE.md §6:** release-first: `RELEASE → COLOR → ENTER`. `RELEASE → ENTER` = release all values **for current Selection**. `RELEASE → RELEASE` = clear Editor globally.
- **New spec:** same as CLAUDE.md, plus a Release Panel on first press. Parameter-level release is not mentioned.
- **Why it matters:** three documents, two opposite meanings for `RELEASE ENTER`. The stabilization prompt says "replace obsolete Release command behavior" — an agent may delete the parameter-level release and break the Parameter Picker that was just approved.
- **Recommendation:** decide (a) release-first, family-first, or both; (b) one meaning for `RELEASE ENTER`; (c) explicitly keep parameter-level release and state how it is reached from the Release Panel.

### C3. EFFECT — fixed key or soft key
- **KEY_SPEC §11:** "EFFECT is NOT a fixed physical key... Do not make EFFECT a permanent fixed key."
- **New spec:** EFFECT is a fixed key in the Command Surface.
- **Recommendation:** decide, then update §11 and §20 (layout).

### C4. PRESET without a family
- **KEY_SPEC §4:** PRESET is not an independent object; recall must always resolve to a family. Without family context: do not guess, offer family choice.
- **CLAUDE.md §5:** one Preset object per AttributeClass pool; the same number can exist in each pool; `STORE COLOR 2`; `STORE PRESET ENTER` opens a dialog.
- **New spec:** `PRESET 5 ENTER` = apply; `STORE PRESET 3 ENTER`.
- **Why it matters:** with per-family pools, "Preset 3" is ambiguous — there can be a Color 3, a Position 3 and a Beam 3.
- **Recommendation:** keep the KEY_SPEC/CLAUDE.md rule; rewrite the new spec's PRESET examples with a family.

### C5. What enters Undo
- **CLAUDE.md §7–8:** Patch must NOT enter Programming Undo; structural history is a separate system.
- **New spec §31 and stabilization prompt:** Undo covers Patch.
- **KEY_SPEC §15:** selection clearing should remain undoable (selection commands currently derive from an undoable `SelectionCommandBase`).
- **New spec:** "Selection history is not editing Undo history."
- **Recommendation:** decide both: (a) Patch in Undo or not; (b) do Selection changes stay in the Undo stack or move to a separate Selection history.

### C6. Object-context STORE/UPDATE/DELETE soft keys
- **KEY_SPEC §23.17:** per-context STORE/UPDATE/DELETE soft keys (Cue, Group, Fixture) already exist and are named as "the template" for future wiring.
- **New spec:** remove local Store variants; soft keys must not duplicate fixed keys; central STORE only.
- **Recommendation:** adopt the new spec, but record it as a deliberate reversal of §23.17 so agents don't treat the existing soft keys as the pattern to follow.

### C7. Store options naming
- **CLAUDE.md §5:** Store options are UPDATE (merge) / OVERWRITE / CANCEL.
- **New spec:** MERGE / OVERWRITE / REMOVE / CUE ONLY / TRACK.
- **Why it matters:** "UPDATE" as a Store option collides with the UPDATE key, which means something else.
- **Recommendation:** use MERGE in both places; decide whether REMOVE exists.

### C8. Cue-level trigger vs Cue List "AUTO CONTINUE"
- **CLAUDE.md §9–10 (implemented on `claude/selection-cycle`, 733 tests):** CueTriggerMode Manual / AutoFollow / Wait belongs to the *target cue*; SHIFT+GO/BACK = instant navigation.
- **New spec:** proposes a list-level AUTO CONTINUE soft key; does not mention Trigger modes or SHIFT+GO/BACK.
- **Recommendation:** drop AUTO CONTINUE or define it in terms of the existing Trigger model; add Trigger and SHIFT+GO/BACK to the spec so the stabilization pass doesn't regress them.

---

## B. New rules that need to be written down (not conflicts, but not in any doc yet)

### N1. STORE CUE auto-clears the Editor
New rule. Open question: if the operator presses UNDO after a successful STORE CUE, is the Editor content restored together with the Store being undone?

### N2. `CUE X ENTER` = EDIT cue content
New meaning. The current behavior of `CUE 12 ENTER` (KEY_SPEC §14 lists it as a valid command) must be audited first — if it currently does something else, this is a behavior change, not a new feature.

### N3. Dropped SHIFT mappings
KEY_SPEC §18 defines `SHIFT + STORE` = Store Options and `SHIFT + LEARN MACRO` = Macro Manager. The new spec's SHIFT list omits both. Keep or remove explicitly.

### N4. `POSITION TIME 5` (parameter time)
Allowed only as a convenience that writes per-channel overrides (KEY_SPEC §9A). Must not create family-level timing storage. The new spec should say so.

---

## C. Process / planning conflicts

### P1. Branch and baseline
- KEY_SPEC, ROADMAP and the stabilization prompt all target `chatgpt/ux-integration-fixes` (583 tests at d06e663).
- Current work in Claude Desktop is on `claude/selection-cycle`: 733 tests, +20,597 uncommitted lines, including the Trigger/Wait implementation.
- **Decide:** which branch is the base for stabilization, and how the other is merged in first.

### P2. Priority order
ROADMAP "Current implementation priority" puts LIVE View first and Command Surface at #8. The stabilization pass reorders this. Legitimate, but the ROADMAP must be updated so agents don't flag it as a violation.

### P3. Fixed keys philosophy
ROADMAP §9: "rather than adding fixed global buttons for every feature". The new map adds LOAD, EFFECT, CUE LIST, EXAM, SETUP and more as fixed keys. Update §9's wording to match the new direction.

### P4. Disabled placeholder soft keys
The Cue context proposes 7 soft keys (MIB, LOOK AHEAD, BLOCK, UNBLOCK, TRY TIME, CUE ONLY, plus Cue List TRACKING) for features with no engine yet (ROADMAP §10 still lists tracking as not built). The new spec itself says "prefer absence over misleading behavior".
**Recommendation:** hide them until the engine exists, rather than showing a row of disabled keys.

### P5. Internal ROADMAP contradiction
ROADMAP §10 still says "timing and delay by attribute family", while ROADMAP §9a, KEY_SPEC §9A and CLAUDE.md say per-family timing was deliberately removed. Delete that line from §10.

### P6. Scope of the stabilization prompt
One prompt with 8 phases contradicts CLAUDE.md "One Slice At A Time". In the last run, Claude itself stated it could not do all 8 phases with full rigor in one pass. After decisions: split into slices, each going implementer → qa-regression → architecture-reviewer → commit.

---

## Decision record (to fill in)

| ID | Decision | Docs to update |
|---|---|---|
| C1 | | |
| C2 | DECIDED — see "Decisions" | CLAUDE.md §6, KEY_SPEC §7 |
| C3 | | |
| C4 | | |
| C5 | | |
| C6 | | |
| C7 | | |
| C8 | | |
| N1 | DECIDED — see "Decisions" below | CLAUDE.md §5, KEY_SPEC §13 |
| N2 | | |
| N3 | | |
| N4 | | |
| P1 | | |
| P2 | | |
| P3 | | |
| P4 | | |
| P5 | | |

---

## Decisions

### N1 — STORE CUE clears Programmer — DECIDED

After a successful STORE CUE, the Programmer is automatically cleared.

The Store and the Programmer clear are one atomic undoable transaction.

If UNDO is executed:
- the Cue returns to its exact pre-Store state
- the Programmer returns to its exact pre-Store state

If STORE fails or is cancelled:
- Cue state remains unchanged
- Programmer state remains unchanged

No partial mutation is allowed.

Implied by existing rules (no new decision needed):
- Selection and Playback are not affected (Selection ≠ Programmer ≠ Playback).
- REDO re-applies both the Store and the clear together.
- Implementation: one transaction via `DispatchBatch` / `CompositeCommand` (CLAUDE.md §14), and the composite must be replayable for Macros (`IReplayableCommand`).
- "Programmer state" includes everything the Programmer holds: values, knockouts, DMX-direct values, and future per-channel timing overrides (KEY_SPEC §9A).

Sub-decision — Filtered Store — DECIDED:
- After a successful STORE CUE the ENTIRE Programmer is cleared, always — including values that were not stored because of a Store filter (e.g. selected-fixtures-only or family filter).
- Rationale: simple and predictable. Anything lost this way is recoverable with UNDO, which restores the exact pre-Store Programmer (see above).

### C2 — RELEASE semantics — DECIDED

Release-first is the canonical workflow, while direct family/parameter-first
release syntax is preserved as a professional shortcut.

Canonical workflow:
RELEASE
→ contextual Release Panel
→ choose family/families
→ ENTER

Semantics:
RELEASE ENTER
= release all Programmer values for Current Selection

RELEASE RELEASE
= clear the entire Programmer globally, selection-independent

Family release:
RELEASE COLOR ENTER
RELEASE POSITION ENTER
etc.
= release the selected family/families for Current Selection

Direct syntax remains supported:
COLOR RELEASE ENTER
PAN RELEASE ENTER
RED RELEASE ENTER
etc.

Parameter-level release MUST remain supported.
Do not regress or remove the existing Parameter Picker release path.

SHIFT + RELEASE:
= release all playbacks

CLEAR dismisses an armed Release state.

Addendum:
- Direct family/parameter release now requires ENTER (was self-terminating). Replace tests that encode the old behavior.
- Selection-scoped release with no Current Selection → no mutation, clear message.
- Doc fix: KEY_SPEC §7 still describes RELEASE ENTER as clearing the entire Programmer — update it to match this decision.
