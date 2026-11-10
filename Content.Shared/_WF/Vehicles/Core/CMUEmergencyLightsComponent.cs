using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared.Actions;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>A red and blue lightbar on a vehicle that its operator cycles through off, slow, medium and fast.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(CMUEmergencyLightsSystem))]
public sealed partial class CMUEmergencyLightsComponent : Component
{
    /// <summary>The action the operator gets while they drive.</summary>
    [DataField]
    public EntProtoId Action = "CMUActionVehicleEmergencyLights";

    /// <summary>The lightbar for each mode after off: the first is mode 1, the second mode 2, and so on.</summary>
    [DataField]
    public List<EntProtoId> LightPrototypes = new()
    {
        "CMUEmergencyLightbarSlow",
        "CMUEmergencyLightbarMedium",
        "CMUEmergencyLightbarFast",
    };

    /// <summary>Where the lightbar sits, in the vehicle's frame.</summary>
    [DataField]
    public Vector2 Offset = new(0, 0.5f);

    /// <summary>0 is off; 1 to <see cref="LightPrototypes"/> count pick the lightbar.</summary>
    [DataField, AutoNetworkedField]
    public int Mode;

    /// <summary>The action held by the current operator; set by the server while someone drives.</summary>
    [NonSerialized]
    public EntityUid? DriverAction;

    /// <summary>The lightbar entity while the lights are on; server only.</summary>
    [NonSerialized]
    public EntityUid? Lightbar;
}

/// <summary>Raised on the operator when they use the emergency lights action.</summary>
public sealed partial class CMUEmergencyLightsActionEvent : InstantActionEvent;
