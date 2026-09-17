using CommunityToolkit.Mvvm.ComponentModel;
using DmxConsole.Application;
using DmxConsole.Application.Commands.Programmer;
using DmxConsole.Core;
using DmxConsole.Core.Fixtures;
using DmxConsole.Web.Workspaces;

namespace DmxConsole.Web.Services;

/// <summary>One fixed encoder slot's display state - always exactly 5 of these per page,
/// EmptySlot for positions with no parameter to show. Partial (only some selected fixtures
/// support this channel) is orthogonal to Mixed (the fixtures that DO support it disagree on
/// value) - both can be true at once. Unit/MinValue/MaxValue come from FixtureChannel's own
/// calibration (default "DMX"/0/255 - see FixtureChannel's own doc comment). MixedRange is
/// orthogonal to both: it means the fixtures that DO support this channel disagree on the
/// calibration itself (different Unit/MinValue/MaxValue), so Unit/MinValue/MaxValue above must
/// not be trusted as "the" range - a UI must show MIXED RANGE instead of one fixture's range
/// passed off as shared. AllValues is one raw byte per supporting fixture (in selection order) -
/// what a Mixed Value Strip draws real markers from, never an invented aggregate.</summary>
public sealed record EncoderSlot(ChannelType? Type, string Label, byte Value, bool Mixed,
    bool Partial, bool IsProgrammerTouched, string Unit, double MinValue, double MaxValue,
    bool MixedRange = false, IReadOnlyList<byte>? AllValues = null)
{
    public double DisplayValue => MinValue + (Value / 255.0) * (MaxValue - MinValue);

    public IReadOnlyList<byte> AllValuesOrEmpty => AllValues ?? Array.Empty<byte>();

    public static readonly EncoderSlot Empty = new(null, string.Empty, 0, false, false, false, "DMX", 0, 255);
}

/// <summary>
/// Milestone 1 (work-plan handoff, 2026-09-16): the fixed Encoder Drawer - always present,
/// open/closed rather than conjured by context. Categories match Compulite Vector's actual
/// documented Editor Toolbar banks, grouped via the one authoritative AttributeClass model
/// (docs/COMMAND_SURFACE_KEY_SPEC.md §23.1 - this used to be a separate EncoderCategory type,
/// now unified with Release/Preset/Fixtures-LIVE's own family grouping).
/// ActiveCategory/Page are sticky across selection changes and across Open/Close - only reset
/// when the active category genuinely stops applying to the current selection.
/// </summary>
public partial class EncoderDrawerViewModel : ObservableObject
{
    /// <summary>The six real, selectable families in fixed display order - never includes
    /// AttributeClass.Other, which is not a family a channel can be "in" from the operator's
    /// point of view. Exposed publicly so EncoderDrawer.razor's disabled-placeholder fallback
    /// (shown when nothing is selected) can render the same fixed six tabs instead of iterating
    /// the raw enum, which would incorrectly include Other.</summary>
    public static IReadOnlyList<AttributeClass> CategoryOrder => ChannelTypeExtensions.SelectableFamilies;

    private readonly CommandDispatcher _dispatcher;
    private readonly ProgrammerViewModel _programmerVm;
    private ConsoleContext Context => _programmerVm.Context;

    /// <summary>At most one gesture may be live at a time across the whole drawer (knob, value
    /// strip, Position pad, Color Picker all share this). Starting a new one safely cancels
    /// whatever was in progress first - see BeginGesture.</summary>
    private EncoderGesture? _activeGesture;

    [ObservableProperty] private bool _isOpen = true;
    [ObservableProperty] private AttributeClass? _activeCategory;
    [ObservableProperty] private int _page;

    public EncoderDrawerViewModel(CommandDispatcher dispatcher, ProgrammerViewModel programmerVm)
    {
        _dispatcher = dispatcher;
        _programmerVm = programmerVm;
    }

    public void Open() => IsOpen = true;

    public void Close()
    {
        CancelActiveGestureIfAny();
        IsOpen = false;
    }

    public void Toggle()
    {
        if (IsOpen) CancelActiveGestureIfAny();
        IsOpen = !IsOpen;
    }

    /// <summary>Loads persisted state (the active Workspace's own EncoderDrawerState) - called by
    /// EncoderDrawer.razor on init and whenever the active Workspace itself changes. Applied
    /// directly, no validation - the caller re-validates against the current selection afterward
    /// if needed (RevalidateActiveCategory), same as any other selection-driven category change.</summary>
    public void RestoreState(EncoderDrawerState state)
    {
        IsOpen = state.IsOpen;
        ActiveCategory = state.ActiveCategory;
        Page = state.Page;
    }

