using Content.Shared._WF.PlanetVents;
using Content.Shared.Atmos;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.PlanetVents;

/// <summary>
/// A vent that breathes: a slow inhale draws on its tile's air, then an exhale puffs warm gas back out. Only cycles
/// with a player nearby.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WFPlanetVentComponent : Component
{
    [DataField]
    public TimeSpan InhaleTime = TimeSpan.FromSeconds(4);

    [DataField]
    public TimeSpan ExhaleTime = TimeSpan.FromSeconds(1.5);

    /// <summary>The pause between breaths is picked between these.</summary>
    [DataField]
    public TimeSpan MinRest = TimeSpan.FromSeconds(6);

    [DataField]
    public TimeSpan MaxRest = TimeSpan.FromSeconds(14);

    /// <summary>The gas each exhale releases.</summary>
    [DataField]
    public Gas Gas = Gas.CarbonDioxide;

    [DataField]
    public float ExhaleMoles = 6f;

    /// <summary>Kelvin of the exhaled gas.</summary>
    [DataField]
    public float ExhaleTemperature = 318.15f;

    /// <summary>Moles each inhale draws from its tile.</summary>
    [DataField]
    public float InhaleMoles = 2f;

    /// <summary>A vent breathes only with a player within this many metres.</summary>
    [DataField]
    public float PlayerRange = 24f;

    /// <summary>Whether a player has to be near; off for tests.</summary>
    [DataField]
    public bool RequirePlayer = true;

    [DataField]
    public SoundSpecifier? InhaleSound;

    [DataField]
    public SoundSpecifier? ExhaleSound;

    /// <summary>The effect spawned on each exhale.</summary>
    [DataField]
    public EntProtoId? Puff;

    [ViewVariables]
    public WFPlanetVentPhase Phase = WFPlanetVentPhase.Rest;

    [ViewVariables, AutoPausedField]
    public TimeSpan NextPhase;
}
