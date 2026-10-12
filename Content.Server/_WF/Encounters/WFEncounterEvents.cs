using Content.Shared._WF.Encounters;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Encounters;

/// <summary>Raised on the encounter entity, and broadcast, once its ships are crewed and have their orders.</summary>
[ByRefEvent]
public readonly record struct WFEncounterStartedEvent(EntityUid Encounter);

/// <summary>Raised on the encounter entity, and broadcast, when it resolves. Its ships may still exist.</summary>
[ByRefEvent]
public readonly record struct WFEncounterResolvedEvent(EntityUid Encounter, WFEncounterResolution Resolution);

/// <summary>Raised, by ref and broadcast, for each candidate when the storyteller picks; a subscriber may change the weight.</summary>
[ByRefEvent]
public record struct WFEncounterWeightEvent(WFEncounterPrototype Prototype, float Weight);

/// <summary>Raised, by ref and broadcast, with the stations of a map before they are used; a subscriber may remove entries.</summary>
[ByRefEvent]
public readonly record struct WFEncounterStationsEvent(MapId Map, List<Entity<MapGridComponent>> Stations);
