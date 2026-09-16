using CommunityToolkit.Mvvm.ComponentModel;
using DmxConsole.Application;
using DmxConsole.Application.Commands.Programmer;
using DmxConsole.Core;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Web.Services;

/// <summary>One fixed encoder slot's display state - always exactly 5 of these per page,
/// EmptySlot for positions with no parameter to show.</summary>
public sealed record EncoderSlot(ChannelType? Type, string Label, byte Value, bool Mixed, bool IsProgrammerTouched)
{
    public static readonly EncoderSlot Empty = new(null, string.Empty, 0, false, false);
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
    /// category/page" acceptance criterion.</summary>
    public void RevalidateActiveCategory()
    {
        if (ActiveCategory is { } current && !AvailableCategories().Contains(current))
        {
            ActiveCategory = null;
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
        var perFixture = Context.Selection.Items
            .Select(f => (Fixture: f, Channel: f.FindChannel(type)))
            .Where(x => x.Channel is not null)
            .ToList();

        var values = perFixture.Select(x => Context.EffectiveOutput.GetEffectiveValue(x.Fixture.UniverseId, x.Fixture.AbsoluteIndex(x.Channel!))).ToList();
        bool touched = perFixture.Any(x => Context.Programmer.HasStoredValue(x.Fixture.UniverseId, x.Fixture.AbsoluteIndex(x.Channel!), out _));

        byte value = values.Count == 0 ? (byte)0 : values[0];
        bool mixed = values.Count > 0 && values.Any(v => v != values[0]);

        return new EncoderSlot(type, EncoderLabel(type), value, mixed, touched);
    }

    private static string EncoderLabel(ChannelType value) => value.ToString().Replace("Color", "").Replace("Rotation", " Rot");

    public void SetValue(ChannelType type, byte value) => _programmerVm.SetEncoderValue(type, value);

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
