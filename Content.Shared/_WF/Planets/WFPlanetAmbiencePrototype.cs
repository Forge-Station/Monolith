using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Planets;

/// <summary>Client-played environmental audio for one kind of planet.</summary>
[Prototype("wfPlanetAmbience")]
public sealed partial class WFPlanetAmbiencePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The surface type this soundscape belongs to; none for one played only where something names it, such as a cavern.</summary>
    [DataField]
    public ProtoId<PlanetTypePrototype>? PlanetType;

    /// <summary>Numbered all-day beds, played in order with crossfades.</summary>
    [DataField] public List<SoundSpecifier> Loops = new();
    [DataField] public List<SoundSpecifier> DayLoops = new();
    [DataField] public List<SoundSpecifier> NightLoops = new();
    [DataField] public float CrossfadeSeconds = 4f;

    public List<SoundSpecifier> GetLoops(bool night)
    {
        var specific = night ? NightLoops : DayLoops;
        return specific.Count > 0 ? specific : Loops;
    }

    /// <summary>Environmental accents selected at random. These are never allowed to overlap each other.</summary>
    [DataField]
    public List<SoundSpecifier> OneShots = new();
    [DataField] public List<SoundSpecifier> DayOneShots = new();
    [DataField] public List<SoundSpecifier> NightOneShots = new();

    public List<SoundSpecifier> GetOneShots(bool night)
    {
        var specific = night ? NightOneShots : DayOneShots;
        return specific.Count > 0 ? specific : OneShots;
    }

    [DataField]
    public float LoopVolume = -18f;

    [DataField]
    public float OneShotVolume = -12f;

    /// <summary>Extra attenuation when the listener stands on a hull grid instead of exposed terrain.</summary>
    [DataField]
    public float HullVolumeOffset = -9f;

    [DataField]
    public float MinInterval = 35f;

    [DataField]
    public float MaxInterval = 90f;
}

/// <summary>Identifies the world's ambience and its attenuation at this z-level.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WFPlanetAmbienceComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public ProtoId<WFPlanetAmbiencePrototype> Profile;

    /// <summary>Decibel offset applied to both the bed and accents at this layer.</summary>
    [DataField, AutoNetworkedField]
    public float VolumeOffset;

    /// <summary>Audio occlusion on the bed and accents at this layer, a low-pass that muffles them; 0 is clear.</summary>
    [DataField, AutoNetworkedField]
    public float Occlusion;
}

/// <summary>Shared constants and pure calculations used by network construction and tests.</summary>
public static class WFPlanetAmbience
{
    public const float SilentVolume = -60f;
    public const float HighestAirVolumeOffset = -24f;

    /// <summary>Volume offset for a layer: full at ground, fading linearly with depth, silent in orbit.</summary>
    public static float LayerVolumeOffset(int depth, int orbitDepth)
    {
        if (depth <= 0)
            return 0f;

        if (orbitDepth <= 0 || depth >= orbitDepth)
            return SilentVolume;

        return HighestAirVolumeOffset * depth / orbitDepth;
    }
}
