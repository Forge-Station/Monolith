using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Forge.ShipWeapons.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class ShipWeaponBoardComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan FabricationTime = TimeSpan.FromSeconds(10);

    [DataField]
    public int RequiredPartRating = 1;
}
