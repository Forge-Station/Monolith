using System;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>On a mob a vehicle ran over: which vehicle, and how long it stays held down and clear of the hull's collision.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VehicleRunoverComponent : Component
{
    [AutoNetworkedField]
    public EntityUid Vehicle;

    public TimeSpan Duration;
    public TimeSpan ExpiresAt;
}
