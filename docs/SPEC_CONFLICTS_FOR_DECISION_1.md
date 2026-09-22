# DMX-CONSOL — Spec Conflicts Requiring Decision (Round 2)

This is round 2 of the cross-check between `CLAUDE.md`, `docs/COMMAND_SURFACE_KEY_SPEC.md`,
`docs/OPERATOR_UX_ROADMAP.md`, and `docs/SPATIAL_PLOT_ARCHITECTURE.md`.

Round 1 (`docs/SPEC_CONFLICTS_FOR_DECISION.md`) resolved two items — N1 (STORE CUE clears the
Programmer) and C2 (RELEASE semantics) — and applied them to `CLAUDE.md`/`COMMAND_SURFACE_KEY_SPEC.md`.
That file and those two applied decisions are untouched by this round. This file is a fresh,
expanded decision set (C1–C8, N1–N4, M1–M3, P1–P6) for round 2, authored on `claude/overnight-docs`
as a documentation-only reconciliation. Every decision below was supplied as an already-approved
product/architecture decision — this file records where and how each one was applied to the docs,
not a request to re-litigate it. Where a decision's write-up below restates or refines a round-1
item under the same short code (e.g. this round's C2 restates round-1's C2), this round's item is
authoritative for round 2 and does not contradict round 1 — it is the same decision, carried
forward and kept consistent.

This is a **documentation-only** slice. No `.cs`/`.razor`/`.csproj`/`.sln` file was touched to
produce this round. Where a decision requires a runtime code change to become true today, that is
recorded as an implementation gap in `docs/COMMAND_SURFACE_KEY_SPEC.md` §23, using that section's
existing numbering convention (this round added §23.24–§23.26; see below), never implemented here.

---

## What changed and where, per decision

### C1 — CLEAR

CLEAR immediately clears Current Selection; also resets the CommandComposer/command line and
pending numeric entry, and dismisses an armed RELEASE state; never clears the Programmer; never
overwrites Last Selection; is not Undo/Backspace; has no `CLEAR CLEAR`; Backspace edits pending
input only.

**Where applied:**
- `CLAUDE.md` §2 — retitled "Last Selection & CLEAR", added a full CLEAR subsection.
- `docs/COMMAND_SURFACE_KEY_SPEC.md` §15 — fully rewritten, replacing the old layered
  digit-backspace/remove-last-gesture/`CLEAR CLEAR` model.
- `docs/COMMAND_SURFACE_KEY_SPEC.md` §23.15 — changed from "matches closely, one caveat" to
  RESOLVED: verified directly against `CommandSurfaceViewModel.PressClear` / `ClearSelectionAction`
  / `FixtureSelection.Clear()` that the current implementation already matches the decided model
  exactly. No implementation gap — this is a docs-only correction.
