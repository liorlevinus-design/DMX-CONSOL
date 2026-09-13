# UX / Workflow Philosophy

**Status:** Living reference. Applies to all future UI, Playback, Effects, and Command-Line work.
**Written:** before Step F, after Steps A–E (semantic Command layer, Programmer, Presets, Cue-by-reference).

## Mission Statement

> Professional power underneath.
> Immediate operational clarity on top.
>
> MA-level capability, with Vector/ETC-like operational clarity.

This is not a UI skin decision. It is a constraint on **every** future architectural choice: the internal model
(`DmxConsole.Core` / `DmxConsole.Application`) is allowed to be as deep and general as grandMA3's, but nothing
reaches the operator that isn't as immediately legible as Vector's or ETC Eos's screens. When those two pull in
different directions, the internal model bends toward depth and the surface bends toward clarity - never the
reverse (we don't flatten the model to make a screen simpler, and we don't bury depth behind a screen that hides
what's actually happening).

We are not copying any single console's UI. We are borrowing **philosophy**, not pixels, from four systems, each
used for what it does best (see [Reference Roles](#reference-roles) at the end of this document).

---

## 1. Visual State, Not Hidden State

Every state listed below must be distinguishable **at a glance**, without opening an inspector:

`Selected` · `Active` · `Manual/Programmer` · `Cue-owned` · `Tracked` · `Hard value / Move instruction` ·
`Blocked` · `Preset referenced` · `Knocked out` · `Released` · `Fading` · `Paused` · `Blind` · `Parked` ·
`Playback-owned` · `Warning` · `Broken/stale reference`

Grounding from research (ETC Eos's Level 1 workbook is unusually explicit about this - it teaches new operators
to *read the stage from channel color alone*, before they touch a single control):

- Eos channel/parameter color convention (Live Summary): **Red** = manual data not yet stored, **Blue** = value
  moving up from the previous cue, **Green** = value moving down, **Magenta** = tracked (unchanged from the
  previous cue), **Yellow** = driven by a Submaster, **White** = Blocked, **Orange** = owned by a currently
  running (live) playback.
- Eos channel-*number* convention (separate from the value color): plain white = patched, gray = unpatched,
  gray-no-outline = deleted, gold = captured, gold outline = selected, bright white with small "p" = parked.
- Eos distinguishes **Block** (an editing-time wall: nothing tracks through or past it) from **Assert** (a
  playback-time instruction: "collect every unfinished fade and force this cue's full look onto stage, using its
  own timing") from **Autoblock** (the console silently protecting a redundant value change it made on your
  behalf, shown differently again - underlined white). Three different concepts, three different marks. We must
  not collapse these into one generic "locked" indicator once we build Tracking (Step E already reserves the
  model space for this - see `docs/ARCHITECTURE.md`, Step E).
- MagicQ playback buttons: Flash lit **green=Add / red=Swap**, steady vs **flashing** distinguishes "on" from
  "on and held over"; Go lit green means running, Pause lit red means paused. Button *color* and button
  *blink state* are two independent signal channels, not one.

**Rule for us:** every one of the states in the list above needs its own decision recorded once we build the
screen that shows it - "how do we mark X" is itself a design question, not an afterthought, and it must reuse a
visual vocabulary (see §2) rather than invent a new one per screen.

## 2. Color Is Semantic, Not Decorative

A color means the same thing everywhere it appears in the app, permanently. Candidate semantic slots to define
formally when we design the first screen that needs them (not committing to hex values yet - that's a design
task, not an architecture task):

`Manual` · `Selected` · `Tracking` · `Move Up` · `Move Down` · `Blocked` · `Preset Reference` · `Warning` ·
`Error` · `Active Playback` · `Pending Cue` · `Running Fade` · `Blind`

Never spend a color on decoration. If a control needs visual interest, use shape, spacing, or icon - not a color
already claimed by the semantic table.

## 3. Command Line Is a First-Class Interface

Every input surface is a peer client of one Command layer - none of them is "the real one" with the others
bolted on:

```
Touch UI          ┐
Command Line      │
Voice / NL        ├──►  DmxConsole.Application Commands  ──►  Core / DMX
MIDI / OSC        │
Macros            ┘
```

This is not new - it's the architecture Steps C0–C1 already built (`IConsoleCommand`, `CommandDispatcher`,
`ConsoleContext`). What's new here is the **command line as a visible, always-available UI surface**, not just an
internal abstraction. It must show, live, as the operator builds a command:

- the command being built (target, action, timing) - before it's committed
- warnings/errors/ambiguity - before or immediately after commit
- the result - after commit

Examples of the target shape (final syntax TBD at implementation time, not fixed here):

```
Fixture 1 Thru 12 → Intensity 40
Group Fronts → +20
Preset Position 3 → Apply
Store Cue 18 → In 8 / Out 13
```

Eos's own command-line convention is worth studying structurally (not copying verbatim): every command is
**target → action/parameter → value → Enter**, and the console echoes exactly what will happen before it
commits (e.g. record confirmations, `[Enter][Enter]` for destructive double-confirm on Delete). That
target-action-value shape, plus "destructive needs a second confirm," are patterns worth carrying forward.

## 4. Direct Syntax Instead of Forced Menus

If the operator already knows what they want, never force a screen or dialog open first. Vector's direct-syntax
patch flow (`DIM x`, `CHANNEL x`, `STORE`) and Eos's command-line patch (`[601] [At] [250] [Enter]` patches
channel 601 to address 250, entirely from the keypad, never opening the Patch screen) are the model. Patch is not
"a screen with an optional command line" - it is a set of operations (Patch / Unpatch / set fixture type / etc.)
that the command line can perform directly, and the Patch **grid** is one *view* onto the same operations, not
the only entry point.

The same principle extends to everything we will eventually expose on the command line:
Patch · Unpatch · Fixture type · Group creation · Store cue · Update cue · Store preset · Load playback ·
Assign executor · Timing · Effects.

Exact syntax is a future design decision (not fixed by this document) - the architectural commitment is only:
**every one of these must be reachable without opening a screen**, because the Command layer (§3) already makes
"screen" and "command line" two equally-valid frontends to the same operation.

## 5. Fast Path + Inspectable Path

Every significant feature needs both, backed by the *same* Command/Application layer - never two divergent code
paths that can drift apart:

| Feature | Fast Path | Inspectable Path |
|---|---|---|
| Patch | direct syntax | Patch Grid |
| Cue | `Store Cue 12 Time 5` | Cue Sheet / Cue Editor |
| Preset | `Store Position 3` | Preset Pool / Grid |

If a future feature only gets one of the two, that's a design gap to flag, not a shortcut to take silently.

## 6. Context-Sensitive UI

The control surface changes with what the operator is working on - it is never a static wall of every possible
control:

- Fixtures selected → Selection / Programmer / Attributes
- Preset selected → Apply / Update / Store / Delete / Inspect
- Inside a Cue → Timing / Tracking / Cue Only / Block / Update
- On a Playback → Go / Back / Pause / Flash / Rate / Assign

Vector's context-sensitive control philosophy is the direct model here. Concretely for us: a screen should ask
"what does the current selection/context actually need control over *right now*" rather than "what could a user
theoretically ever want from this screen."

## 7. Dense Professional View + Touch View - Same Data, Two Shapes

Not a choice between "professional" and "tablet-friendly" - both, over the same backend data, chosen per
screen/workflow:

- **Tile / Soft-Key View** - touch-friendly, large buttons, groups, presets, executors, quick operations.
- **Grid / Table View** - dense, spreadsheet-like, many fixtures/parameters at once, cue sheet, patch,
  programmer, diagnostics.

This mirrors Vector's soft-key/grid duality and Eos's Summary/Table/Spreadsheet views of the same show data.
Every data model we build should be presentable both ways without a second parallel data path - the view is a
projection, not a second source of truth.

## 8. Filtering Without Hiding Truth

View filters (Eos's **Flexi** is the direct model) narrow what's *shown*, never what *is*:

`All` · `Selected` · `Active` · `Programmer` · `In Use` · `Cue-owned` · `Changed` · `Tracked` · `Errors`

(Eos's actual Flexi states for reference: All / Patched / Manual / Show / Active / In Use / Selected - our list
above is the DMX-GEN equivalent vocabulary, not required to match 1:1.)

A filter is **presentation only**. It must never mutate Selection, Programmer, or any other real state as a side
effect of being turned on - if a filter ever needs to change state to work, that's a bug, not a feature.

## 9. Fade/Timing Are Visual — Time First, Not Percentage First

**This is a deliberate correction, not a preference.** During a running cue, the headline information is
**time remaining**, not percent complete:

```
Cue 18
Elapsed: 3.2s
Remaining: 4.8s
Total: 8.0s
```

If the cue has per-Attribute-Class timing (which Step E's `Cue.GeneralTiming` / `AttributeTiming` /
`ChannelTiming` model already supports - see `docs/ARCHITECTURE.md`), each class gets its own elapsed/remaining
line:

```
INT       6.1 / 8.0s     1.9s remaining
POSITION  2.6 / 5.0s     2.4s remaining
COLOR     3.4 / 12.0s    8.6s remaining
BEAM      5.5 / 7.0s     1.5s remaining
```

A progress bar is fine as a secondary visual, and a percentage is fine as optional secondary text - but the
number an operator's eye should land on first is **seconds remaining**, because that's the question a real
operator standing at the desk is actually asking ("how long until this finishes"), not "what fraction is done."
Any future playback-status UI (Step F onward) must be designed to this rule, not to whatever a progress-bar
component happens to expose by default.

## 10. Command Feedback

Every meaningful action - regardless of which frontend triggered it (§3) - gets immediate, structured feedback:

1. The command line shows what was understood: `Cold Wash → Intensity -20`
2. The affected Fixtures/Group are highlighted
3. The new values are visible
4. Ambiguity/warnings are shown clearly, never silently resolved:

```
⚠ Cold Wash matched 2 groups:
- Cold Wash Stage
- Cold Wash Floor
```

No silent guessing - this is not a new rule for this document, it restates a constraint already binding on
`CommandResult`/the NL layer since Step C0 (`docs/ARCHITECTURE.md`), now extended explicitly to every UI surface,
not just the data model.

## 11. The NL Programmer Is Not a Separate World

There is no separate "AI state." Every NL utterance decomposes into the exact same real operations any other
frontend performs:

- "select the fronts" → the **real** Selection changes
- "raise them by twenty" → the **real** Programmer changes
- "store" → a **real** Cue is created
- "undo" → the **real**, ordinary Undo history runs

This was already the explicit design constraint on `ConsoleContext` from Step C0 (it holds console state, never
conversational memory) - this section just restates it as a UX principle: the NL layer is one more frontend
client of `DmxConsole.Application`, not a parallel console.

## 12. Don't Make the User Think in DMX

The operator's vocabulary is Fixture / Group / Attribute / Preset / Cue / Playback. DMX address is an
implementation detail of the bottom layer - shown only where it's actually the point (Patch, diagnostics, raw
output), never as the default way of referring to anything during normal operation. This is already how
`DmxConsole.Application` is shaped (Commands operate on fixtures/attributes, never raw addresses;
`CommandResult.PreviousValues`/`NewValues` are keyed by `(FixtureId, ChannelType)`, not `(Universe, Channel)`) -
this section makes it an explicit UI-facing rule too, not just an internal one.

## 13. Playback UI Must Be Operational, Not Just Beautiful

See the dedicated architecture-comparison and Step F recommendation, planned separately (Plan Mode, pending your
approval) per your instruction not to assume `Executor = CueList + Fader + Flash` without first comparing how
grandMA3, MagicQ, Vector, and Eos actually model playback.

## 14. Effects Philosophy — Vector-Clarity UI, Phaser-Depth Model

- **UI language** (Vector-style, intuitive, what the operator touches): Primitive · Base · Swing · Size · Rate ·
  Offset · Duty Cycle · Fan · Grouping.
- **Internal model** (grandMA3 Phaser-depth, future-proof, not required to be built now): Steps ·
  Absolute/Relative · Phase · Speed · Transition · Accel/Decel · Distribution.

Not built in Step F. The commitment here is architectural insurance: whatever internal Effects model we design
later must be able to express the Vector-style controls as a *front-end simplification* of a deeper model,
never the other way around - so we don't inherit a shallow model now and hit a rewrite when Phaser-depth
features are eventually wanted. (This mirrors exactly the lesson already applied to Cue/Preset design in Step E:
build the deep model quietly now, expose the simple surface first.)

## 15. Design Questions for Every New UX Decision

Ask all eight, every time, before committing to a design:

1. What's fastest in real time?
2. What's clearest to the eye?
3. What's hardest to trigger by accident?
4. Can an experienced operator do this without opening a menu?
5. Is all meaningful state visible?
6. Can the same command arrive from Touch, Command Line, *and* NL?
7. Is the information actually highlighted the information that actually matters to the operator?
8. Does a pretty UI hide operational information? If so - fix it.

---

## Reference Roles

| Console | Use as reference for |
|---|---|
| **grandMA3** | Internal flexibility, Phasers, preset/cue relationships, executor depth, complex show structures. |
| **MagicQ** | IPCB, palettes, cue stacks, pragmatic workflow, playback master/flash semantics (Grand Master, Sub Master, DBO, Add/Swap). |
| **Compulite Vector** | Visual hierarchy, context-sensitive UI, fast command workflows, playback language, effect UX, progress visualization, grid/soft-key duality, immediate operational feedback. |
| **ETC Eos** | Command-line workflow, tracking/cue-only visibility (Track, Cue Only, Block, Assert, Autoblock), semantic color coding, Live/Blind clarity, Flexi filtering, direct syntax, cue/playback information density. |

None of these is copied as UI. All four are mined for **philosophy**, filtered through the mission statement at
the top of this document: professional power underneath, immediate operational clarity on top.
