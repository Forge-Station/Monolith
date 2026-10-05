using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server._Mono.ShipRepair;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.ShipAccess;
using Content.Server.Access.Systems;
using Content.Server.Administration.Logs;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WF.Administration.Systems;

/// <summary>
/// Spawns vessel prototypes straight into the world. Backs the Wolfgate admin tooling.
/// </summary>
public sealed partial class AdminVesselSpawnSystem : EntitySystem
{
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IdCardSystem _idCard = default!;
    [Dependency] private ShipyardSystem _shipyard = default!;
    [Dependency] private ShipRepairSystem _shipRepair = default!;
    [Dependency] private WFShipAccessServerSystem _shipAccess = default!;

    /// <summary>
    /// Loads the vessel's grid at a world position and applies the prototype's extra components.
    /// </summary>
    /// <param name="vessel">Vessel to spawn.</param>
    /// <param name="mapId">Map to load the grid into.</param>
    /// <param name="position">World position for the grid origin.</param>
    /// <param name="spawner">Entity credited in the admin log, if any.</param>
    /// <param name="gridUid">The spawned grid.</param>
    public bool TrySpawnVessel(VesselPrototype vessel, MapId mapId, Vector2 position, EntityUid? spawner, [NotNullWhen(true)] out EntityUid? gridUid)
    {
        gridUid = null;

        if (!_mapLoader.TryLoadGrid(mapId, vessel.ShuttlePath, out var grid, offset: position))
        {
            Log.Error($"Failed to load vessel grid {vessel.ShuttlePath} for {vessel.ID}");
            return false;
        }

        gridUid = grid.Value.Owner;

        // Same post-load steps the shipyard applies, minus deeds and ownership.
        EntityManager.AddComponents(gridUid.Value, vessel.AddComponents);
        _metaData.SetEntityName(gridUid.Value, vessel.Name);

        // This path raises no purchase event: the shipyard's repair snapshot has to be taken here too, or an SRD has
        // nothing to rebuild the vessel from.
        _shipRepair.GenerateRepairData(gridUid.Value);

        _adminLogger.Add(LogType.EntitySpawn, LogImpact.Medium,
            $"{ToPrettyString(spawner):player} spawned vessel {vessel.ID} as {ToPrettyString(gridUid.Value):grid} on map {mapId}");
        return true;
    }

    /// <summary>
    /// Finds the ID card a player would hold a deed on: one their living body carries. Fails when they are in the
    /// lobby or a ghost, carry no card, or it already holds a deed.
    /// </summary>
    /// <param name="owner">Player who should receive the deed.</param>
    /// <param name="idCard">Their ID card.</param>
    /// <param name="errorKey">Loc key describing why no card was found; takes a "name" argument.</param>
    public bool TryGetDeedCard(ICommonSession owner, [NotNullWhen(true)] out EntityUid? idCard, [NotNullWhen(false)] out string? errorKey)
    {
        idCard = null;
        errorKey = null;

        if (owner.AttachedEntity is not { Valid: true } ownerEntity)
        {
            errorKey = "cmd-spawnvessel-owner-no-entity";
            return false;
        }

        // A ghost's card goes when the ghost does, and the deed with it.
        if (HasComp<GhostComponent>(ownerEntity))
        {
            errorKey = "cmd-spawnvessel-owner-ghost";
            return false;
        }

        if (!_idCard.TryFindIdCard(ownerEntity, out var card))
        {
            errorKey = "cmd-spawnvessel-owner-no-id";
            return false;
        }

        // The shipyard enforces one ship per captain; keep that rule so selling still works.
        if (HasComp<ShuttleDeedComponent>(card))
        {
            errorKey = "cmd-spawnvessel-owner-has-deed";
            return false;
        }

        idCard = card;
        return true;
    }

    /// <summary>
    /// Registers the spawned vessel to a player's account, so its access stays theirs in any body. With a deed card
    /// it is also registered like a shipyard purchase: deed, console locks, records. Without one it gets ship access only.
    /// </summary>
    public bool TryAssignOwner(EntityUid gridUid, VesselPrototype vessel, EntityUid? idCard, ICommonSession owner)
    {
        if (idCard is not { } card)
        {
            var name = owner.AttachedEntity is { Valid: true } body && !HasComp<GhostComponent>(body) ? Name(body) : owner.Name;
            _shipAccess.SetupRegisteredShip(gridUid, name, new[] { owner.UserId });
            _adminLogger.Add(LogType.EntitySpawn, LogImpact.Medium,
                $"{ToPrettyString(gridUid):grid} registered to {owner.Name} without a deed card");
            return true;
        }

        if (!_shipyard.TryAssignDeed(gridUid, card, owner, vessel))
            return false;

        _shipAccess.AddOwnerUser(gridUid, owner.UserId);
        _adminLogger.Add(LogType.EntitySpawn, LogImpact.Medium,
            $"Deed for {ToPrettyString(gridUid):grid} assigned to {owner.Name} on {ToPrettyString(card):card}");
        return true;
    }
}
