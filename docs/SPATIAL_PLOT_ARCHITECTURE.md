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
```

Each stage depends on the one before it existing and being stable:

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

Do not build a later stage ahead of an earlier one (e.g. do not design Venue Adaptation storage
before the Spatial Database exists, do not let 2D Plot invent its own fixture identity ahead of a
stable PATCH Data Model).

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
