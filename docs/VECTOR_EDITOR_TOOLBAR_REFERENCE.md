# Compulite Vector — Editor Tool Bar Reference

**Status:** Living reference for the H1.6 Contextual Command Surface / Editor Tool Bar milestone.
**Source:** *Vector Reference Guide for Enter Syntax v3.11* (Compulite), the Editor Toolbar chapter
(guide pages 37 and 285–294), read in full and extracted to text via `pdftotext -layout` for this
document. Cross-checked against real screenshots the user captured from a live Vector session
(main window with Cue Sheet / Channel editor / A-B Qlist, and the Workspace Tree panel).
**Not** a copy of Vector's UI — this records what the manual actually says, so our own
`EditorContextStack` / `SoftKeyRegistry` design decisions can be checked against a real reference
instead of guessed. See `docs/UX_PHILOSOPHY.md` for the overall "borrow philosophy, not pixels"
rule this document serves.

---

## 1. What the manual says about the Editor Toolbar itself

> "The Editor toolbar contains buttons used when programming a show. The Editor toolbar is
> **context sensitive**; the buttons change according to the current editing function."
> — p. 37

Structural facts, stated directly:

- **13 buttons per row**, with paging arrows at the row's end if more than 13 apply to the current mode.
- It is opened like any other panel, **from the Workspace Tree**: *Editor Tools → Editor ToolBar*.
  This confirms the observation from the user's own Workspace Tree screenshot — in Vector, the
  Editor Toolbar is itself one entry in the same tree that Channel/Spot/Cues/Groups/Patch/Playback
  Wings/etc. all come from. It is **not** architecturally special the way our own H1.6 spec made
  it (persistent, outside the pane tree, never closable) — that was an explicit choice *we* made
  (H1.6 §2), diverging from Vector on purpose, not by omission. Worth knowing, not worth reverting
  without being asked.
- It is described as "context sensitive" in the *same* sentence, twice, independently — this is
  clearly the single most load-bearing idea in Vector's own documentation of it, matching exactly
  what H1.6 asked for.

## 2. The key structural insight: modes are entered by VERB, not only by OBJECT TYPE

Our current `EditorContextStack` (first H1.6 slice) enters a context when the operator selects an
**object** — clicking a Fixture, a Group, a Cue. Vector's actual mode list shows a second,
**independent** axis: many modes are entered by pressing a **command verb/token**, sometimes nested
several levels deep, regardless of what object (if any) is currently selected:

- Press **CUE** → Cue mode
- Press **EVERY** (after a fixture selection already exists) → Every (selection filter) mode
- Press **DELTA** (only reachable *from inside* Every mode) → Delta mode — a mode nested under
  another mode, not under an object
- Press **TIME** → Time mode
- Press **SHIFT+TIME** → Timeline mode (a *different* mode than Time, reached by a modifier key)
- Press **STORE OPTIONS** (only available after CUE) → Store Options mode
- Press **RATE** → Rate mode
- Press **PROFILE** → Profile mode

This does **not** require a redesign of `EditorContextStack` — `Push(idSegment, label)` already
lets any frame nest under any current frame, which is exactly the mechanism these verb-triggered
sub-modes need. It does mean future slices should let `CommandSurface`/`EditorToolBar` verb-presses
call `Context.Push(...)` directly (not only `EnterObject(...)`), which our architecture already
permits — this is additive scope for the *soft-key trees and their wiring*, not a change to the
context model itself.

## 3. Full mode → button reference (as documented, p. 285–294)

Blank cells below are exactly what the manual leaves blank or marks unimplemented — recorded
faithfully, not filled in with a guess.

### Default (idle) mode
Available when the editor is idle, using Enter syntax.

| Button | What it does |
|---|---|
| EDIT CUE | Go to the selected cue on a playback device and load its levels into the editor — **only** the fixtures/levels actually stored in the cue. |
| LOAD CUE | Open the selected cue in the editor — only the fixtures/levels stored in the cue. |
| LOAD STATE | Open the selected cue for editing — the stored fixtures/levels **plus** everything currently tracking through it. |
| LIBRARY | Set Vector for library selection (Enter/Action syntax only). |
| EDIT LIBRARY | Open the selected library for editing. |
| PATCH | Enter patch mode. |

**Note:** LOAD CUE vs LOAD STATE is a real distinction we don't have yet — it maps directly onto
the Tracking model Step E's plan already reserved space for (`Cue.Levels` sparse, "inherit from a
previous cue") but never implemented. Worth remembering when Tracking is eventually built: the
operator needs an explicit choice between "edit only what THIS cue stores" and "edit what's
actually showing, including tracked-through values."