    /// <summary>The inverse of RestoreState - mirrors this ViewModel's current runtime state back
    /// into the given (mutable, in-place) EncoderDrawerState, e.g. the active Workspace's own
    /// EncoderDrawer field, so a later explicit Save picks it up like any other layout edit.</summary>
    public void CopyStateInto(EncoderDrawerState state)
    {
        state.IsOpen = IsOpen;
        state.ActiveCategory = ActiveCategory;
        state.Page = Page;
    }

    public void SelectCategory(AttributeClass category)
    {
        CancelActiveGestureIfAny();
        ActiveCategory = category;
        Page = 0;
    }

    public void NextPage()
    {
        if ((Page + 1) * 5 < ChannelTypesForActiveCategory().Count) Page++;
    }

    public void PreviousPage()
    {
        if (Page > 0) Page--;
    }

    /// <summary>Every category with at least one matching channel on at least one selected
    /// fixture (a partial match is still shown - not required on every fixture in the
    /// selection), in the fixed Vector-bank display order.</summary>
    public IReadOnlyList<AttributeClass> AvailableCategories()
    {
        var present = new HashSet<AttributeClass>();
        foreach (var fixture in Context.Selection.Items)
            foreach (var channel in fixture.Mode.Channels)
            {
                var category = channel.Type.ToAttributeClass();
                if (category != AttributeClass.Other) present.Add(category);
            }

        return CategoryOrder.Where(present.Contains).ToList();
    }

    /// <summary>Called after anything that might invalidate ActiveCategory (selection changes) -
    /// resets to null only if the current category is no longer available; never resets Page
    /// unless ActiveCategory itself actually changes, per the "open/close never loses
    /// category/page" acceptance criterion. Also auto-selects the first available category when
    /// none is active yet (e.g. the very first selection after an empty one) - but never
    /// overrides a category the operator already picked while it's still valid.</summary>
    public void RevalidateActiveCategory()
    {
        var available = AvailableCategories();

        if (ActiveCategory is { } current && !available.Contains(current))
        {
            ActiveCategory = null;
            Page = 0;
        }

        if (ActiveCategory is null && available.Count > 0)
        {
            ActiveCategory = available[0];
            Page = 0;
        }
    }

    /// <summary>Always exactly 5 slots - EncoderSlot.Empty for positions beyond what the
    /// active category/page actually has.</summary>
    public IReadOnlyList<EncoderSlot> SlotsForCurrentPage()
    {
        var types = ChannelTypesForActiveCategory();
        var windowed = types.Skip(Page * 5).Take(5).ToList();
        var slots = windowed.Select(BuildSlot).ToList();
        while (slots.Count < 5) slots.Add(EncoderSlot.Empty);
        return slots;
    }

    public int PageCount()
    {
        int count = ChannelTypesForActiveCategory().Count;
        return count == 0 ? 1 : (count + 4) / 5;
    }

    /// <summary>Builds the live aggregate state for a specific channel type, including Mixed,
    /// Partial and MixedRange. Special controls (Position pad and Color Picker) use the exact
    /// same source of truth as the five encoder strips rather than maintaining parallel state.</summary>
    public EncoderSlot SlotFor(ChannelType type) => BuildSlot(type);

    // ---------- Position pad ----------

    /// <summary>Shown only when the active category is Position and the selection actually has
    /// BOTH Pan and Tilt as real channel types - never invented for a fixture that lacks one of
    /// them.</summary>
    public bool ShowsPositionPad() =>
        ActiveCategory == AttributeClass.Position
        && ChannelTypesForActiveCategory().Contains(ChannelType.Pan)
        && ChannelTypesForActiveCategory().Contains(ChannelType.Tilt);

    /// <summary>Pan-vs-Pan and Tilt-vs-Tilt uniformity are checked completely separately (per the
    /// review correction) - Pan and Tilt are unrelated physical axes and are never required to
    /// share a range with EACH OTHER, only with themselves across the selected fixtures.</summary>
    public bool PositionPadHasMixedRange() => SlotFor(ChannelType.Pan).MixedRange || SlotFor(ChannelType.Tilt).MixedRange;

    /// <summary>True only when every selected fixture that has Pan/Tilt declares a real
    /// calibration (Unit != "DMX") for BOTH axes - otherwise the pad still renders, but in an
    /// explicitly-labeled raw-normalized space (0..255 on each axis), never pretending a
    /// meaningless byte range is degrees.</summary>
    public bool PositionPadIsCalibrated() => SlotFor(ChannelType.Pan).Unit != "DMX" && SlotFor(ChannelType.Tilt).Unit != "DMX";

