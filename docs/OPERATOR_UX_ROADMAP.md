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

## 11. Patch and Fixture Library / Device Profiles

Patch is the natural home for fixture/device management. The Fixture Library should be a subsystem inside Patch rather than a separate primary workspace screen.

### Patch workflow still required

- compact creation/edit workflow;
- sticky headers;
- explicit `UNPATCHED` state;
- occupancy / used / free / overlap visualization;
- footprint and status;
- Patch / Repatch / Unpatch / Delete as separate operations;
- fixture-profile and mode editing;
- safety around destructive operations.

### Fixture Library / Device Profiles

The operator workflow should be:

`Patch -> Add Fixture -> Search Manufacturer / Model -> Select Mode -> Quantity / Fixture Numbers -> Universe / Address -> Patch`

The library must represent three distinct concepts:

- **Fixture Model** — the physical product, for example a manufacturer/model combination;
- **Fixture Mode** — one DMX personality/mode for that model, with its own footprint and channel map;
- **Patched Fixture Instance** — the fixture number/address used in the current show.

The internal profile model must be DMX-CONSOL-native and semantic. External libraries are import sources, not runtime business models.

Target import path:

`GDTF / Open Fixture Library / Manual Profile -> Importer -> DMX-CONSOL Fixture Profile`

Required profile metadata should include, where available:

- Manufacturer, Model, Mode and footprint;
- semantic parameter families and attribute mapping;
- coarse/fine channel relationships and resolution;
- channel defaults, Home and Highlight values;
- physical Pan/Tilt ranges and other calibrated physical ranges;
- capability ranges and discrete values such as gobo, prism, shutter/strobe and color-wheel slots;
- color-system metadata such as RGB/RGBW/other emitters;
- source/version information and validation status.

Patch should expose management actions such as:

- `Manage Fixture Library`;
- `Edit Profile`;
- `Import GDTF`;
- `Import OFL`;
- `Create Custom Fixture`.

A Custom Fixture/Profile editor is required because some real fixtures will not have a reliable library entry.

### Profile integrity rules

- Never invent calibration, physical range or capability metadata.
- If semantic metadata is missing, fall back honestly to raw DMX rather than guessing.
- Profile validation should detect invalid channel overlap, missing fine/coarse relationships, impossible capability ranges and footprint inconsistencies where feasible.
- Imported profiles must be normalized into the internal model before being consumed by Patch, Encoder Drawer, Presets, Calibration, LIVE, Quick Grid or future 2D/3D.

This profile system is a dependency for richer discrete Encoder behavior, Fixture Type/Compatible presets, fixture calibration, reliable patch footprint handling and future visualization.

## 12. Encoder Drawer follow-up

The Encoder Drawer milestone is considered a stable focused milestone, not completion of the larger UX roadmap.

Completed baseline includes gesture transactions, rotary encoders, value strips, MIXED markers, category rail, Position Pad, RGB-gated Color Picker, raw DMX fallback for unknown Pan/Tilt calibration, and responsive/collapsed states.

Still required later:

- NEXT / LAST / MODE for discrete parameters;
- metadata-driven gobo/strobe modes;
- additional Color modes such as XY/Gel where supported;
- Position Flip/Invert only when real calibration metadata exists;
- broader hardware/touch verification.

## 13. Preset scope model — Fixture / Fixture Type / Compatible

The Preset system must support three scope levels using DMX-CONSOL-native terminology rather than copying another console's naming.

### Fixture

A **Fixture** preset is fixture-specific.

- It applies only to the specific fixtures whose data was stored into the preset.
- Recalling it on other fixtures must not silently invent or transfer data.
- This is the safest scope for fixture-specific looks, positions, and special-case values.

### Fixture Type

A **Fixture Type** preset applies to all compatible fixtures of the same fixture profile/type.

- Storing a preset from one representative fixture of a fixture type must allow that preset to be recalled on other fixtures of the same fixture type.
- The stored data must be semantic/profile-aware data for that fixture type, not copied raw DMX addresses from the originally selected fixture.
- Example: store a Color preset from one ESPIT fixture as Fixture Type; every ESPIT of the same fixture type can recall it even if only one ESPIT was selected when the preset was created.

### Compatible

A **Compatible** preset applies across different fixture types when the relevant semantic parameters can be translated safely.

- It should be applicable to different fixture types when the relevant semantic parameters are compatible.
- The system must use semantic attribute/profile metadata rather than raw DMX copying.
- Compatibility translation must never guess when compatibility is ambiguous.
- Color is a major target use case, but translation rules must be explicit for RGB/RGBW/other color systems before those combinations are allowed.

### Preset architecture requirements

Preset storage and recall must therefore become profile-aware and semantic.

Required direction:

