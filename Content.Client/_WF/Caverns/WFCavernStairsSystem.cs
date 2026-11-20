using Content.Shared._WF.Caverns;

namespace Content.Client._WF.Caverns;

/// <summary>Lets the stairs' construction ghost go down only in a cavern; the server checks the ground above.</summary>
public sealed partial class WFCavernStairsSystem : SharedWFCavernStairsSystem;