### Active mode (press ACTIVE)
| Button | What it does |
|---|---|
| STAGE | Grab fixtures visible on stage. |
| EDITOR | Grab fixtures in the editor. |
| MASTER PB | Grab fixtures on the master playback. |
| INVISIBLE PARAMETERS | Grab parameters invisible because their dimmer is at zero. |
| INACTIVE | Grab everything not active on any playback device or in the editor. |
| EVERYTHING | Grab all visible and invisible output. |
| =@ / <@ / >@ | Grab parameters at / below / above a specified level. |

### Cue mode (press CUE)
| Button | What it does |
|---|---|
| CUE ONLY | Tracking mode only — value(s) apply to the current cue only; reverts to tracking on the next fade. |
| EDIT CUE | Load only the cue itself into the editor for modification. |
| LOAD CUE / LOAD STATE | Same distinction as Default mode, above. |
| SKIP | Tracking mode only — on the next fade, ignore the skipped values, revert to previously tracked ones. |
| BACKTRACK | (listed, description not given in the source). |
| BLOCK | Tracking mode only — restart tracking at the block cue. |
| UNBLOCK | Tracking mode only — cancel the block, reinstate tracking. |
| LINK | Link non-sequential cues. |
| LOOP | Opens **Loop mode** (below) for the selected cue range. |
| STORE OPTIONS | Opens **Store Options mode** (below) — only available after CUE. |
| MARK CUE | Tracking mode only. |
| FORCE BLACK CUE | Mark the selected cue as a force-black cue. |
| PROFILE | Opens **Profile mode** (below) for the selected parameter. |
| DELTA | Opens **Delta mode** (below). |
| TRACK SHEET | (listed, description not given in the source). |

### Store Options mode (press STORE OPTIONS, only after CUE)
| Button | What it does |
|---|---|
| ALL EDITOR (red + white) | Store all fixture values currently in the editor into the cue. |
| ACTIVE ONLY (red) | Store only the fixture values actually selected in the editor. |
| ALL STAGE | Store the entire lighting state into the cue. |
| ALL PARAMS FOR SELECTED | All parameter values for selected (red) spots whose dimmer is above zero. |
| ALL PARAMS IF ACTIVE | All parameter values for active (red or white) spots whose dimmer is above zero. |

