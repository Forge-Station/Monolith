using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Spawners.Components;
using Content.Shared.Station.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.Spawners.EntitySystems;

public sealed partial class SpawnPointSystem : EntitySystem
{
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private StationSystem _stationSystem = default!;
    [Dependency] private StationSpawningSystem _stationSpawning = default!;

    [SubscribeLocalEvent]
    private void OnPlayerSpawning(PlayerSpawningEvent args)
    {
        if (args.SpawnResult != null)
            return;

        // Unset follows the round: job markers before the round starts, late-join markers after.
        // alwaysUseSpawner jobs pass SpawnPointType.Job even mid-round so they keep their own marker.
        var spawnType = args.DesiredSpawnPointType;
        if (spawnType == SpawnPointType.Unset)
        {
            spawnType = _gameTicker.RunLevel == GameRunLevel.InRound
                ? SpawnPointType.LateJoin
                : SpawnPointType.Job;
        }

        // TODO: Cache all this if it ends up important.
        var possiblePositions = FindSpawnPoints(args.Station, spawnType, args.Job, restrictToStation: true);

        // Mid-round used to ignore job markers. A role whose outpost has no late-join marker
        // (Flunky on the renegade fortress) then took the first spawn point in the world,
        // which is the TSF ambassador room on Central Outpost.
        if (possiblePositions.Count == 0 && args.Job != null)
            possiblePositions = FindSpawnPoints(null, SpawnPointType.Job, args.Job, restrictToStation: false, exactJob: true);

        if (possiblePositions.Count == 0 && spawnType != SpawnPointType.LateJoin && args.Station != null)
            possiblePositions = FindSpawnPoints(args.Station, SpawnPointType.LateJoin, job: null, restrictToStation: true);

        if (possiblePositions.Count == 0)
        {
            Log.Error($"No spawn points were available!\nRunLevel: {_gameTicker.RunLevel} Station: {ToPrettyString(args.Station)} Job: {args.Job} Desired: {args.DesiredSpawnPointType}");
            return;
        }

        var spawnLoc = _random.Pick(possiblePositions);

        args.SpawnResult = _stationSpawning.SpawnPlayerMob(
            spawnLoc,
            args.Job,
            args.HumanoidCharacterProfile,
            args.Station,
            session: args.Session);
    }

    /// <summary>
    /// <paramref name="exactJob"/> ignores markers that do not name this job.
    /// A marker with no job is only a wildcard when searching inside one station.
    /// </summary>
    private List<EntityCoordinates> FindSpawnPoints(
        EntityUid? station,
        SpawnPointType spawnType,
        ProtoId<JobPrototype>? job,
        bool restrictToStation,
        bool exactJob = false)
    {
        var possible = new List<EntityCoordinates>();
        var points = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();

        while (points.MoveNext(out var uid, out var spawnPoint, out var xform))
        {
            if (restrictToStation && station != null && _stationSystem.GetOwningStation(uid, xform) != station)
                continue;

            if (spawnPoint.SpawnType != spawnType)
                continue;

            if (spawnType == SpawnPointType.Job && !JobMatches(spawnPoint, job, exactJob))
                continue;

            possible.Add(xform.Coordinates);
        }

        return possible;
    }

    private static bool JobMatches(SpawnPointComponent spawnPoint, ProtoId<JobPrototype>? job, bool exactJob)
    {
        if (job == null)
            return true;

        if (spawnPoint.Job == null)
            return !exactJob;

        return spawnPoint.Job == job;
    }
}
