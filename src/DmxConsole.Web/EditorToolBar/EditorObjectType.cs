namespace DmxConsole.Web.EditorToolBar;

/// <summary>
/// The top-level object families the Editor Tool Bar can enter a context for (H1.6 §4). Not
/// every one has a built tree yet - see SoftKeyRegistryBuilder for which are real this slice
/// (Fixture/Group/Cue) versus reserved for later (the rest still resolve to a context id with no
/// registered keys, which renders as an empty tool bar rather than an error - never a crash for
/// an object family that's merely not built out yet).
/// </summary>
public enum EditorObjectType
{
    None,
    Fixture,
    Group,
    Cue,
    Preset,
    Executor,
    Submaster,
    Effect,
    Macro,
    Patch,
    Programmer,
}
