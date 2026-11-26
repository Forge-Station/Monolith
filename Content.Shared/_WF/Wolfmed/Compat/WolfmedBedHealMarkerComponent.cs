using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Compat;

/// <summary>Shared stand-in for the server-only HealOnBuckleComponent so shared wound code can test for a healing bed.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedBedHealMarkerComponent : Component;
