using Content.Shared.Atmos;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.Showers;

/// <summary>
/// A shower head that sprays its tank over everything on its tile: a touch reaction for every reagent in it,
/// plus heat, stamina and a scrub of forensics, footprints and floor stains. Ported from tgstation via Meridian Rift.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WFShowerComponent : Component
{
    [DataField]
    public string Solution = "tank";

    /// <summary>Whether the user wants it running.</summary>
    [DataField]
    public bool IntendedOn;

    /// <summary>Whether it is spraying right now.</summary>
    [DataField]
    public bool ActuallyOn;

    [DataField]
    public WFShowerTemperature Temperature = WFShowerTemperature.Normal;

    [DataField]
    public WFShowerMode Mode = WFShowerMode.UntilEmpty;

    /// <summary>Refills the tank with water while it is off.</summary>
    [DataField]
    public bool Reclaimer = true;

    [DataField]
    public string RefillReagent = "Water";

    /// <summary>Units refilled per second while off.</summary>
    [DataField]
    public FixedPoint2 RefillRate = 0.5f;

    /// <summary>Units sprayed per tick.</summary>
    [DataField]
    public FixedPoint2 SprayVolume = 5;

    /// <summary>How much more than the sprayed volume each target is exposed to.</summary>
    [DataField]
    public float ExposureMultiplier = 2f;

    [DataField]
    public TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextTick;

    [DataField]
    public TimeSpan TimedLength = TimeSpan.FromSeconds(15);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan TimedEnd;

    /// <summary>Share of the tank that has to be water for the soothing effects and the scrub.</summary>
    [DataField]
    public float CleanFraction = 0.7f;

    /// <summary>Stamina damage removed per tick from someone under a clean, non-scalding shower.</summary>
    [DataField]
    public float StaminaRecovery = 4f;

    /// <summary>Share of the gap to the water temperature a body closes per tick.</summary>
    [DataField]
    public float HeatTransfer = 0.15f;

    [DataField]
    public float NormalTemperature = Atmospherics.T0C + Atmospherics.NormalBodyTemperature;

    [DataField]
    public float FreezingTemperature = 250f;

    [DataField]
    public float BoilingTemperature = 345f;

    /// <summary>Heat damage per tick from a scalding shower.</summary>
    [DataField]
    public float ScaldDamage = 2f;

    [DataField]
    public TimeSpan TemperatureAdjustDelay = TimeSpan.FromSeconds(5);

    [DataField]
    public TimeSpan MistDelay = TimeSpan.FromSeconds(5);

    [DataField]
    public TimeSpan MistClearDelay = TimeSpan.FromSeconds(25);

    [DataField]
    public bool Mist;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan? MistChangeAt;

    /// <summary>Volume left on the floor when it shuts off.</summary>
    [DataField]
    public FixedPoint2 RunoffVolume = 5;

    [DataField]
    public SoundSpecifier StartSound = new SoundPathSpecifier("/Audio/_WF/Showers/shower_start.ogg");

    [DataField]
    public SoundSpecifier EndSound = new SoundPathSpecifier("/Audio/_WF/Showers/shower_end.ogg");

    [DataField]
    public SoundSpecifier AdjustSound = new SoundPathSpecifier("/Audio/Items/screwdriver.ogg");

    /// <summary>Mobs already told how the water feels, so the popup fires once per visit.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Showering = new();
}

[Serializable, NetSerializable]
public enum WFShowerTemperature : byte
{
    Normal,
    Freezing,
    Boiling,
}

[Serializable, NetSerializable]
public enum WFShowerMode : byte
{
    /// <summary>Runs until the tank is dry.</summary>
    UntilEmpty,

    /// <summary>Runs for a set time or until the tank is dry.</summary>
    Timed,

    /// <summary>Pauses when dry and starts again once refilled.</summary>
    Forever,
}

[Serializable, NetSerializable]
public enum WFShowerVisuals : byte
{
    On,
    Mist,
    Color,
}

[Serializable, NetSerializable]
public enum WFShowerVisualLayers : byte
{
    Water,
    Mist,
}

[Serializable, NetSerializable]
public sealed partial class WFShowerTemperatureDoAfterEvent : SimpleDoAfterEvent;
