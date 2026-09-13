using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
using Xunit;

namespace DmxConsole.Core.Tests;

public class CueValueTests
{
    private static FixtureProfile PanTiltMover() => new()
    {
        Id = "test-pan-tilt",
        Manufacturer = "Test",
        Model = "PanTilt",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "2ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 0 },
                    new FixtureChannel { Name = "Tilt", Type = ChannelType.Tilt, Offset = 1 },
                },
            },
        },
    };

    private static (Patch Patch, PatchedFixture Fixture) BuildPatch()
    {
        var patch = new Patch();
        var profile = PanTiltMover();
        var fixture = new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1);
        patch.Add(fixture);
        return (patch, fixture);
    }

    [Fact]
    public void RecordCueWithPresetRefs_UsesPresetRef_OnlyForMatchingAttributeAndChannelType()
    {
        var (patch, fixture) = BuildPatch();
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 111); // Pan - will be overridden by the preset ref
        programmer.SetChannel(0, 1, 222); // Tilt - has no entry in the override preset, stays Absolute

        var presets = new PresetLibrary();
        var preset = new Preset { Class = AttributeClass.Position, Name = "Centre", Number = 1 };
        preset.Values[ChannelType.Pan] = 128;
        presets.Add(preset);

        var cueList = new CueList(presetResolver: presets);
        var overrides = new Dictionary<AttributeClass, Preset> { [AttributeClass.Position] = preset };
        var cue = cueList.RecordCueWithPresetRefs(patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero, overrides);

        var panValue = cue.Levels[(0, 0)];
        var tiltValue = cue.Levels[(0, 1)];
        Assert.Equal(CueValueKind.PresetRef, panValue.Kind);
        Assert.Equal(preset.Id, panValue.PresetId);
        Assert.Equal(CueValueKind.Absolute, tiltValue.Kind);
        Assert.Equal(222, tiltValue.AbsoluteValue);
    }

    [Fact]
    public void Playback_ResolvesPresetRef_ToTheLibrarysCurrentValue()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 0);

        var presets = new PresetLibrary();
        var preset = new Preset { Class = AttributeClass.Position, Name = "Centre", Number = 1 };
        preset.Values[ChannelType.Pan] = 128;
        presets.Add(preset);

        var cueList = new CueList(presetResolver: presets);
        var overrides = new Dictionary<AttributeClass, Preset> { [AttributeClass.Position] = preset };
        cueList.RecordCueWithPresetRefs(patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero, overrides);
        cueList.Go();

        Assert.True(cueList.TryGetChannelValue(0, 0, out var value));
        Assert.Equal(128, value);
    }

    [Fact]
    public void Playback_PresetDeleted_ChannelStopsContributing_NoThrow()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();

        var presets = new PresetLibrary();
        var preset = new Preset { Class = AttributeClass.Position, Name = "Centre", Number = 1 };
        preset.Values[ChannelType.Pan] = 128;
        presets.Add(preset);

        var cueList = new CueList(presetResolver: presets);
        var overrides = new Dictionary<AttributeClass, Preset> { [AttributeClass.Position] = preset };
        cueList.RecordCueWithPresetRefs(patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero, overrides);
        cueList.Go();

        presets.Remove(preset);

        var exception = Record.Exception(() => cueList.TryGetChannelValue(0, 0, out var value));
        Assert.Null(exception);
        Assert.False(cueList.TryGetChannelValue(0, 0, out _));
    }

    [Fact]
    public void Playback_PresetNoLongerContainsChannelType_ChannelStopsContributing_NoThrow()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();

        var presets = new PresetLibrary();
        var preset = new Preset { Class = AttributeClass.Position, Name = "Centre", Number = 1 };
        preset.Values[ChannelType.Pan] = 128;
        presets.Add(preset);

        var cueList = new CueList(presetResolver: presets);
        var overrides = new Dictionary<AttributeClass, Preset> { [AttributeClass.Position] = preset };
        cueList.RecordCueWithPresetRefs(patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero, overrides);
        cueList.Go();

        preset.Values.Remove(ChannelType.Pan); // e.g. re-recorded without Pan among the targets

        Assert.False(cueList.TryGetChannelValue(0, 0, out _));
    }

    [Fact]
    public void Playback_PresetUpdatedBetweenTicks_ReflectsNewValue_NoCaching()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();

        var presets = new PresetLibrary();
        var preset = new Preset { Class = AttributeClass.Position, Name = "Centre", Number = 1 };
        preset.Values[ChannelType.Pan] = 128;
        presets.Add(preset);

        var cueList = new CueList(presetResolver: presets);
        var overrides = new Dictionary<AttributeClass, Preset> { [AttributeClass.Position] = preset };
        cueList.RecordCueWithPresetRefs(patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero, overrides);
        cueList.Go();

        Assert.True(cueList.TryGetChannelValue(0, 0, out var before));
        Assert.Equal(128, before);

        preset.Values[ChannelType.Pan] = 200;

        Assert.True(cueList.TryGetChannelValue(0, 0, out var after));
        Assert.Equal(200, after);
    }

    [Fact]
    public void TimingPrecedence_AttributeOverride_BeatsGeneral_ChannelOverride_BeatsBoth()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 50); // Pan
        programmer.SetChannel(0, 1, 50); // Tilt

        var cueList = new CueList();
        var general = new CueTiming(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
        var positionOverride = new CueTiming(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        var channelOverride = new CueTiming(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(9));

        var cue = cueList.RecordCue(patch, programmer, "Cue 1", 1, general.FadeInTime, general.FadeOutTime);
        var withTiming = new Cue
        {
            Number = cue.Number,
            Name = cue.Name,
            GeneralTiming = general,
            AttributeTiming = new Dictionary<AttributeClass, CueTiming> { [AttributeClass.Position] = positionOverride },
            ChannelTiming = new Dictionary<(int, int), CueTiming> { [(0, 0)] = channelOverride },
            Levels = cue.Levels,
        };

        var panValue = withTiming.Levels[(0, 0)];
        var tiltValue = withTiming.Levels[(0, 1)];

        Assert.Equal(channelOverride, withTiming.TimingFor((0, 0), panValue)); // channel override wins for Pan
        Assert.Equal(positionOverride, withTiming.TimingFor((0, 1), tiltValue)); // no channel override -> AttributeTiming for Position
    }
}