**Note:** this is a real gap in what we built for UX correction F (`StoreCueCommand`) — our
implementation always does the equivalent of "ALL STAGE" (a full snapshot of every patched
channel, per `CueList.RecordCue`'s existing, unchanged policy from Step E). Vector treats "what
counts as part of this Store" as an explicit operator choice with five distinct meanings. Not
something to change without being asked — recorded here so the gap is a documented, deliberate
deferral rather than an unnoticed one.

### Time mode (press TIME)
| Button | What it does |
|---|---|
| TIME-IN | Fade-in time for parameters moving to higher values. |
| TIME-OUT | Fade time for parameters moving to lower values. |
| DELAY-IN | Delay before parameters moving to higher values start fading. |
| DELAY-OUT | Delay before parameters moving to lower values start fading. |
| WAIT | Opens wait-time options. |
| FOLLOW ON | Fade to the next cue automatically, without waiting for GO. |
| MANUAL | The fade requires an explicit GO. |
| PROFILE | Opens Profile mode. |

**Important correction to our own CUE > TIME tree:** we built GENERAL / INTENSITY / POSITION /
COLOR / BEAM / DELAY as the CUE > TIME sub-keys (matching the *grandMA3* Feature-Group-Timing model
from Step E's own research, not Vector). Vector's actual TIME mode is organized completely
differently — by **In / Out / Delay-In / Delay-Out / Wait / Follow / Manual**, with no per-attribute
breakdown visible in this mode at all. These are two genuinely different timing philosophies (MA3:
"which attribute group gets its own time" vs Vector: "which phase of the fade — in, out, or delay —
gets a time, plus how the NEXT cue is triggered"). Both are legitimate; our built context was
modeled on MA3's Feature-Group-Timing on purpose (Step E's own documented research), not a mistake —
but it's worth being explicit that it is **not** what Vector calls "Time mode." This is a genuine
design fork the user may want to weigh in on before this tree grows further.

### Timeline mode (press SHIFT+TIME)
| Button | What it does |
|---|---|
| TEACH TIME LINE | (listed, description not given in the source). |
| RUN / STOP | Start/stop the timeline. |
| SET TIME | (listed, description not given in the source). |
| RESTART | Restart Vector's internal clock. |
| CLEAR | (listed, description not given in the source). |

### Loop mode (select a cue range, press LOOP)
| Button | What it does |
|---|---|
| AUTO LOOP | GO on the loop's first cue starts the whole loop automatically (default). |
| MANUAL LOOP | Every cue in the loop needs its own GO. |
| AUTO FOLLOW | Automatic fade to the cue after the loop ends. |
| LOOP COUNT | Lit when a specific loop count has been set. |

### Delta mode (press DELTA, only reachable from Every mode)
| Button | What it does |
|---|---|
| RELATIVE | Use the delta's relative values when pasting. |
| ABSOLUTE | Use the delta's absolute values when pasting. |
| ADD NEW | Add fixtures present in the delta but not in the original cue, on paste. |
| COPY NEW DELTA | Store the current parameter levels as a new delta. |
| PASTE | Overwrite a cue's values with the delta's values. |

**Note:** this is the closest documented analogue to the milestone's own Copy/Paste example
(*"CUE 12 → COPY → CUE 20 → PASTE"*) — Vector's real implementation is scoped specifically to
parameter deltas within cue editing, not a generic object clipboard. Worth knowing before COPY/PASTE
get real behavior in a future slice.

### Dimmer / Patch mode (press DIM)
| Button | What it does |
|---|---|
| CLEAR PATCH | Clear patch assignment for the selected dimmer/channel. |
| EXTERNAL DIMMER | Assign an external dimmer (yokes, scrollers, etc.). |
| PARK | Park the selected dimmer. |
| PROPORTIONAL LEVEL | Set a proportional level for the selected dimmer. |
| CLEAR PROPORTIONAL LEVEL | Reset proportional level to 100%. |

### Every / selection-filter mode (select fixtures, press EVERY)
| Button | What it does |
|---|---|
| ODDS / EVENS | Select odd/even-numbered fixtures in the requested range. |
| 3RD / 4TH | Select every 3rd/4th fixture in the range. |
| / (slash) | Advance selection by a specified increment. |
| INTERSECT | **"not implemented yet"** — Vector's own manual, verbatim. |
| INVERT ALL | **"not implemented yet"** — verbatim. |
| INVERT RANGE | **"not implemented yet"** — verbatim. |

**Note:** even Vector's own shipped reference guide documents unimplemented buttons rather than
omitting them or faking behavior — direct precedent for our own H1.6 §16 rule ("show disabled state
or structured 'not implemented yet' — do not create fake behavior"). This is not just consistent
with our approach, it's evidence the approach matches how a real shipped console handles the same
problem.

### Fan mode (press FAN)
Only partially extracted from the source (a figure/diagram occupied most of this section) —
**SPREAD NEGATIVE** is confirmed; the rest needs a follow-up read if this mode becomes relevant.

### Fixture selection mode (press CHANNEL, SPOT, GROUP, or SET)
| Button | What it does |
|---|---|
| LAST SELECTION | Re-select the previous selection. |
| ALL EDITOR | Select the entire editor output. |
| EVERY | Opens Every/selection-filter mode (above). |
| USED IN SHOW | Show all patched fixtures used anywhere in the show (cues or groups). |
| FREE IN SHOW | Show all patched fixtures **not** used anywhere in the show. |
| FLIP | Flip pan/tilt 180° for the selection. |
| REM.DIM. | Black out every fixture's dimmer except the selected one. |
| PAN/TILT PATCH | Opens Pan/Tilt mode (below). |
| PARAM GROUPING | Toggle parameter grouping. |
| STORE OPTIONS | Opens Store Options mode. |
| PROFILE | Opens Profile mode. |
| CONTROL | Ignite the selected fixtures. |
| LOOK AHEAD MASK | Define which fixtures/parameters the LookAhead feature affects. |
| DELTA | Opens Delta mode. |

### Library mode (press LIBRARY or LIB)
| Button | What it does |
|---|---|
| FIXTURE SPECIFIC | Library applies only to the exact fixtures stored in it. |
| DEVICE SPECIFIC | Library applies to any fixture of the same device type. |
| PATTERN | Effects-like — applied sequentially as a repeating pattern. |
| LIBRARY NUMBER | Next entered number is treated as a library identifier. |
| TRACKING | Library is tracked for global cue modification (default). |
| ALL FOR SELECTED BANK | **"Not implemented yet"** — verbatim. |
| INCLUDE TIME | Include the selected parameter's time assignments in the library. |
| INCLUDE EFFECT | Include running effects on the selected parameter. |
| BANK FILTERS OFF | Include all active parameters in the library. |

### Macro mode (press MACRO)
| Button | What it does |
|---|---|
| TEACH MACRO | Collect all subsequent key presses into a macro. |
| MIDI MACRO | (listed, description not given in the source). |

### Pan/Tilt mode (press PAN/TILT PATCH)
| Button | What it does |
|---|---|
| INVERT PAN / INVERT TILT | Swap full and zero values for that axis. |
| SWAP PAN/TILT | Pan becomes tilt and vice versa. |

### Parameter (setting levels) mode (select fixtures + a parameter)
| Button | What it does |
|---|---|
| STEP UP / STEP DOWN | Move a step-indexed parameter to the next/previous step. |
| +@ / -@ | Add/subtract a specified value from the parameter's level. |
| FLIP | Reverse the X/Y axes 180°. |
| REM DIM | (listed, description not given in the source). |
| PAN/TILT PATCH | Opens Pan/Tilt mode. |
| BLOCK | On fade, ignore previous values for this channel/parameter. |
| PROFILE | Opens Profile mode — determines fade behavior. |
| DELTA | Opens Delta mode. |
| STORE OPTIONS | Opens Store Options mode. |

### Profile mode (select a cue + PROFILE, or a cue + TIME + PROFILE)
| Button | What it does |
|---|---|
| JUMP ON START | Parameters jump to their new value at the fade's start. |
| JUMP ON END | Parameters jump to their new value at the fade's end. |
| JUMP ON 50% | Parameters jump to their new value at 50% of the fade. |

### Rate mode (press RATE)
| Button | What it does |
|---|---|
| FREEZE ALL | Stop all chases and effects. |
| FREEZE CHASE | Stop chases only. |
| FREEZE EFFECT | Stop effects only. |

## 4. Relevant Shift-pairs (global, not tied to one toolbar mode)

| Shortcut | Effect |
|---|---|
| SHIFT + ACTIVE | Grab fixtures without specifying the source. |
| SHIFT + FREE | Free all playback devices. |
| SHIFT + GO | Initiate a fade on every loaded playback device except the master. |
| SHIFT + JOIN | Link all playback devices to respond to one command simultaneously. |
| SHIFT + RESET | Fully reset the editor (last fixture selection stays under wheel control until this). |
| SHIFT + SNAP GO | Operate the previous snap. |
| SNAP # + SHIFT + ENTER | Operate a snap in snap-add mode. |
| SHIFT + TIME | Open the Editor toolbar in Timeline mode. |

## 5. What this changes / confirms about our H1.6 work

**Confirmed, not changed:**
- Context-sensitive, mode-driven soft keys — exactly what we built.
- "Show disabled / not-implemented rather than fake behavior" — Vector's own shipped manual does
  the same thing for INTERSECT/INVERT ALL/INVERT RANGE/ALL FOR SELECTED BANK.
- A stable edit-key vocabulary (STORE, indirectly EDIT/DELTA-as-copy-paste) resolved by context —
  present throughout, e.g. STORE OPTIONS appearing identically in Cue mode, Fixture selection mode,
  and Parameter mode.

**Gaps now documented, not yet closed (deliberately, pending direction):**
1. Modes triggered by **verb press**, not only object selection (Every, Delta, Time, Timeline,
   Store Options, Rate, Profile, Loop) — `EditorContextStack.Push` already supports this
   mechanically; wiring it from `CommandSurface`/`EditorToolBar` verb keys is unbuilt.
2. **LOAD CUE vs LOAD STATE** — a real "cue-only vs tracked-state" distinction we don't model yet
   (ties into the Tracking work Step E deferred).
3. **STORE OPTIONS** (5 distinct "what counts as part of this store" choices) — our `StoreCueCommand`
   always does the "ALL STAGE" equivalent; the other four are unbuilt.
4. **CUE > TIME's actual shape** — Vector organizes by In/Out/Delay-In/Delay-Out/Wait/Follow/Manual,
   not by attribute class (which is what we built, following grandMA3's model instead). This is a
   genuine fork between the two references worth an explicit decision, not a silent pick.
5. **Delta mode** (Relative/Absolute/Add New/Copy New Delta/Paste) is Vector's actual COPY/PASTE
   mechanism for cue editing — narrower and more specific than a generic object clipboard.
6. Loop, Rate, Profile, Timeline, Library, Pan/Tilt, Macro modes — not represented in our context
   trees at all yet (Fixture/Group/Cue/Cue.Time only, per H1.6's own first-slice scope).

None of the above is being implemented by writing this document — this is the research record the
user asked for (§16 of H1.6: "read the manual" before the next slice), so the next slice can be
scoped against real facts instead of assumptions.
