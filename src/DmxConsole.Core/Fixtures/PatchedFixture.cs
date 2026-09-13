namespace DmxConsole.Core.Fixtures;

/// <summary>
/// One physical instance of a fixture placed in the rig: a profile+mode combination
/// patched at a specific universe/address, with its own name and position calibration.
/// </summary>
public sealed class PatchedFixture
{
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// Stable, operator-facing fixture number (e.g. "12", used in selection syntax like
    /// "12 Thru 18"). 0 means "not assigned yet" - <see cref="Patch.Add"/> assigns the next
    /// free number automatically unless one was already set explicitly before patching.
    /// </summary>
    public int Number { get; set; }

    /// <summary>User-facing name, e.g. "Mover 1 - Stage Left".</summary>
    public string Name { get; set; } = string.Empty;

    public FixtureProfile Profile { get; }
    public FixtureMode Mode { get; }

    public int UniverseId { get; set; }

    /// <summary>1-based DMX start address (1..512), as it would be shown on the fixture's own display.</summary>
    private int _startAddress = 1;
    public int StartAddress
    {
        get => _startAddress;
        set
        {
            if (value < 1 || value + Mode.FootprintSize - 1 > Core.Universe.ChannelCount)
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Start address {value} with footprint {Mode.FootprintSize} does not fit in a {Core.Universe.ChannelCount}-channel universe.");
            _startAddress = value;
        }
    }

    public int Footprint => Mode.FootprintSize;

    /// <summary>Zero-based index (StartAddress - 1), matching Universe's indexer.</summary>
    public int StartIndex => StartAddress - 1;

    public PanTiltCalibration Calibration { get; set; } = new();

    public PatchedFixture(FixtureProfile profile, FixtureMode mode, int universeId, int startAddress, string? name = null)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        Mode = mode ?? throw new ArgumentNullException(nameof(mode));
        UniverseId = universeId;
        StartAddress = startAddress;
        Name = name ?? profile.DisplayName;
    }

    /// <summary>The absolute 0-based channel index (within its universe) for a given fixture channel.</summary>
    public int AbsoluteIndex(FixtureChannel channel) => StartIndex + channel.Offset;

    public FixtureChannel? FindChannel(ChannelType type) => Mode.Channels.FirstOrDefault(c => c.Type == type);

    /// <summary>True if this fixture's footprint in its universe overlaps the given other fixture.</summary>
    public bool OverlapsWith(PatchedFixture other)
    {
        if (UniverseId != other.UniverseId) return false;
        int aStart = StartIndex, aEnd = StartIndex + Footprint - 1;
        int bStart = other.StartIndex, bEnd = other.StartIndex + other.Footprint - 1;
        return aStart <= bEnd && bStart <= aEnd;
    }
}
