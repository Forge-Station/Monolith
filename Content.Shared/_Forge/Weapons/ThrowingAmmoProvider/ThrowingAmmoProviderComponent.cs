using Content.Shared.Containers.ItemSlots;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Weapons.ThrowingAmmoProvider;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(true)]
public sealed partial class ThrowingAmmoProviderComponent : Component
{
    [DataField]
    public string ContainerId = "throwing_ammo";

    [DataField, AutoNetworkedField]
    public int Capacity = 6;

    [DataField, AutoNetworkedField]
    public float DamageMultiplier = 3f;

    [DataField, AutoNetworkedField]
    public float MinimumThrowFlyTime = 0.15f;

    [DataField]
    public EntityWhitelist? Whitelist;

    [AutoNetworkedField]
    public int AmmoCount;

    [DataField]
    public ItemSlot SlotTemplate = new();
}

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class CannonBoostedComponent : Component
{
    [DataField, AutoNetworkedField]
    public float MinimumFlyTime;
}

[RegisterComponent]
public sealed partial class ThrowingAmmoDamageBoostComponent : Component
{
    [DataField]
    public float DamageMultiplier;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class HiddenUntilThrownComponent : Component
{
}

public enum JunkCannonVisualLayers
{
    Empty,
    Loaded
}
