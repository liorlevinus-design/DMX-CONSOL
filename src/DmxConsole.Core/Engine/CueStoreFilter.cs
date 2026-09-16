namespace DmxConsole.Core.Engine;

/// <summary>Vector's five STORE OPTIONS meanings (docs/VECTOR_EDITOR_TOOLBAR_REFERENCE.md §3
/// "Store Options mode") - what counts as "part of this Store", an explicit operator choice
/// instead of always doing the equivalent of AllStage. AllEditor/ActiveOnly are sparse (only
/// Programmer-touched channels are stored - exercising the sparse Cue.Levels model Step E
/// reserved for exactly this); AllParamsForSelected/AllParamsIfActive read live merged output and
/// gate on the fixture's Intensity channel being above zero.</summary>
public enum CueStoreFilter { AllEditor, ActiveOnly, AllStage, AllParamsForSelected, AllParamsIfActive }
