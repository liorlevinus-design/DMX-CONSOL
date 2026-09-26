# DMX-CONSOL PATCH / 2D Plot / Spatial Architecture

Status: forward-looking architecture and product decisions. No implementation exists yet for
anything described in this document unless explicitly marked otherwise.
Companion documents: [`OPERATOR_UX_ROADMAP.md`](OPERATOR_UX_ROADMAP.md) (§11 Patch and Fixture
Library / Device Profiles, §14 Fixture Calibration / Venue Adaptation), [`ARCHITECTURE.md`](ARCHITECTURE.md)
(engine layering/history), [`CLAUDE.md`](../CLAUDE.md) (binding invariants, §13 Fixture Profiles).

This document exists to record a set of authoritative product/architecture decisions about the
relationship between PATCH, the future 2D Plot, and a future Spatial Database, before any of that
code is written. It is architecture and intent, not a spec for a shippable feature and not a grant
of scope to implement any of it in the current or next slice.

---

## 1. PATCH is a major workstream, not "Quick Patch"

Existing "Quick Patch" (see `CLAUDE.md` §8) is a narrow, already-implemented structural operation:
atomic, validate-before-mutate, rollback-safe, no normal Programming Undo entry.

That does not make PATCH itself a small feature. PATCH is the console's authoritative owner of:

- fixture identity (manufacturer/model/mode, per `CLAUDE.md` §13 Fixture Profiles);
- technical patch/show-fixture information (footprint, channel offsets, universe/address,
  fixture number, patch state such as `UNPATCHED`);
- the show's technical rig definition that every other subsystem (Encoder assignment, Presets,
  Effects, Cue playback) ultimately reads through the Fixture Profile.

The future PATCH page (`docs/OPERATOR_UX_ROADMAP.md` §11) is expected to grow into a substantial
operator workspace in its own right — comparable in weight to LIVE or the Command Surface — not a
single-purpose add/remove dialog. Nothing about "Quick Patch" being a small, already-shipped verb
should be read as capping the size or ambition of PATCH as a whole.

---

## 2. 2D Plot is not merely a view of PATCH

The 2D Plot is not a rendering of PATCH data. It is a separate subsystem with its own state.

- 2D Plot owns a dedicated **Spatial Database** (spatial state), distinct from the Patch data model.
- PATCH provides the **initial fixture identity and technical seed data** to the Plot (which
  fixture exists, its profile/mode, its patch identity) — the Plot does not invent or duplicate
  that identity.
- Shared fixture identity must **link** PATCH records and Plot records (e.g. by `PatchedFixture`
  identity/number), never blindly duplicate fixture identity fields into a second parallel store.

This mirrors the existing rule that Selection, Programmer and Playback are separate concepts
(`CLAUDE.md` §1) that reference shared identity rather than re-deriving or duplicating each other's
state. The same discipline applies here: PATCH and Spatial Plot are two authoritative stores linked
by identity, not one store with two skins.

---

## 3. What the Plot spatial model may own

The Spatial Database is where spatial intelligence lives. It may own data such as:

- X / Y / Z position;
- hanging point / anchor;
- orientation;
- logical role (e.g. "Back Truss Wash 3", independent of DMX identity);
- focus / target point(s);
- original position (as designed/plotted);
- adapted position (as corrected for the current venue/rig);
- movement/adaptation constraints;
- mapping between venues (how a fixture's role/position in Venue A corresponds to Venue B).

None of this belongs in the Patch data model. PATCH does not need to know a fixture's XYZ position
to patch it; the Plot does not need to know DMX channel offsets to plot it.

---

## 4. Separation of responsibility

- **PATCH is authoritative** for technical fixture/patch identity: manufacturer/model/mode,
  footprint, universe/address, fixture number, patch state.
- **Spatial Plot DB is authoritative** for spatial intelligence: position, orientation, focus,
  venue mapping, adaptation constraints.
- **Not every Plot edit automatically changes PATCH.** Moving a fixture's plotted position does not
  repatch it, reassign its DMX address, or change its profile/mode.
- **Not every PATCH field belongs in the Plot database.** Channel offsets, coarse/fine mapping, and
  other DMX-technical fields stay in the Fixture Profile / Patch model and are never duplicated into
  spatial state.

This is the same boundary discipline as `CLAUDE.md` §3 (UI must not own semantics) and §14 (engine
layering): two authoritative stores, linked by shared identity, each owning only its own domain.

---

## 5. Venue adaptation (long-term intent only)

The long-term goal includes **semi-automatic fixture relocation** when adapting a show to a
different venue:

- spatial relationships recorded in the Spatial Database should make it possible to map a rig from
  Venue A to Venue B;
- for moving lights, future venue adaptation may recalculate focus/target relationships and derive
  new Pan/Tilt values from changed physical positions.

This is the same direction already recorded in `docs/OPERATOR_UX_ROADMAP.md` §14 (Fixture
Calibration / Venue Adaptation) for the single-fixture "correct one known position, apply the
relative correction" workflow, and in §14's Stage Calibration / XYZ alignment subsection for the
later multi-reference-point layer. This document does not replace §14; it records where the data
that a future venue-adaptation feature would need (spatial positions, focus targets, venue mappings)
is expected to live once it exists.

**Explicit statement: none of this is implemented in this slice, and nothing in this document
should be read as implying it exists yet.** No relocation, no recalculation, no Pan/Tilt derivation
from position exists in the codebase today. This section documents architecture and intent only.

---

## 6. Dependency chain

The expected build order, recorded here so later slices are sequenced correctly and nothing is
built out of order:

```
Internal Fixture Profile Model
  -> PATCH Data Model
    -> Serious PATCH Page
      -> 2D Plot Foundation
        -> Spatial Database
          -> Venue Adaptation
          -> Lighting Plot Import Assistant
```

Each stage depends on the one before it existing and being stable. Venue Adaptation and the
Lighting Plot Import Assistant are two separate branches off a stable Spatial Database — neither
depends on the other:

1. **Internal Fixture Profile Model** — the DMX-CONSOL-native profile schema (`CLAUDE.md` §13):
   manufacturer/model/mode, footprint, channel/attribute mapping, physical ranges, capabilities.
2. **PATCH Data Model** — the technical patch/show-fixture record built on top of Fixture Profile
   identity (fixture number, universe/address, footprint instance).
3. **Serious PATCH Page** — the substantial operator workspace described in §1 above and in
   `docs/OPERATOR_UX_ROADMAP.md` §11, built on a stable PATCH Data Model.
4. **2D Plot Foundation** — the first Plot surface, seeded from PATCH fixture identity per §2.
5. **Spatial Database** — the dedicated spatial state described in §2/§3, once the Plot foundation
   exists to read/write it.
6. **Venue Adaptation** — the semi-automatic relocation/recalculation direction in §5, which depends
   on a populated, trustworthy Spatial Database.
7. **Lighting Plot Import Assistant** — the assisted PDF/image import workstream described in §9,
   which depends on stable PATCH and Spatial Database domains to import *into* (§9.18), not on Venue
   Adaptation.

Do not build a later stage ahead of an earlier one (e.g. do not design Venue Adaptation storage
before the Spatial Database exists, do not let 2D Plot invent its own fixture identity ahead of a
stable PATCH Data Model, do not build the Lighting Plot Import Assistant ahead of a stable PATCH
Data Model and Spatial Database).

---

## 7. GDTF / OFL importers feed Fixture Profile only

GDTF and Open Fixture Library (OFL) importers (see `.claude/agents/fixture-profile-librarian.md`,
`docs/OPERATOR_UX_ROADMAP.md` §11 "Fixture Library / Device Profiles") are **input formats** for the
Internal Fixture Profile Model — stage 1 of the dependency chain above.

**They must not become the runtime spatial model.** GDTF/OFL structures (or any future importer's
native structures) must never leak into PATCH, the 2D Plot, or the Spatial Database as runtime
domain types. This restates and extends the existing rule already binding on the Fixture Profile
Librarian agent ("External formats are INPUT formats. They must not become the runtime domain
model.") to explicitly cover the spatial layer as well: an importer may eventually carry rig/hang
metadata from a lighting-design file format, but even then, it feeds the Fixture Profile / Spatial
Database through the same importer → normalize → internal model path — it does not define the
runtime spatial schema itself.

---

## 8. Relationship to existing documents

- This document does not contradict or restate `CLAUDE.md` §8 (Quick Patch) or §13 (Fixture
  Profiles) — it extends them by describing what PATCH grows into and what sits alongside it.
- This document does not contradict `docs/OPERATOR_UX_ROADMAP.md` §11 or §14 — it gives the
  underlying data-architecture rationale for why PATCH is listed as a major workstream and records
  the dependency ordering that §11/§14 do not spell out.
- Nothing here reintroduces or touches the known contradiction called out in `CLAUDE.md` regarding
  per-`AttributeClass` cue timing. That remains unrelated and unchanged.
- §9 (Lighting Plot Import Assistant) does not contradict `docs/OPERATOR_UX_ROADMAP.md` §11a — it
  gives the full architectural detail for the workstream that §11a's companion section summarizes,
  the same relationship §1–§7 already have with `docs/OPERATOR_UX_ROADMAP.md` §11a.

---

## 9. Lighting Plot Import Assistant (future major workstream, not implemented)

Status: forward-looking architecture only. Nothing in this section exists in the codebase. No
schema, model, or code described here is authorized by this section — it records where a future
feature sits once it is built, and what invariants it must respect when it is.

This is the last stage of the dependency chain in §6: it depends on the Internal Fixture Profile
Model, PATCH Data Model, and Spatial Database already existing and being stable (§6 above). It is
documented here, alongside PATCH/Plot/Spatial, because its entire purpose is to populate those two
authoritative stores from an external lighting plot — so it must respect the PATCH-vs-Spatial
separation of responsibility (§4) from day one of its design, not retrofit it later.

### 9.1 Purpose

Let an operator import a lighting plot — vector PDF, scanned/raster PDF, or image — and produce a
**reviewed draft** of PATCH data and Spatial Plot/Spatial Database data from it, instead of manually
re-keying every fixture from a paper or PDF plot.

### 9.2 Canonical architecture (binding shape for this feature)

```
PDF / Image
  -> Plot Import Analyzer
    -> Intermediate Import Model
      -> Review / Correction Workspace
        -> Confirmed Fixture Mapping
          -> PATCH + Spatial Database
```

**Never:** `PDF / Image -> direct PATCH mutation`. There is no path, present or future, by which a
source document mutates PATCH or the Spatial Database without passing through the Intermediate
Import Model and an explicit operator-reviewed commit. This is an assisted import workflow, not an
automatic one.

### 9.3 Core product principle

The importer may detect, interpret, and propose. It must **not** silently invent missing technical
data, and it must not commit uncertain interpretations. Every imported datum retains source
provenance, a confidence value, and a review state (see §9.7 and §9.10). Human confirmation is required
before anything is written into authoritative show data (PATCH or the Spatial Database).

### 9.4 Source ingestion

Three source kinds are supported conceptually:

1. **Vector PDF** — preferred where available. May expose text objects, vector paths, geometry, and
   layers, which can carry native source coordinates.
2. **Raster / scanned PDF** — no reliable text/vector layer; requires OCR and computer-vision
   detection.
3. **Image files** — treated the same as a raster source.

The original source document remains available as an underlay/reference throughout the review
workflow (§9.10) — it is never discarded once import begins, so a human can always check a proposal
against the page it came from.

### 9.5 Coordinate calibration

Before any source geometry becomes authoritative spatial data, the workflow must establish a
coordinate system for the plot. Potential calibration inputs: center line, stage orientation, X/Y
axes, a grid, known dimensions, or operator-selected reference points.

If scale cannot be derived reliably from the source, the operator must be able to select two points
on the drawing and enter their known physical distance. **The system must never invent scale.**

Spatial coordinates derived from the source retain a link back to their original source-plan
coordinates (see the `SourceCoordinates` field in §9.7) so a placement can always be traced to where
it was measured from.

### 9.6 Symbol key / legend

If the drawing contains a symbol key/legend, the workflow may: identify the legend region, extract
symbol samples and their labels, and propose mappings from detected symbols to internal Fixture
Profiles/Modes — for example, a detected symbol plus nearby legend text "ETC Source Four 36°"
suggesting the internal profile "ETC Source Four 36°" at confidence 0.94.

The operator can confirm a mapping once and apply it to every matching symbol in the drawing (batch
confirmation). If no legend exists, a future version may cluster visually similar symbols and
request manual mapping instead. **The system must never silently assume symbol identity.**

### 9.7 Intermediate Import Model

The workflow requires a non-authoritative, staging-only model — conceptually named
`ImportFixtureCandidate` here as a working label, not a finalized schema or a committed type name.
Its fields below are described as suggested/conceptual documentation, not as an implementation
contract:

- **Identity/source:** `CandidateId`, `SourceDocumentId`, `SourcePage`, `SourceBoundingBox`,
  `SourceCoordinates`, `DetectedSymbolClass`, `NearbyRawText`.
- **Suggested interpretations:** `SuggestedProfile`, `SuggestedMode`, `SuggestedUnitNumber`,
  `SuggestedChannel`, `SuggestedDimmer`, `SuggestedUniverse`, `SuggestedAddress`, `SuggestedColor`,
  `SuggestedPosition`, `SuggestedPurpose`/focus note where available.
- **Confidence** (independent values, never a single collapsed score — see §9.11):
  `SymbolConfidence`, `TextConfidence`, `ProfileMappingConfidence`, `PatchDataConfidence`,
  `PositionConfidence`.
- **Review:** `ReviewStatus`, plus operator corrections/confirmations applied to the candidate.

This intermediate model is **not** the runtime Patch or Spatial domain. It is a staging area that
exists only until it is confirmed or rejected in the Review Workspace (§9.10).

### 9.8 Metadata extraction

The importer may recognize nearby annotations such as channel, unit number, dimmer, universe/
address, gel/color, hanging position, and purpose/focus notes. Input text formats vary widely in
practice — for example `"Ch 12"`, `"12"`, `"Dim 24"`, `"U2/145"`, `"2.145"`, `"L201"`, `"LX2"`,
`"FOH 1"`.

Each parsed datum must preserve, together: the original raw text, the interpreted value, a
confidence value, and the source region it came from. **The original source representation is
never discarded.**

### 9.9 Hanging positions / structures

The workflow may detect or let the operator confirm hanging structures such as: pipe, truss, boom,
floor position, side ladder, FOH, balcony rail, or other hanging positions. A candidate fixture may
link to a hanging position; this relationship may later feed PATCH's Position field, Spatial
anchors, and venue adaptation (§5).

This link must **not** collapse Patch position metadata and Spatial geometry into one domain — it
reinforces, and must never weaken, the PATCH-vs-Spatial separation of responsibility already
established in §4.

### 9.10 Review Workspace

A dedicated review/correction workflow, distinct from PATCH and from the Plot itself, is required
before any commit. The operator should be able to view, side by side: the original lighting plan,
detected fixture overlays, confidence/unresolved state, the mapped fixture type, extracted
metadata, and the source crop/location a candidate came from. Example summary line: "Detected
fixtures: 46, High-confidence: 32, Needs review: 10, Unrecognized symbols: 4."

Operator actions in this workspace should eventually include: confirm, correct, reject, map a
symbol, batch-map all matching symbols, assign profile/mode, correct extracted labels, resolve
Patch conflicts, and resolve position mappings.

### 9.11 Confidence model

Confidence is **never** a single magic score. The model keeps independent confidence values for (at
minimum) symbol detection, OCR/text extraction, fixture-profile mapping, Patch metadata, and
position/spatial interpretation (the fields listed in §9.7). This lets an operator accept one
interpretation for a candidate — e.g. its profile mapping — while still requesting review for
another — e.g. its position.

### 9.12 Patch Draft and Spatial Draft

After review/mapping, the importer may construct two parallel, linked drafts — never one merged
draft — consistent with §4's PATCH-vs-Spatial separation:

**Patch Draft** may contain: fixture identity, profile, mode, unit number, universe/address when
actually known, hanging position, and source notes. If a DMX address is absent or uncertain, the
fixture is kept **unpatched**. The importer must not automatically choose a free address; automatic
address assignment, if it is ever built, is a separate, explicit future feature — not part of this
one.

**Spatial Draft** may contain: X, Y, optional Z, source-plan coordinates, hanging point/anchor,
orientation when known, original/adapted position fields as appropriate (per §3/§5), confidence,
and provenance.

PATCH and the Spatial Database remain two separate domains linked by shared durable fixture
identity, exactly as established in §2/§4 — the importer must not blur that boundary by, for
example, writing spatial fields into the Patch Draft or technical patch fields into the Spatial
Draft.

### 9.13 Import commit

Only an explicit operator action (e.g. "COMMIT IMPORT") writes reviewed data into the authoritative
PATCH and Spatial Database domains. Before commit, the workflow validates at least: duplicate
fixture/unit identifiers, unresolved fixture profiles/modes, overlapping DMX addresses, invalid
universe/address values, unresolved coordinates, and unsupported/unmapped fields.

The commit must be atomic where practical: a failed validation must not leave a half-imported show.
This echoes the atomicity principle already required of Quick Patch in `CLAUDE.md` §8, applied here
to a larger, multi-record import instead of a single patch operation.

### 9.14 Provenance (hard architectural requirement)

Every imported Patch or Spatial datum must retain enough provenance to trace it back to the source
document and source region it was derived from. This includes, at minimum: source document/revision
ID, page, bounding box/source coordinates, original raw text, import adapter/analyzer version, and
confidence/review state.

This is a hard requirement, not a nice-to-have: it is what makes correction, audit, re-import,
revision comparison (§9.15), and future machine-learning improvements to the importer possible.

### 9.15 Revision / re-import

A future capability supports revised lighting plots without redoing an import from scratch:

```
Plot v1 -> Import -> PATCH + Spatial
Plot v2 -> Analyze -> Compare against existing imported source -> Proposed changes -> Operator review -> Apply
```

Potential detected differences: fixture added, fixture removed, fixture moved, fixture number
changed, profile changed, DMX address changed, hanging position changed, annotation changed.

No automatic destructive behavior is defined for revision comparison yet — every proposed change
goes through the same operator review before it is applied.

### 9.16 Future outputs enabled by this data (not built here)

Once confirmed Patch/Spatial data exists via this workflow, other future systems may derive from
it: Patch, the 2D Plot, 3D visualization, an instrument schedule, channel hookup, position lists,
universe/address reports, and venue-adaptation inputs. None of these downstream outputs is
implemented as part of this feature; they are noted only to show why the provenance and confidence
requirements above matter beyond the importer itself.

### 9.17 Phased delivery (MVP and beyond)

- **Phase 1 — Manual Assisted Import:** load PDF/image as an underlay, establish scale/coordinate
  axes, manual symbol mapping, assisted fixture placement, create draft Patch + Spatial records.
- **Phase 2 — Symbol Detection:** detect fixture symbols, cluster repeated symbols, legend mapping,
  batch confirmation.
- **Phase 3 — Metadata Recognition:** OCR/text extraction, channel/unit/address/color/position
  suggestions, confidence/review.
- **Phase 4 — Full Draft Generation:** Patch Draft, Spatial Draft, validation, explicit commit.
- **Phase 5 — Revision Compare:** import a revised plot, detect differences, propose changes,
  review/apply.

### 9.18 Dependencies

Serious implementation requires these foundations to already be stable:

1. Internal Fixture Profile Model (§6 stage 1, `CLAUDE.md` §13).
2. Durable Fixture/Profile IDs.
3. A serious PATCH data model (§6 stage 2–3).
4. A Spatial Database (§6 stage 5).
5. Shared durable fixture identity linking Patch and Spatial records (§2).
6. Show persistence.

GDTF/OFL remain profile-data **input adapters** only and must not become the runtime spatial/import
domain model — this is the same constraint already stated in §7, applied here to the importer as
well: whatever a future importer parses from a lighting-design file format still feeds the Fixture
Profile / Spatial Database through the same importer → normalize → internal model path described in
§7, never bypassing it.

### 9.19 Explicitly out of scope for now

The following are not implemented, not promised for any near-term version, and not authorized by
this section: fully automatic interpretation of arbitrary plans; automatic 3D reconstruction;
guessing Z without evidence; focus calculation; automatic DMX address assignment; direct unreviewed
Patch mutation; venue adaptation performed during import; automatic destructive synchronization
from revised plans.
