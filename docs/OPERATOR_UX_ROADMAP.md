# DMX-CONSOL Operator UX Roadmap

Status: active roadmap
Branch: `chatgpt/ux-integration-fixes`

This document is the current UX source of truth for the operator-facing console workflow. It reflects the latest agreed direction: LIVE is the primary operational view, while Programmer remains an internal state/engine and is not a primary workspace view.

## 1. LIVE is the primary operational view

The operator should work from LIVE and see what is actually happening on stage without switching to a separate Programmer screen.

Programmer remains an internal console mechanism responsible for temporary/editor values, pending values, Store/Update source data, undoable editing, and command execution. Its state is surfaced inside LIVE rather than requiring a separate primary Programmer view.

A small Programmer/Editor inspector may exist later, but only as a secondary diagnostic/editor surface.

### LIVE must expose three simultaneous concepts

For every relevant fixture/channel/parameter the model must be able to expose:

1. **Effective Live Value** — the value currently winning at the output.
2. **Editor / Pending Value** — the temporary value currently being edited by the operator.
3. **Source / Provenance** — the source currently responsible for the effective value, such as Programmer/Editor, Cue, Executor, Effect, or another future playback source.

The UI must make it possible to answer questions such as:

- What is live on stage right now?
- What am I currently editing but have not stored yet?
- Why is Fixture 12 at this value?
- Which playback/source currently owns or wins this parameter?

## 2. LIVE display filters

LIVE must support fast operator filtering. These filters are functional console concepts, not merely cosmetic table filters.

### All

Show all relevant fixtures/channels available to the current view.

### Editor Only

Show only fixtures/channels that currently contain temporary Programmer/Editor values.

This is the replacement for using a separate Programmer view during normal operation.

### Patched

Show only fixtures/channels that have a valid patch.

### Used in Show

Show fixtures/channels that are part of the programmed show structure, regardless of whether they are currently outputting a non-zero level.

A fixture/channel is considered **Used in Show** when it is referenced by programmed show data through at least one of these paths:

- it is referenced in one or more Cue Lists; or
- it is referenced/used by content assigned to an Executor.

`Used in Show` is deliberately different from `Live on Stage`. A fixture may be part of the show while currently at 0%.

Future playback/source types may extend this rule when they become first-class show-programming objects.

### Live on Stage

Show only fixtures/channels that are currently affecting the effective output.

This filter must be based on the effective merged live state, not merely on Programmer contents or cue membership.

### Selected

Show only the current operator selection.

This is important for focused theatre/dance programming and should remain synchronized with the shared selection model used by Channels, Fixtures, Groups, the Command Surface, and future control surfaces.

## 3. Channels LIVE

Channels LIVE should become a dense, fast operational grid/table optimized for theatre and dance work.

Required direction:

- compact grid/list modes;
- clear selected state;
- clear live/active state;
- visible Editor/Pending state;
- provenance/source indication;
- filters listed above;
- responsive behaviour in narrow workspace panes;
- fast fixture/channel navigation;
- no requirement to open a separate Programmer view to understand temporary values.

## 4. Fixtures LIVE

Fixtures LIVE should provide the same LIVE model at fixture level, with semantic parameter presentation.

Primary semantic families:

- Intensity
- Position
- Color
- Beam
- Image
- Shape

The Encoder Drawer remains the primary direct parameter editing surface. Fixtures LIVE should summarize effective live values, pending/editor values, and provenance and hand off parameter editing to the shared editor/encoder workflow.

## 5. Programmer policy

Programmer is retained in the architecture, but its UX role changes:

- **keep** Programmer as an internal state/command engine;
- **keep** Programmer values undoable and usable by Store/Update;
- **surface** Programmer values inside LIVE as Editor/Pending state;
- **remove/de-emphasize** the standalone Programmer View as a primary operator workflow;
- optionally provide a secondary Programmer/Editor inspector or `Editor Only` table for diagnostics and advanced inspection.

Do not duplicate business logic by building a separate live model and programmer model in the UI. Both must read from the same console/Application state.

## 6. Provenance / winner model

LIVE must eventually support parameter-level provenance rather than a single generic fixture source label.

For an effective value, the system should be able to identify:

- winning source type;
- specific source identity where meaningful, e.g. Cue List / Cue / Executor / Effect;
- pending Editor value if one exists;
- whether the pending Editor value currently wins the output or merely exists as an unstored edit;
- future merge/priority information where required to explain ownership.

