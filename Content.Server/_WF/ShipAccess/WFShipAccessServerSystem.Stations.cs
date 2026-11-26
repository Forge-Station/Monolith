using Content.Server.StationRecords;
using Robust.Server.GameStates;

namespace Content.Server._WF.ShipAccess;

public sealed partial class WFShipAccessServerSystem
{
    [Dependency] private PvsOverrideSystem _pvsOverride = default!;

    /// <summary>Every holder of crew records reaches every client, so the record keys that name it resolve there too.</summary>
    private void InitializeStations()
    {
        SubscribeLocalEvent<StationRecordsComponent, ComponentStartup>(OnRecordsStartup);
    }

    /// <summary>
    /// A record key names the entity that holds the record: a station, or on Frontier the sector records service,
    /// which is where a spawning player's card is keyed. Both live off the map, where PVS never sends them, and
    /// without one the client drops a door's keys and voids its own card's, so every keyed door is predicted as a
    /// deny the server then overrules. Force-sent from the start, the holder reaches a client no later than any
    /// card keyed to it.
    /// </summary>
    private void OnRecordsStartup(Entity<StationRecordsComponent> ent, ref ComponentStartup args)
    {
        _pvsOverride.AddForceSend(ent);
    }
}
