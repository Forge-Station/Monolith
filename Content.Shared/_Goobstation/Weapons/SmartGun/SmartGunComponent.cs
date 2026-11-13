using Content.Shared.DoAfter; // Forge-Change
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Goobstation.Weapons.SmartGun;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SmartGunComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool RequiresWield;

    // Forge-Change-Start
    [DataField, AutoNetworkedField]
    public SmartGunMode Mode = SmartGunMode.Company;

    [DataField, AutoNetworkedField]
    public HashSet<EntityUid> SelectedTargets = new();

    [DataField]
    public float TargetSelectionDelay = 1f;
    // Forge-Change-End
}

// Forge-Change-Start
[Serializable, NetSerializable]
public enum SmartGunMode : byte
{
    Disabled,
    Company,
    Humanoids,
    Selected,
}

[Serializable, NetSerializable]
public sealed partial class SmartGunTargetSelectionDoAfterEvent : DoAfterEvent
{
    public NetEntity Target;

    public override DoAfterEvent Clone() => this;
}
// Forge-Change-End