    // ---------- Color Picker ----------

    /// <summary>Shown only when EVERY selected fixture (not just some) has the full ColorRed/
    /// ColorGreen/ColorBlue triple - a partial match must never render a picker that silently
    /// ignores a fixture, or pretends a ColorWheel-only fixture has RGB.</summary>
    public bool ShowsColorPicker() =>
        ActiveCategory == AttributeClass.Color
        && Context.Selection.Items.Count > 0
        && Context.Selection.Items.All(HasFullRgb);

    /// <summary>Some but not all of the selection has full RGB - the picker is withheld and the
    /// caller shows an explicit PARTIAL label instead, per the same principle Slot.Partial already
    /// uses elsewhere in this drawer.</summary>
    public bool ColorPickerIsPartial() =>
        ActiveCategory == AttributeClass.Color
        && Context.Selection.Items.Count > 0
        && Context.Selection.Items.Any(HasFullRgb)
        && !ShowsColorPicker();

    private static bool HasFullRgb(PatchedFixture fixture) =>
        fixture.FindChannel(ChannelType.ColorRed) is not null
        && fixture.FindChannel(ChannelType.ColorGreen) is not null
        && fixture.FindChannel(ChannelType.ColorBlue) is not null;

    private List<ChannelType> ChannelTypesForActiveCategory()
    {
        if (ActiveCategory is not { } category) return new List<ChannelType>();
        return Context.Selection.Items
            .SelectMany(f => f.Mode.Channels)
            .Where(c => c.Type.ToAttributeClass() == category)
            .Select(c => c.Type).Distinct().OrderBy(t => t).ToList();
    }

    private EncoderSlot BuildSlot(ChannelType type)
    {
        var allSelected = Context.Selection.Items;
        var perFixture = allSelected
            .Select(f => (Fixture: f, Channel: f.FindChannel(type)))
            .Where(x => x.Channel is not null)
            .ToList();

        var values = perFixture.Select(x => Context.EffectiveOutput.GetEffectiveValue(x.Fixture.UniverseId, x.Fixture.AbsoluteIndex(x.Channel!))).ToList();
        bool touched = perFixture.Any(x => Context.Programmer.HasStoredValue(x.Fixture.UniverseId, x.Fixture.AbsoluteIndex(x.Channel!), out _));

        byte value = values.Count == 0 ? (byte)0 : values[0];
        bool mixed = values.Count > 0 && values.Any(v => v != values[0]);
        bool partial = perFixture.Count < allSelected.Count;

        var firstChannel = perFixture.Count > 0 ? perFixture[0].Channel! : null;
        string unit = firstChannel?.Unit ?? "DMX";
        double min = firstChannel?.MinValue ?? 0;
        double max = firstChannel?.MaxValue ?? 255;

        // MIXED RANGE: the fixtures that DO support this channel disagree on the calibration
        // itself, not just the value - comparing every supporting fixture's own channel, not
        // just the first one against nothing.
        bool mixedRange = perFixture.Count > 1 && perFixture.Skip(1)
            .Any(x => x.Channel!.Unit != unit || x.Channel!.MinValue != min || x.Channel!.MaxValue != max);

        return new EncoderSlot(type, EncoderLabel(type), value, mixed, partial, touched, unit, min, max,
            mixedRange, values);
    }

    private static string EncoderLabel(ChannelType value) => value.ToString().Replace("Color", "").Replace("Rotation", " Rot");

    public void SetValue(ChannelType type, byte value) => _programmerVm.SetEncoderValue(type, value);

    // ---------- Gesture transaction (knob drag, wheel, value strip, Position pad, Color Picker) ----------

    /// <summary>Starts a new gesture for the given channel types (one type = a single knob/value
    /// strip; two = Position's Pan+Tilt; three = Color's R+G+B). Only one gesture may be live at
    /// a time across the whole drawer - if one is already in progress, it's safely cancelled
    /// (restored to its own snapshot) first, never left dangling.</summary>
    public EncoderGesture BeginGesture(params ChannelType[] types)
    {
        CancelActiveGestureIfAny();
        var gesture = EncoderGesture.Begin(Context, types);
        _activeGesture = gesture;
        return gesture;
    }

