using Robust.Shared.GameStates;

namespace Content.Shared._WF.MilitaryVehicles;

/// <summary>A military chassis: the only kind of vehicle that takes weapon and turret hardpoints.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFMilitaryVehicleComponent : Component;

/// <summary>A heavy vehicle licence, issued to one person. Carrying one lets its holder buy military vehicles and weapons.</summary>
[RegisterComponent]
public sealed partial class WFHeavyVehicleLicenceComponent : Component
{
    /// <summary>The name of whoever it was issued to, or null while blank.</summary>
    [DataField]
    public string? Holder;
}
