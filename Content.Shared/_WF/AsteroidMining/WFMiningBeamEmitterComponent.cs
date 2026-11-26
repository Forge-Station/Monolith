using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.AsteroidMining;

/// <summary>
/// A ship-mounted mining laser gun. Aimed and fired from a gunnery console; each pulse that hits an asteroid keeps it
/// heating at the power set in the gunnery console's mining laser panel.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFMiningBeamEmitterComponent : Component
{
    /// <summary>Heat delivered at full power, before the rock's resistance.</summary>
    [DataField] public float Strength = 1.25f;

    /// <summary>How fast the beam cracks a rock held in its window; the beams on one rock add up.</summary>
    [DataField] public float CrackRate = 1f;

    /// <summary>Scales the drawn beam's width and impact.</summary>
    [DataField] public float BeamWidth = 1f;

    /// <summary>Seconds the gun can't fire after its target blows up.</summary>
    [DataField] public float OverloadCooldown = 10f;

    /// <summary>The beam stays lit, and keeps heating its rock, this long after the last pulse.</summary>
    [DataField] public float ActiveTime = 0.5f;

    /// <summary>Where the beam leaves the barrel, in the gun's local frame.</summary>
    [DataField] public Vector2 MuzzleOffset = new(0f, -1.31f);

    [DataField] public Color BeamColor = Color.FromHex("#ffb347");

    /// <summary>The hum each client loops while the beam is lit; its pitch climbs with the rock's heat.</summary>
    [DataField] public SoundSpecifier? LoopSound = new SoundPathSpecifier("/Audio/_WF/AsteroidMining/mining_laser_heavy.ogg",
        AudioParams.Default.WithVolume(-4f).WithLoop(true).WithMaxDistance(24f));

    /// <summary>Pitch of the hum's hot copy, which takes over as the rock nears the overcharge line.</summary>
    [DataField] public float HeatPitch = 1.2f;

    /// <summary>The gun is firing along <see cref="BeamDirection"/>, to what its last pulse hit <see cref="BeamLength"/> out.</summary>
    [AutoNetworkedField] public bool Active;
    /// <summary>World direction of the last pulse, which fire control aims at the gunner's cursor.</summary>
    [AutoNetworkedField] public Vector2 BeamDirection = new(0f, -1f);
    /// <summary>Distance from the gun's centre to where the last pulse hit.</summary>
    [AutoNetworkedField] public float BeamLength;
    /// <summary>The rock under the beam is inside its cracking window.</summary>
    [AutoNetworkedField] public bool InWindow;
    /// <summary>The rock under the beam is past its window, cracking fast but losing ore.</summary>
    [AutoNetworkedField] public bool Hot;

    /// <summary>Power setting from zero to one, set from the gunnery console.</summary>
    [AutoNetworkedField] public float Power = 0.5f;

    /// <summary>Target heat for the beam's colour and impact glow.</summary>
    [AutoNetworkedField] public float Charge;

    // Runtime state, never saved into maps.
    public EntityUid? Target;
    /// <summary>Client-side: the hum playing for this beam, and its higher-pitched copy for heat.</summary>
    public EntityUid? Audio;
    public EntityUid? AudioHot;
    public TimeSpan CooldownUntil;
    public TimeSpan LastPulse;
}

[Serializable, NetSerializable]
public enum WFMiningBeamVisualLayers : byte
{
    Base,
}
