using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Deafness;

[Access(typeof(SharedDeafnessSystem))]
[RegisterComponent, NetworkedComponent]
public sealed partial class DeafenWhileCritComponent : Component;