- `docs/OPERATOR_UX_ROADMAP.md` §7 — removed "full `CLEAR` semantics and edge cases" / "`CLEAR
  CLEAR` semantics and verification" from the "still to complete" list, replaced with a pointer to
  the decided/already-implemented model.

### C2 — RELEASE

Release-first canonical workflow via a contextual Release Panel; `RELEASE ENTER` releases all
Programmer values for Current Selection; `RELEASE RELEASE` clears the Programmer globally;
`RELEASE <family> ENTER` releases that family for Current Selection; direct family/parameter-first
syntax (`COLOR RELEASE ENTER`, `PAN RELEASE ENTER`, `RED RELEASE ENTER`) remains supported as
professional shorthand; `SHIFT+RELEASE` releases all playbacks; CLEAR dismisses armed Release
state.

**Where applied:** this round did not need to change `CLAUDE.md` §6 or `COMMAND_SURFACE_KEY_SPEC.md`
§7 — round 1's C2 already put this text in place and it was verified consistent with this round's
restated instructions (parameter-level release preserved, `ENTER` required on direct syntax per
round 1's addendum, `SHIFT+RELEASE`/CLEAR-dismiss both present). `CLAUDE.md` §2's new CLEAR
subsection cross-references RELEASE Panel dismissal; no contradiction found.

### C3 — EFFECT

EFFECT is a fixed Command Surface key, reversing the earlier "not a fixed key" position.

**Where applied:**
- `docs/COMMAND_SURFACE_KEY_SPEC.md` §11 — rewritten from "NOT a fixed physical key... do not make
  EFFECT a permanent fixed key" to "is a fixed physical key."
- `docs/COMMAND_SURFACE_KEY_SPEC.md` §20 (layout) — moved EFFECT out of the "ODD / EVEN and EFFECT
  remain contextual Soft Keys" line and into the RIGHT COMMAND BANK list.
- `docs/COMMAND_SURFACE_KEY_SPEC.md` §23.12 — marked superseded, pointing to new §23.24.
- `docs/COMMAND_SURFACE_KEY_SPEC.md` §23.24 (new) — implementation-gap entry: verified no
  `CommandTokenKind.Effect`, no grammar branch, no registered key (fixed or contextual) exists
  anywhere in the codebase today.
- `CLAUDE.md` §12 — added EFFECT as a fixed key, cross-referencing this decision.
- `docs/OPERATOR_UX_ROADMAP.md` §9 — folded into the P3 fixed-key-philosophy rewrite (EFFECT cited
  as the concrete example of an approved fixed key).

### C4 — PRESET

Preset pools remain per-family/per-AttributeClass; `PRESET 5 ENTER` without family context must not
guess; `STORE PRESET` may open a family-selection workflow; no collapsed single namespace.

**Where applied:** `CLAUDE.md` §5 — added explicit recall-side wording ("must not silently guess
which pool's Preset 5 to apply") alongside the already-correct store-side rules. No change needed
in `COMMAND_SURFACE_KEY_SPEC.md` §4/§5 — already stated "do not guess, expose the family choice,"
already consistent.

### C5 — Undo boundaries

Patch does not enter Programming Undo; structural Patch/Show history is separate future work;
Selection history is separate from Programming/Edit Undo; CLEAR is not part of Programming Undo.

**Where applied:** `CLAUDE.md` §7 — added a paragraph stating Selection history (and specifically
CLEAR) is separate from Programming Undo, narrowly scoped to not reopen whether other
selection-building commands are undoable `IConsoleCommand`s (unchanged, existing architecture).
Patch-not-in-Undo wording in §7/§8 was already correct and untouched.

### C6 — STORE/UPDATE/DELETE routing

Shared Command Surface STORE/UPDATE/DELETE are authoritative; contextual per-object soft keys may
exist but must route to the shared operations, not implement local semantics.

**Where applied:**
- `CLAUDE.md` §4 — added a "STORE/UPDATE/DELETE routing" paragraph stating the shared operations
  are authoritative and contextual soft keys must route to them.
- `docs/COMMAND_SURFACE_KEY_SPEC.md` §23.17 — revised: removed the "should be the template for
  future wiring" framing for the existing per-context soft-key pattern, replaced with "the shared
  Command Surface STORE/UPDATE/DELETE operations are the template; contextual soft keys route to
  them." Existing code (`RunContextAction` → shared Application commands) already satisfies this
  and needed no implementation-gap entry.

### C7 — STORE conflict terminology

Exactly UPDATE / OVERWRITE / CANCEL. No MERGE, no REMOVE without a separate decision.

**Where applied:** `CLAUDE.md` §5 — added an explicit "Store conflict terminology (decided)"
heading directly above the existing UPDATE/OVERWRITE/CANCEL list (the list itself was already
correct; only the explicit "never MERGE, never a stray REMOVE" framing was added).
`docs/COMMAND_SURFACE_KEY_SPEC.md` §13 — added a one-line cross-reference to the same rule, next to
the new N2 EDIT/LOAD/GO TO subsection.

### C8 — Cue Trigger model

Keep Manual/AutoFollow/Wait on the target cue; no list-level AUTO CONTINUE.

**Where applied:** `CLAUDE.md` §9 — added an explicit "No list-level AUTO CONTINUE feature" line
directly under the existing (unchanged, already-correct) Manual/AutoFollow/Wait model. No changes
needed elsewhere — no document had actually proposed AUTO CONTINUE as current-state text; this
closes the round-1 C8 item explicitly rather than leaving it silently implied.

### N1 — STORE CUE clears Programmer (round 1, restated/refined here)

Store succeeds first, then Programmer clears, as one atomic undoable transaction; UNDO restores
both; no partial mutation on failure/cancel.

**Where applied:** no change — round 1 already applied this to `CLAUDE.md` §5 and
`COMMAND_SURFACE_KEY_SPEC.md` §13/§23.23. This round's instructions were verified consistent with
round 1's existing text; nothing contradicted it, so nothing was rewritten. `CLAUDE.md` §11 (new,
this round) cross-references it when describing which non-playback operations must NOT trigger Cue
Macro firing.

### N2 — CUE X ENTER vs LOAD vs GO TO

`CUE X ENTER` = EDIT (load into Programmer for editing). `LOAD CUE X ENTER` = LOAD (load for
reuse). `GO TO CUE X ENTER` = playback jump. All three distinct.

**Where applied:**
- `CLAUDE.md` §10 (Cue Navigation) — added an "EDIT vs LOAD vs GO TO" subsection.
- `docs/COMMAND_SURFACE_KEY_SPEC.md` §13 — added the same subsection, cross-referencing §17's Macro
  firing rule.
- `docs/COMMAND_SURFACE_KEY_SPEC.md` §23.26 (new) — implementation-gap entry: verified
  `CommandComposer.Build` returns `Incomplete(...)` for any bare `Cue`-token command today ("Cue
  numeric commands are not implemented yet"); no `LOAD` token/grammar exists; no command-line
  `GO TO` grammar exists. All three meanings are unbuilt at the grammar layer.

### N3 — SHIFT mappings and Macro Manager

Keep `SHIFT+STORE` = Store Options, `SHIFT+LEARN MACRO` = Macro Manager. Document Macro Manager as
future work with the listed v1 responsibilities; LEARN MACRO remains the recording workflow;
Macro playback must use shared operations (already true).

**Where applied:**
- `CLAUDE.md` §15 (new) — added a Macros section: SHIFT mappings restated, Macro Manager
  future-work status and v1 responsibility list, LEARN MACRO/playback-sharing restated.
- `docs/COMMAND_SURFACE_KEY_SPEC.md` §17 — added a "Macro Manager" subsection with the same v1
  responsibility list and out-of-scope note. §17's existing `SHIFT+LEARN MACRO` line was already
  correct and untouched.
- No implementation-gap entry needed beyond what §23.7 already documents (Macro Manager/EXAM
  explicitly called out there as future work); this round only adds the v1 responsibility list that
  didn't exist in any doc before.

### N4 — TIME semantics

Timing is per parameter/channel only, never per family/AttributeClass; family TIME syntax (e.g.
`POSITION TIME 5`) is shorthand that expands to per-parameter writes.

**Where applied:**
- `CLAUDE.md` §4 — added a "TIME semantics" paragraph, and extended the existing "Known
  contradiction — do not reintroduce" callout to explicitly cover TIME grammar, not only Cue-level
  `AttributeTiming`.
- `docs/COMMAND_SURFACE_KEY_SPEC.md` §9A — already stated this correctly in detail (family syntax
  is a convenience expanding to per-channel writes, no second storage bucket); verified consistent,
  no rewrite needed.
- `docs/OPERATOR_UX_ROADMAP.md` §10 — removed the obsolete "timing and delay by attribute family"
  line (see P5 below; same fix serves both N4 and P5).

### M1 — Macro assignment to Cues

New grammar: `STORE MACRO n AT CUE n ENTER` / `STORE MACRO n+m AT CUE a THRU b ENTER` / `DELETE
MACRO ... AT CUE ... ENTER`. Macros are independent objects; Cues store references, not copies.
`ENTER` mandatory; atomic across a Cue range.

**Where applied:**
- `CLAUDE.md` §15 (new) — "Macro assignment to Cues" subsection with the full grammar block and
  atomicity/ENTER requirements.
- `docs/COMMAND_SURFACE_KEY_SPEC.md` §17 — same grammar block added under "Macro assignment to
  Cues."
- `docs/COMMAND_SURFACE_KEY_SPEC.md` §23.25 (new) — implementation-gap entry: verified no `AT CUE`
  clause exists in `CommandComposer`, `Cue.cs` has no Macro-reference field, and
  `MacroPlaybackService`/`MacroBank` have no Cue-entry hook. Entirely new: grammar, data field, and
  firing hook all unbuilt.

### M2 — Cue Macro firing

"Cue Macro Assignments fire on Cue entry/start" — fires on GO / AutoFollow / Wait / GO TO / BACK /
SHIFT+GO / SHIFT+BACK / any valid playback-starting path; never on EDIT / LOAD / inspection.

**Where applied:**
- `CLAUDE.md` §15 (new) and §10 (new N2 subsection) — the verbatim rule "Cue Macro Assignments fire
  on Cue entry/start" is stated in §15, with the fire/don't-fire list, and cross-referenced from
  §10's EDIT/LOAD/GO TO subsection.
- `docs/COMMAND_SURFACE_KEY_SPEC.md` §17 and §13 — same verbatim rule and cross-reference.
- Implementation gap recorded together with M1 in §23.25 (the firing hook and the assignment data
  it fires from are one dependent piece of work, not two separate gaps).

### M3 — Macro Manager visibility

Macro Manager should show which Cues reference each Macro; optional future reverse view (which
Macros are assigned to a selected Cue).

**Where applied:** `CLAUDE.md` §15 and `docs/COMMAND_SURFACE_KEY_SPEC.md` §17 — both Macro
Manager/Macro-assignment subsections end with this visibility requirement and the optional future
reverse view, stated identically in both files.

### P1 — Baseline

Current baseline: branch `claude/selection-cycle`, 733/733 tests. Remove obsolete
`chatgpt/ux-integration-fixes`/"583 tests" as CURRENT-state guidance; historical mentions may stay
if clearly marked historical.

**Where applied:**
- `docs/COMMAND_SURFACE_KEY_SPEC.md` header (top of file) — rewritten: states the current baseline
  explicitly, marks `chatgpt/ux-integration-fixes`/583-tests as historical context for how the doc
  came to exist, not current-state guidance.
- `docs/COMMAND_SURFACE_KEY_SPEC.md` §23 intro paragraph — same treatment: the branch reference in
  the existing §23 lead-in sentence is now explicitly marked historical, with a pointer to the
  header for the actual current baseline.
- `docs/OPERATOR_UX_ROADMAP.md` header — same rewrite as KEY_SPEC's header.
- `docs/SPEC_CONFLICTS_FOR_DECISION.md` (round 1 file) — **not touched**, per this round's explicit
  instruction to leave it exactly as-is (it already records P1 as undecided-at-the-time in its own
  P1 row/status note, which remains historically accurate for round 1 and is not contradicted by
  round 2 now deciding it).
- `docs/chatgpt-work-notes.md`, `docs/reports/review-selection-cycle.md` — out of scope for this
  round (not among the three files this round updates); their `chatgpt/ux-integration-fixes` /
  branch-name mentions are their own historical record of a specific piece of work, not
  current-baseline claims, and were left untouched.

### P2 — Priority order

Present current planning as slice-based/dependency-driven, not a fixed numbered priority list that
contradicts current direction (e.g. the PATCH → 2D Plot → Spatial Database chain).

**Where applied:** `docs/OPERATOR_UX_ROADMAP.md` — the "Current implementation priority" numbered
1–16 list is replaced with a "Current development approach: slice-based, dependency-driven"
section, explicitly citing the `docs/SPATIAL_PLOT_ARCHITECTURE.md` §6 dependency chain as the
reason a single linear list doesn't work, and pointing to each section's own "still required" list
plus CLAUDE.md's One-Slice-At-A-Time/workflow rules as the actual sequencing mechanism.

### P3 — Fixed-key philosophy

The approved fixed-key map (including EFFECT) is authoritative; the real principle is "avoid
duplicated semantics," not "avoid fixed keys."

**Where applied:**
- `docs/OPERATOR_UX_ROADMAP.md` §9 — rewritten: removed "rather than adding fixed global buttons
  for every feature," replaced with the corrected principle and an explicit citation of EFFECT as
  the concrete example.
- `CLAUDE.md` §12 — added the same corrected principle statement, cross-referenced from the new
  EFFECT line.

### P4 — Placeholder softkeys

Hide unimplemented contextual actions (MIB, LOOK AHEAD, BLOCK, UNBLOCK, TRY TIME, CUE ONLY,
TRACKING, etc.) rather than showing misleading disabled soft keys.

**Where applied:** `docs/COMMAND_SURFACE_KEY_SPEC.md` §21 (Architecture requirements) — added a
"Placeholder soft keys" subsection stating the hide-don't-disable rule with the example list and
the "prefer absence over misleading behavior" line. `docs/OPERATOR_UX_ROADMAP.md` does not discuss
placeholder/disabled soft keys anywhere in its current text (checked directly), so no change was
needed there — the rule now lives in KEY_SPEC §21, the document that actually discusses soft-key
behavior.

### P5 — Timing contradiction

Remove obsolete "timing and delay by attribute family" wording from ROADMAP; timing is per
parameter/channel only (see N4).

**Where applied:** `docs/OPERATOR_UX_ROADMAP.md` §10 — removed the "timing and delay by attribute
family" bullet from the Cue List "still required" list, replaced with a sentence restating the
per-parameter-only rule and cross-referencing `CLAUDE.md` §4/§9a and the "Known contradiction"
callout, so the fix stays consistent with N4 rather than being a separate, disconnected edit.

### P6 — Development workflow

One slice per Claude Code session; workflow implementer → qa-regression → architecture-reviewer →
operator approval → commit; agents do not commit/push; unresolved ambiguity after this
reconciliation is marked DECISION REQUIRED here and the agent stops (CLAUDE.md's existing
Stop-On-Ambiguity Rule).

**Where applied:** `CLAUDE.md` "Working Rules" § "One Slice At A Time" — added the explicit
workflow sentence and the "commit/push only by instruction, stop on ambiguity" cross-reference,
worded so it restates (not duplicates) the existing Stop-On-Ambiguity Rule and Commit/Push Rule
sections already present in the file. No changes were needed to those two existing sections — they
already say this; P6 only needed the workflow chain itself written down once, in the "One Slice At
A Time" section, so all three stay consistent with each other.

---

## Implementation gaps discovered and documented in KEY_SPEC §23

Verified directly against the current `src/` codebase (read-only; no code was changed):

- **§23.24 (new) — EFFECT as a fixed key (C3).** No `CommandTokenKind.Effect`, no grammar branch in
  `CommandComposer.cs`, no registered EFFECT key of any kind (fixed or contextual) anywhere in
  `SoftKeyRegistryBuilder`/`CommandSurface.razor`. Supersedes the now-stale §23.12.
- **§23.25 (new) — Macro assignment to Cues (M1/M2).** No `AT CUE` grammar clause in
  `CommandComposer.cs`; `Cue.cs` (`DmxConsole.Core/Engine/Cue.cs`) has no Macro-reference field of
  any kind; `MacroPlaybackService`/`MacroBank` (`DmxConsole.Application.Macros`) have no hook into
  Cue-start/playback-entry logic. Entirely new grammar, data model field, and firing hook.
- **§23.26 (new) — CUE X ENTER / LOAD CUE X ENTER / GO TO CUE X ENTER (N2).**
  `CommandComposer.Build` explicitly rejects any bare `Cue`-token command today
  ("Cue numeric commands are not implemented yet"); no `LOAD` token/grammar exists; no command-line
  `GO TO` grammar exists. All three meanings are unbuilt.
- **C1 (CLEAR) — checked, found to require NO code change.** Verified against
  `CommandSurfaceViewModel.PressClear`, `ClearSelectionAction`, and `FixtureSelection.Clear()`: the
  implementation already matches the decided model exactly (immediate single-step clear, resets
  pending composer/digit state, dismisses armed RELEASE, never touches Last Selection, no `CLEAR
  CLEAR` code path, Backspace already a separate handler). §23.15 was updated to RESOLVED rather
  than adding a new gap entry, since there is no gap.

---

## Decision Record

| ID | Decision | Docs updated |
|---|---|---|
| C1 | DECIDED — applied; code already matched, no gap | `CLAUDE.md` §2; `docs/COMMAND_SURFACE_KEY_SPEC.md` §15, §23.15; `docs/OPERATOR_UX_ROADMAP.md` §7 |
| C2 | DECIDED — verified consistent with round 1, no rewrite needed | (no changes required this round; round 1's `CLAUDE.md` §6 / KEY_SPEC §7 stand) |
| C3 | DECIDED — applied; implementation gap recorded | `CLAUDE.md` §12; `docs/COMMAND_SURFACE_KEY_SPEC.md` §11, §20, §23.12, §23.24; `docs/OPERATOR_UX_ROADMAP.md` §9 |
| C4 | DECIDED — applied | `CLAUDE.md` §5 |
| C5 | DECIDED — applied | `CLAUDE.md` §7 |
| C6 | DECIDED — applied | `CLAUDE.md` §4; `docs/COMMAND_SURFACE_KEY_SPEC.md` §23.17 |
| C7 | DECIDED — applied | `CLAUDE.md` §5; `docs/COMMAND_SURFACE_KEY_SPEC.md` §13 |
| C8 | DECIDED — applied | `CLAUDE.md` §9 |
| N1 | DECIDED (round 1) — verified consistent, no rewrite needed | (no changes required this round; round 1's `CLAUDE.md` §5 / KEY_SPEC §13/§23.23 stand) |
| N2 | DECIDED — applied; implementation gap recorded | `CLAUDE.md` §10; `docs/COMMAND_SURFACE_KEY_SPEC.md` §13, §23.26 |
| N3 | DECIDED — applied | `CLAUDE.md` §15; `docs/COMMAND_SURFACE_KEY_SPEC.md` §17 |
| N4 | DECIDED — applied | `CLAUDE.md` §4 (incl. "Known contradiction" callout); `docs/OPERATOR_UX_ROADMAP.md` §10 |
| M1 | DECIDED — applied; implementation gap recorded | `CLAUDE.md` §15; `docs/COMMAND_SURFACE_KEY_SPEC.md` §17, §23.25 |
| M2 | DECIDED — applied; implementation gap recorded (with M1) | `CLAUDE.md` §10, §15; `docs/COMMAND_SURFACE_KEY_SPEC.md` §13, §17, §23.25 |
| M3 | DECIDED — applied | `CLAUDE.md` §15; `docs/COMMAND_SURFACE_KEY_SPEC.md` §17 |
| P1 | DECIDED — applied | `docs/COMMAND_SURFACE_KEY_SPEC.md` header, §23 intro; `docs/OPERATOR_UX_ROADMAP.md` header |
| P2 | DECIDED — applied | `docs/OPERATOR_UX_ROADMAP.md` "Current development approach" section |
| P3 | DECIDED — applied | `docs/OPERATOR_UX_ROADMAP.md` §9; `CLAUDE.md` §12 |
| P4 | DECIDED — applied | `docs/COMMAND_SURFACE_KEY_SPEC.md` §21 |
| P5 | DECIDED — applied | `docs/OPERATOR_UX_ROADMAP.md` §10 |
| P6 | DECIDED — applied | `CLAUDE.md` "Working Rules" § "One Slice At A Time" |

---

## DECISION REQUIRED items

None. Every item in this round's instructions was supplied as an already-approved decision with
enough detail to apply directly to the docs, and no genuine new contradiction was discovered while
applying them (cross-checked against `docs/SPATIAL_PLOT_ARCHITECTURE.md`, which required no changes
— its PATCH/2D Plot/Spatial Database/Venue Adaptation architecture does not conflict with any
decision in this round). Per CLAUDE.md's Stop-On-Ambiguity Rule, this section exists to be filled
in if that had not been the case; it is empty because it was not.
