using DmxConsole.Web.EditorToolBar;

namespace DmxConsole.Web.Tests.EditorToolBar;

public class SoftKeyRegistryTests
{
    [Fact]
    public void For_UnregisteredContext_ReturnsEmpty_NotThrow()
    {
        var registry = new SoftKeyRegistry();

        var keys = registry.For("NoSuchContext");

        Assert.Empty(keys);
    }

    [Fact]
    public void Register_ThenFor_ReturnsExactlyThatContextsKeys()
    {
        var registry = new SoftKeyRegistry();
        registry.Register(new SoftKeyDefinition { Id = "a", Label = "A", ContextId = "Fixture" });
        registry.Register(new SoftKeyDefinition { Id = "b", Label = "B", ContextId = "Group" });
        registry.Register(new SoftKeyDefinition { Id = "c", Label = "C", ContextId = "Fixture" });

        var fixtureKeys = registry.For("Fixture");

        Assert.Equal(2, fixtureKeys.Count);
        Assert.Contains(fixtureKeys, k => k.Id == "a");
        Assert.Contains(fixtureKeys, k => k.Id == "c");
    }

    [Fact]
    public void Builder_RootContext_OffersTheThreeRealObjectFamilies()
    {
        var registry = SoftKeyRegistryBuilder.Build();

        var rootKeys = registry.For("Root");

        Assert.Contains(rootKeys, k => k.Label == "FIXTURE" && k.ActionType == SoftKeyActionType.EnterContext);
        Assert.Contains(rootKeys, k => k.Label == "GROUP" && k.ActionType == SoftKeyActionType.EnterContext);
        Assert.Contains(rootKeys, k => k.Label == "CUE" && k.ActionType == SoftKeyActionType.EnterContext);
    }

    [Fact]
    public void Builder_CueContext_HasATimeKeyThatEntersANestedContext()
    {
        var registry = SoftKeyRegistryBuilder.Build();

        var cueKeys = registry.For("Cue");
        var timeKey = Assert.Single(cueKeys, k => k.Label == "TIME");

        Assert.Equal(SoftKeyActionType.EnterContext, timeKey.ActionType);
        Assert.Equal("Time", timeKey.NextContextIdSegment);

        var timeContextKeys = registry.For("Cue.Time");
        Assert.NotEmpty(timeContextKeys);
        Assert.Contains(timeContextKeys, k => k.Label == "POSITION");
    }

    [Fact]
    public void Builder_UnimplementedObjectFamilies_HaveNoRegisteredKeys()
    {
        var registry = SoftKeyRegistryBuilder.Build();

        // H1.6 §4 lists these as future object families - this slice only builds real trees for
        // Fixture/Group/Cue. An empty result here is the honest, non-crashing default.
        Assert.Empty(registry.For("Preset"));
        Assert.Empty(registry.For("Executor"));
        Assert.Empty(registry.For("Submaster"));
        Assert.Empty(registry.For("Effect"));
    }

    [Fact]
    public void Builder_StableEditKeys_AreOfferedConsistentlyAcrossRealContexts()
    {
        var registry = SoftKeyRegistryBuilder.Build();

        foreach (var contextId in new[] { "Fixture", "Group", "Cue" })
        {
            var keys = registry.For(contextId);
            Assert.Contains(keys, k => k.Label == "EDIT");
            Assert.Contains(keys, k => k.Label == "COPY");
            Assert.Contains(keys, k => k.Label == "PASTE");
            Assert.Contains(keys, k => k.Label == "MOVE");
        }
    }
}