- Preset scope enum/model: `Fixture`, `FixtureType`, `Compatible`.
- Store/Update preserves and exposes the selected scope.
- Preset tiles/pools clearly indicate scope, for example `F`, `T`, `C` or an equivalent visual treatment.
- Fixture Type data is keyed/resolved by fixture type/profile semantics rather than fixture DMX address.
- Compatible data is keyed/resolved by semantic attributes and explicit compatibility/translation rules.
- No Compatible recall may silently coerce incompatible parameter systems.
- Mixed preset contents may later support different scope behavior per semantic family only if the model remains deterministic and understandable to the operator.
- Scope should be available from Command Surface, touch UI, Quick Grid, and future NL/macros through the same Application-layer operation path.
- Default scope per preset pool may be added later, but the current scope must always remain visible to the operator.

### Preset workflow still required

- full Store / Update / Delete / Move / Copy workflow;
- scope selection during Store;
- scope-preserving Update;
- controlled scope conversion where valid;
- compatibility reporting when a target cannot use a Fixture Type/Compatible preset;
- semantic Position/Color/Beam/Image/Shape preset pools;
- integration with LIVE, Encoder Drawer, Quick Grid, Cue programming, and provenance where relevant.

## 14. Fixture Calibration / Venue Adaptation

DMX-CONSOL should support fast venue adaptation for moving fixtures so a show can be corrected after re-hang, touring, or fixture replacement without manually editing every Position preset and cue.

The feature must be implemented as a reversible calibration/transform layer, not as bulk rewriting of programmed show data.

### Fixture Alignment / Patch Offset

The fast workflow should support correcting one known position and applying that correction relatively across all programmed positions for the affected fixture(s).

Target workflow:

1. Select one fixture or a fixture group.
2. Recall a known Position preset or programmed stage position.
3. Correct the fixture physically on stage using Pan/Tilt.
4. The console calculates the relative Pan/Tilt correction.
5. Show a clear preview, for example `Pan +4.2° / Tilt -2.7°` or the equivalent calibrated/raw delta.
6. Ask the operator whether to apply the correction as a fixture calibration offset.
7. If confirmed, all existing programmed positions for that fixture resolve through the new calibration transform automatically.

Requirements:

- do not rewrite all Cue or Position preset data;
- preserve the original programmed semantic position;
- store the correction as fixture/patch calibration metadata;
- support one fixture and multiple fixtures/groups;
- preview before commit;
- Undo/Revert calibration changes;
- inspect original value vs corrected output;
- make the active calibration state visible in Patch and relevant Position tooling;
- no hidden or destructive correction.

Target signal path:

`Programmed Semantic Position -> Fixture Calibration Transform -> Pan/Tilt Conversion -> DMX`

### Stage Calibration / XYZ alignment

A later advanced layer should support multiple known stage reference points and spatial calibration.

Target direction:

- operator defines or selects known stage reference points;
- fixture is aimed at multiple references;
- system derives fixture position/orientation or an equivalent spatial correction model;
- semantic XYZ positions can then survive venue changes more accurately;
- support future integration with Stage Layout / 2D / 3D without making full 3D a prerequisite for the basic offset workflow.

### Integration requirements

Fixture Calibration must integrate with:

- Position presets;
- LIVE effective values and provenance where useful;
- Patch fixture metadata;
- Encoder Drawer Position controls;
- Cue playback without rewriting stored cue data;
- future Stage Layout / 2D / 3D;
- future fixture replacement / touring workflows.

The basic Fixture Alignment / Patch Offset workflow should be implemented before full 3D visualization.

## 15. Quick Grid

`Quick Grid` is the DMX-CONSOL name for the touch-oriented quick-access surface inspired by professional Direct Select workflows.

Target:

- workspace view/pane;
- touch-friendly grid;
- banks and pages;
- paging and jump navigation;
- Fixture targets;
- Group targets;
- Preset targets;
- Effect targets;
- Macro targets;
- Executor targets;
- mixed-target custom banks;
- one-touch activation;
- Fixture/Group actions use the shared selection model, not a parallel selection state;
- tiles expose meaningful live/selected/running/pending state where available;
- layouts persist at Show/Workspace level.

Quick Grid should adopt the useful workflow concept without cloning another console's visual design or terminology.

## 16. Workspace and later roadmap

Still open:

- responsive pane polish and compact modes;
- tablet/touch/accessibility verification;
- Stage Layout / 2D / 3D;
- diagnostics and DMX/device diagnostics;
- effects/phasers operator UX;
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
9. Quick Grid.
10. Preset scope model and full preset workflow: Fixture / Fixture Type / Compatible.
11. Cue Lists + Executors.
12. Patch + Fixture Library / Device Profiles.
13. Fixture Calibration / Venue Adaptation.
14. Effects UX.
15. Workspace polish.
16. Stage / 2D / 3D / diagnostics.

## Definition of done for future UX slices

A UX slice is not considered complete merely because the UI renders.

It should normally require:

- the architecture to use shared Application/console state rather than duplicate UI-only state;
- targeted automated tests;
- `dotnet test` passing;
- manual verification for the relevant operator workflow;
- no merge to `master` until the slice is reviewed and accepted.
