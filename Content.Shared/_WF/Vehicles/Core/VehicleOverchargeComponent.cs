using System;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>A burst of extra top speed the operator triggers from an action, followed by a cooldown.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VehicleOverchargeComponent : Component
{
    [DataField]
    public float SpeedMultiplier = 1.6f;

    [DataField]
    public float Duration = 3f;

    [DataField]
    public float Cooldown = 16f;

    [DataField]
    public SoundSpecifier? OverchargeSound;

    [AutoNetworkedField]
    public TimeSpan ActiveUntil;

    [AutoNetworkedField]
    public TimeSpan CooldownUntil;

    /// <summary>The overcharge action held by the current operator; set by the server while someone drives.</summary>
    [NonSerialized]
    public EntityUid? Action;
}
