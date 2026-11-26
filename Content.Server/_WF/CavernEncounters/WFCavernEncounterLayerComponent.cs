using Content.Shared._WF.CavernEncounters;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.CavernEncounters;

/// <summary>The encounter spots planned for one cavern map, and what became of each.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFCavernEncounterLayerComponent : Component
{
    /// <summary>Every spot, in the order they were planned.</summary>
    [ViewVariables]
    public List<WFCavernEncounterSite> Sites = new();
}

/// <summary>One planned encounter: where it was meant to go, where it went and what it spawned.</summary>
public sealed class WFCavernEncounterSite
{
    /// <summary>The encounter laid out here.</summary>
    [ViewVariables]
    public ProtoId<WFCavernEncounterPrototype> Encounter;

    /// <summary>The spot planned from the seed.</summary>
    [ViewVariables]
    public Vector2i Planned;

    /// <summary>The centre it was laid out on, which may be shifted off the planned spot to fit.</summary>
    [ViewVariables]
    public Vector2i? Centre;

    /// <summary>Whether it is waiting, laid out, or found no room.</summary>
    [ViewVariables]
    public WFCavernEncounterState State;

    /// <summary>Everything it spawned.</summary>
    [ViewVariables]
    public List<EntityUid> Spawned = new();
}

/// <summary>What became of a planned encounter.</summary>
public enum WFCavernEncounterState : byte
{
    /// <summary>Nobody has come near yet.</summary>
    Planned,

    /// <summary>Laid out.</summary>
    Spawned,

    /// <summary>No spot near the planned one was clear, so it never appears.</summary>
    Skipped,
}