    /// <summary>No-op if the given gesture isn't the currently-active one (already superseded by
    /// a newer BeginGesture, or already committed/cancelled) - this is what makes a stale wheel-
    /// debounce callback or an out-of-order pointer event harmless instead of corrupting state.</summary>
    public void PreviewGesture(EncoderGesture gesture, ChannelType type, byte value)
    {
        if (!ReferenceEquals(gesture, _activeGesture)) return;

        if (gesture.SelectionChanged(Context))
        {
            gesture.RestoreSnapshot(Context);
            _activeGesture = null;
            return;
        }

        gesture.Preview(Context, type, value);
    }

    /// <summary>finalValues: one final byte per channel type in the gesture. Multiple entries
    /// (Position/Color) commit as a single CompositeCommand - one Undo step for every channel
    /// together. Always restores the original per-target snapshot FIRST, so the real Command's
    /// own before/after capture (ProgrammerChannelCommandBase) sees the true pre-gesture state,
    /// not the gesture's last live-preview value.</summary>
    public void CommitGesture(EncoderGesture gesture, IReadOnlyDictionary<ChannelType, byte> finalValues)
    {
        if (!ReferenceEquals(gesture, _activeGesture)) return;
        _activeGesture = null;

        gesture.RestoreSnapshot(Context);
        if (gesture.SelectionChanged(Context)) return; // changed mid-gesture - never applied to a new selection

        var commands = finalValues
            .Select(kv => (Targets: gesture.TargetFixtures(Context, kv.Key), kv.Key, kv.Value))
            .Where(t => t.Targets.Count > 0)
            .Select(t => (IConsoleCommand)new SetAttributeValueCommand(t.Targets, t.Key, t.Value))
            .ToList();
        if (commands.Count == 0) return;

        if (commands.Count == 1) _dispatcher.Dispatch(commands[0]);
        else _dispatcher.DispatchBatch(commands);
        _programmerVm.RefreshAllFaders();
    }

    /// <summary>Restores every target to its own private snapshot - no Command, no Undo entry.</summary>
    public void CancelGesture(EncoderGesture gesture)
    {
        if (ReferenceEquals(gesture, _activeGesture)) _activeGesture = null;
        gesture.RestoreSnapshot(Context);
    }

    /// <summary>Called from every exit path that must never leave a live-preview value stranded
    /// in the Programmer: selection change, workspace switch, category switch, drawer close,
    /// and (via each Razor component's own Dispose) circuit disconnect/navigation-away. Idempotent
    /// - safe to call when no gesture is active.</summary>
    public void CancelActiveGestureIfAny()
    {
        if (_activeGesture is { } gesture) CancelGesture(gesture);
    }

    /// <summary>Direct numeric entry - displayValue is in the slot's own display unit (e.g. a
    /// typed "50" for a %-calibrated channel), converted via the first matching selected
    /// fixture's own FixtureChannel.FromDisplayValue (clamps to [MinValue,MaxValue] before
    /// converting, same validation the knob's own range enforces). No-op (false) if no selected
    /// fixture actually has this channel - never writes to a fixture that doesn't support the
    /// parameter - or if displayValue is NaN/Infinity (e.g. a user literally typing "NaN"/
    /// "Infinity", which double.TryParse accepts): rejected here, before it would otherwise reach
    /// FixtureChannel.FromDisplayValue's own defensive throw, so a bad numeric-entry keystroke
    /// never surfaces an exception up through the Razor layer.</summary>
    public bool TrySetDisplayValue(ChannelType type, double displayValue)
    {
        if (!double.IsFinite(displayValue)) return false;

        var channel = Context.Selection.Items.Select(f => f.FindChannel(type)).FirstOrDefault(c => c is not null);
        if (channel is null) return false;

        SetValue(type, channel.FromDisplayValue(displayValue));
        return true;
    }

    public void Min(ChannelType type) => _programmerVm.SetEncoderValue(type, 0);

    public void Max(ChannelType type) => _programmerVm.SetEncoderValue(type, 255);

    /// <summary>Each fixture's own FixtureChannel.DefaultValue, not one shared value - fixtures
    /// with different defaults for this channel type genuinely need different target bytes.
    /// Batched into one CompositeCommand (via DispatchBatch) so this is a single Undo step.</summary>
    public void Home(ChannelType type)
    {
        var targets = Context.Selection.Items
            .Select(f => (Fixture: f, Channel: f.FindChannel(type)))
            .Where(x => x.Channel is not null)
            .ToList();
        if (targets.Count == 0) return;

        var commands = targets
            .Select(x => (IConsoleCommand)new SetAttributeValueCommand(new List<PatchedFixture> { x.Fixture }, type, x.Channel!.DefaultValue))
            .ToList();

        _dispatcher.DispatchBatch(commands);
        _programmerVm.RefreshAllFaders();
    }
}
