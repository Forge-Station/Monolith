namespace Content.Server._WF.Cowardice;

/// <summary>What a frightened NPC is doing.</summary>
public enum WFCowardState : byte
{
    None,

    /// <summary>Down on the ground where he stands, unable to move or fight.</summary>
    Cowering,

    /// <summary>Running from whoever scared him, unable to fight.</summary>
    Fleeing,
}

/// <summary>
/// A civilian who loses his nerve: when someone points a gun at him, or has hit him enough times, he may cower on the
/// spot or run, and fights nobody until he has calmed down. Never on a combat NPC.
/// </summary>
[RegisterComponent]
public sealed partial class WFCowardiceComponent : Component
{
    /// <summary>The chance, each time he is threatened, that he breaks.</summary>
    [DataField]
    public float Cowardice = 0.6f;

    /// <summary>Of the times he breaks, the share he runs rather than cowers.</summary>
    [DataField]
    public float Flee = 0.4f;

    /// <summary>Hits within <see cref="HitWindow"/> that count as a threat.</summary>
    [DataField]
    public int Hits = 3;

    [DataField]
    public TimeSpan HitWindow = TimeSpan.FromSeconds(20);

    /// <summary>How far off he notices a drawn gun.</summary>
    [DataField]
    public float GunRange = 8f;

    /// <summary>Seconds he stays frightened, from the least to the most.</summary>
    [DataField]
    public float MinSeconds = 6f;

    [DataField]
    public float MaxSeconds = 14f;

    /// <summary>Seconds after calming down before he can break again.</summary>
    [DataField]
    public float RestSeconds = 20f;

    /// <summary>The chance each thing in his hands is dropped as he breaks.</summary>
    [DataField]
    public float DropChance = 0.5f;

    [ViewVariables]
    public WFCowardState State;

    [ViewVariables]
    public TimeSpan Until;

    [ViewVariables]
    public TimeSpan RestUntil;

    [ViewVariables]
    public TimeSpan NextThink;

    [ViewVariables]
    public EntityUid? Threat;

    /// <summary>When each recent hit landed.</summary>
    [ViewVariables]
    public List<TimeSpan> HitTimes = new();

    /// <summary>Gunmen he has already weighed up, until when; he does not roll again for the same one at once.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Weighed = new();
}
