using Robust.Shared.Audio;

namespace Content.Server._WF.Remade.Roster;

/// <summary>An exploder: a pustule limb that bursts when shot, and a blast it can set off itself.</summary>
[RegisterComponent]
public sealed partial class WFRemadeExploderComponent : Component
{
    /// <summary>The limb that holds the blast. Without it the mob can't explode.</summary>
    [DataField]
    public string Pustule = "l_arm";

    [DataField]
    public string ExplosionType = "Default";

    [DataField]
    public float Intensity = 80f;

    [DataField]
    public float Slope = 4f;

    [DataField]
    public float MaxIntensity = 12f;

    /// <summary>A hit from this far off or further is a shot, which bursts the pustule. A nearer one cuts it off.</summary>
    [DataField]
    public float ShotDistance = 2.5f;

    [DataField]
    public SoundSpecifier? BurstSound;

    [ViewVariables]
    public bool Detonated;

    /// <summary>When the last hit landed and whether it came from range, so a severed pustule can tell shot from cut.</summary>
    [ViewVariables]
    public TimeSpan LastHitAt;

    [ViewVariables]
    public bool LastHitShot;
}