The long-term UX target is that an operator can inspect a parameter and understand **why this value is live**.

## 7. Selection semantics remain shared

LIVE must use the existing shared operator selection model. No new parallel selection state may be introduced.

Target behaviour remains:

- consecutive selections accumulate during the current selection cycle;
- execution such as `AT 50` leaves the selection visible;
- the next new Fixture/Group selection after execution starts a fresh cycle;
- GUI/touch/keyboard/Command Surface all affect the same selection;
- selection history and recall remain separate concepts from Undo history.

Still to complete:

- full `CLEAR` semantics and edge cases;
- `CLEAR CLEAR` semantics and verification;
- contextual recall: `FIXTURE .`, `GROUP .`, `AT .`;
- selection history / LAST selection workflows;
- parameter-level cycle behaviour where required.

## 8. Groups

Groups should become a real operator pool rather than an administrative table.

Target:

- large touch-friendly tiles;
- obvious selected/active visual state;
- visible membership summary;
- shared selection-cycle behaviour;
- Store / Update workflow;
- explicit overwrite/update/cancel semantics;
- optional intelligent name suggestion only when confidence is high.

## 9. Command Surface and Editor Tool Bar

Continue expanding the existing context-driven command/editor architecture rather than adding fixed global buttons for every feature.

Priorities include:

- richer command vocabulary;
- recall syntax and history;
- CUE timing hierarchy;
- Effect hierarchy;
- Macro;
- Submaster;
- Highlight / Lowlight / Home;
- stable Edit actions with honest disabled states when not implemented.

## 10. Cue Lists and Executors

Cue List work still required:

- deeper compact redesign;
- tracking;
- timing and delay by attribute family;
- unified Store/Update through Application commands;
- stronger editing workflow and source integration with LIVE.

Executor work still required:

- real playback strips/tiles;
- clear fader/GO/BACK/PAUSE/STOP/FLASH state;
- ownership/source clarity;
- integration with `Used in Show` and LIVE provenance;
- Submaster as a first-class operator concept when implemented.

## 11. Patch

Still required:

- compact creation/edit workflow;
- sticky headers;
- explicit `UNPATCHED` state;
- occupancy / used / free / overlap visualization;
- footprint and status;
- Patch / Repatch / Unpatch / Delete as separate operations;
- fixture-profile and mode editing;
- safety around destructive operations.

## 12. Encoder Drawer follow-up

The Encoder Drawer milestone is considered a stable focused milestone, not completion of the larger UX roadmap.

Completed baseline includes gesture transactions, rotary encoders, value strips, MIXED markers, category rail, Position Pad, RGB-gated Color Picker, raw DMX fallback for unknown Pan/Tilt calibration, and responsive/collapsed states.

Still required later:

- NEXT / LAST / MODE for discrete parameters;
- metadata-driven gobo/strobe modes;
- additional Color modes such as XY/Gel where supported;
- Position Flip/Invert only when real calibration metadata exists;
- broader hardware/touch verification.

## 13. Workspace and later roadmap

Still open:

- responsive pane polish and compact modes;
- tablet/touch/accessibility verification;
- Stage Layout / 2D / 3D;
- diagnostics and DMX/device diagnostics;
- effects/phasers operator UX;
- presets workflow;
- remaining show-control roadmap.

## Current implementation priority

Unless a blocking regression appears, the current priority order is:

1. LIVE View redesign as the primary operational surface.
2. LIVE filters: All / Editor Only / Patched / Used in Show / Live on Stage / Selected.
3. Effective Live + Editor/Pending + Provenance in one model/view.
4. Channels LIVE dense operational grid.
5. Fixtures LIVE semantic summary integrated with Encoder Drawer.
6. Selection History + CLEAR + Recall completion.
7. Groups operator pool.
8. Command Surface + Editor Tool Bar expansion.
9. Cue Lists + Executors.
10. Patch.
11. Presets / Effects UX / Workspace polish / Stage / 2D / 3D / diagnostics.

## Definition of done for future UX slices

A UX slice is not considered complete merely because the UI renders.

It should normally require:

- the architecture to use shared Application/console state rather than duplicate UI-only state;
- targeted automated tests;
- `dotnet test` passing;
- manual verification for the relevant operator workflow;
- no merge to `master` until the slice is reviewed and accepted.
