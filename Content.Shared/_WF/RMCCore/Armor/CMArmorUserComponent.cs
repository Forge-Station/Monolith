using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Armor;

[RegisterComponent, NetworkedComponent]
[Access(typeof(CMArmorSystem))]
public sealed partial class CMArmorUserComponent : Component;
