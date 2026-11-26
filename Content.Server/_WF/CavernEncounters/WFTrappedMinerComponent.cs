namespace Content.Server._WF.CavernEncounters;

/// <summary>A miner walled in by a rock collapse; digging out any rock of it frees him, and he pays and walks off.</summary>
[RegisterComponent, Access(typeof(WFTrappedMinerSystem), typeof(WFCavernEncounterSystem))]
public sealed partial class WFTrappedMinerComponent : Component
{
    /// <summary>The rocks walling him in.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Rocks = new();

    /// <summary>Whether he has been dug out.</summary>
    [ViewVariables]
    public bool Freed;

    /// <summary>Spesos he leaves for whoever dug him out.</summary>
    [DataField]
    public int Reward = 800;

    /// <summary>Tiles he walks off once free.</summary>
    [DataField]
    public float LeaveDistance = 25f;

    /// <summary>He is gone this long after being freed, once nobody is near.</summary>
    [DataField]
    public TimeSpan LeaveTime = TimeSpan.FromSeconds(90);

    /// <summary>When he may vanish, once freed.</summary>
    [ViewVariables]
    public TimeSpan? GoneAt;
}

/// <summary>One rock of a collapse; the miner it walls in goes free when it is dug out.</summary>
[RegisterComponent, Access(typeof(WFTrappedMinerSystem), typeof(WFCavernEncounterSystem))]
public sealed partial class WFCavernCollapseRockComponent : Component
{
    /// <summary>The miner behind it.</summary>
    [ViewVariables]
    public EntityUid? Miner;
}
