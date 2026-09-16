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
/// calibration (default "DMX"/0/255 - see FixtureChannel's own doc comment).</summary>
public sealed record EncoderSlot(ChannelType? Type, string Label, byte Value, bool Mixed,
    bool Partial, bool IsProgrammerTouched, string Unit, double MinValue, double MaxValue)
{
    public double DisplayValue => MinValue + (Value / 255.0) * (MaxValue - MinValue);

    public static readonly EncoderSlot Empty = new(null, string.Empty, 0, false, false, false, "DMX", 0, 255);
}

/// <summary>
/// Milestone 1 (work-plan handoff, 2026-09-16): the fixed Encoder Drawer - always present,
/// open/closed rather than conjured by context. Categories match Compulite Vector's actual
/// documented Editor Toolbar banks (see EncoderCategory's own doc comment), NOT AttributeClass.
/// ActiveCategory/Page are sticky across selection changes and across Open/Close - only reset
/// when the active category genuinely stops applying to the current selection.
/// </summary>
public partial class EncoderDrawerViewModel : ObservableObject
{
    private static readonly EncoderCategory[] CategoryOrder =
    {
        EncoderCategory.Intensity, EncoderCategory.Position, EncoderCategory.Color,
        EncoderCategory.Beam, EncoderCategory.Image, EncoderCategory.Shape,
    };

    private readonly CommandDispatcher _dispatcher;
    private readonly ProgrammerViewModel _programmerVm;
    private ConsoleContext Context => _programmerVm.Context;

    [ObservableProperty] private bool _isOpen = true;
    [ObservableProperty] private EncoderCategory? _activeCategory;
    [ObservableProperty] private int _page;

    public EncoderDrawerViewModel(CommandDispatcher dispatcher, ProgrammerViewModel programmerVm)
    {
        _dispatcher = dispatcher;
        _programmerVm = programmerVm;
    }

    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;
    public void Toggle() => IsOpen = !IsOpen;

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

    public void SelectCategory(EncoderCategory category)
    {
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
    public IReadOnlyList<EncoderCategory> AvailableCategories()
    {
        var present = new HashSet<EncoderCategory>();
        foreach (var fixture in Context.Selection.Items)
            foreach (var channel in fixture.Mode.Channels)
                if (channel.Type.ToEncoderCategory() is { } category)
                    present.Add(category);

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

    private List<ChannelType> ChannelTypesForActiveCategory()
    {
        if (ActiveCategory is not { } category) return new List<ChannelType>();
        return Context.Selection.Items
            .SelectMany(f => f.Mode.Channels)
            .Where(c => c.Type.ToEncoderCategory() == category)
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

        // Unit/Min/Max come from the first matching fixture's own calibration - if the selection
        // mixes fixtures with genuinely different calibration for the same ChannelType, Mixed
        // already flags the value disagreement; the display unit itself isn't re-validated here.
        var firstChannel = perFixture.Count > 0 ? perFixture[0].Channel! : null;
        string unit = firstChannel?.Unit ?? "DMX";
        double min = firstChannel?.MinValue ?? 0;
        double max = firstChannel?.MaxValue ?? 255;

        return new EncoderSlot(type, EncoderLabel(type), value, mixed, partial, touched, unit, min, max);
    }

    private static string EncoderLabel(ChannelType value) => value.ToString().Replace("Color", "").Replace("Rotation", " Rot");

    public void SetValue(ChannelType type, byte value) => _programmerVm.SetEncoderValue(type, value);

    /// <summary>Direct numeric entry - displayValue is in the slot's own display unit (e.g. a
    /// typed "50" for a %-calibrated channel), converted via the first matching selected
    /// fixture's own FixtureChannel.FromDisplayValue (clamps to [MinValue,MaxValue] before
    /// converting, same validation the knob's own range enforces). No-op if no selected fixture
    /// actually has this channel - never writes to a fixture that doesn't support the
    /// parameter.</summary>
    public bool TrySetDisplayValue(ChannelType type, double displayValue)
    {
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
