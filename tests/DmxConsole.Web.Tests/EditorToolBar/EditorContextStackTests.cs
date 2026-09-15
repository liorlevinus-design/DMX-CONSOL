using DmxConsole.Web.EditorToolBar;

namespace DmxConsole.Web.Tests.EditorToolBar;

public class EditorContextStackTests
{
    [Fact]
    public void NewStack_StartsAtRoot()
    {
        var stack = new EditorContextStack();

        Assert.True(stack.IsAtRoot);
        Assert.Same(EditorContextStack.RootFrame, stack.Current);
        Assert.Empty(stack.Frames);
    }

    [Fact]
    public void EnterObject_PushesOneFrame_WithObjectTypeAndSelection()
    {
        var stack = new EditorContextStack();
        var fixtureRef = new object();

        stack.EnterObject(EditorObjectType.Fixture, "FIXTURE", fixtureRef, "Fixture 3");

        Assert.False(stack.IsAtRoot);
        Assert.Equal("Fixture", stack.Current.Id);
        Assert.Equal("FIXTURE", stack.Current.Label);
        Assert.Equal(EditorObjectType.Fixture, stack.Current.ObjectType);
        Assert.Same(fixtureRef, stack.Current.SelectedObject);
        Assert.Equal("Fixture 3", stack.Current.SelectedObjectLabel);
        Assert.Single(stack.Frames);
    }

    [Fact]
    public void EnterObject_ReplacesWholeStack_EvenWhenNested()
    {
        var stack = new EditorContextStack();
        stack.EnterObject(EditorObjectType.Cue, "CUE");
        stack.Push("Time", "TIME");
        Assert.Equal(2, stack.Frames.Count);

        stack.EnterObject(EditorObjectType.Group, "GROUP");

        Assert.Single(stack.Frames);
        Assert.Equal(EditorObjectType.Group, stack.Current.ObjectType);
        Assert.Equal("Group", stack.Current.Id);
    }

    [Fact]
    public void Push_NestsUnderCurrent_BuildingADottedId()
    {
        var stack = new EditorContextStack();
        stack.EnterObject(EditorObjectType.Cue, "CUE");

        stack.Push("Time", "TIME");

        Assert.Equal(2, stack.Frames.Count);
        Assert.Equal("Cue.Time", stack.Current.Id);
        Assert.Equal("TIME", stack.Current.Label);
        Assert.Equal(EditorObjectType.Cue, stack.Current.ObjectType); // inherited from parent
    }

    [Fact]
    public void Push_AtRoot_IsANoOp()
    {
        var stack = new EditorContextStack();

        stack.Push("Time", "TIME");

        Assert.True(stack.IsAtRoot);
    }

    [Fact]
    public void Back_PopsOneFrame_ReturningToParent()
    {
        var stack = new EditorContextStack();
        stack.EnterObject(EditorObjectType.Cue, "CUE");
        stack.Push("Time", "TIME");
        stack.Push("Position", "POSITION");

        stack.Back();

        Assert.Equal("Cue.Time", stack.Current.Id);
        stack.Back();
        Assert.Equal("Cue", stack.Current.Id);
        stack.Back();
        Assert.True(stack.IsAtRoot);
    }

    [Fact]
    public void Back_AtRoot_IsANoOp()
    {
        var stack = new EditorContextStack();
        stack.Back();
        Assert.True(stack.IsAtRoot);
    }

    [Fact]
    public void Root_ClearsTheWholeStack_InOneStep()
    {
        var stack = new EditorContextStack();
        stack.EnterObject(EditorObjectType.Cue, "CUE");
        stack.Push("Time", "TIME");
        stack.Push("Position", "POSITION");

        stack.Root();

        Assert.True(stack.IsAtRoot);
    }

    [Fact]
    public void Changed_FiresOnEveryMutation()
    {
        var stack = new EditorContextStack();
        int fireCount = 0;
        stack.Changed += () => fireCount++;

        stack.EnterObject(EditorObjectType.Fixture, "FIXTURE");
        stack.Push("Sub", "SUB");
        stack.Back();
        stack.Root();

        Assert.Equal(4, fireCount);
    }

    [Fact]
    public void Changed_DoesNotFire_ForNoOpBackOrPushAtRoot()
    {
        var stack = new EditorContextStack();
        int fireCount = 0;
        stack.Changed += () => fireCount++;

        stack.Back();
        stack.Push("x", "X");
        stack.Root();

        Assert.Equal(0, fireCount);
    }
}
