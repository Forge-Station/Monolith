using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Shuttles.Components;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Shipyard;

/// <summary>Checks every vessel grid, loaded into an initialised map as the shipyard spawns it.</summary>
// The name ends in ShipyardTest so the shipyard CI workflow's filter runs it when a grid changes.
[TestFixture]
public sealed class VesselShipyardTest
{
    /// <summary>Every vessel loads as a shuttle, every hardpoint-only gun on it mounts on a hardpoint, and the crew planner posts the pilot by a square helm.</summary>
    [Test]
    public async Task VesselsLoadAsShuttlesWithMountedGuns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var loader = entities.System<MapLoaderSystem>();
        var maps = entities.System<SharedMapSystem>();
        var planner = entities.System<WFCrewPlannerSystem>();
        var failures = new List<string>();
        var guns = 0;
        var helmShips = 0;

        await server.WaitPost(() =>
        {
            foreach (var vessel in prototypes.EnumeratePrototypes<VesselPrototype>())
            {
                maps.CreateMap(out var mapId);
                if (!loader.TryLoadGrid(mapId, vessel.ShuttlePath, out var grid))
                {
                    failures.Add($"{vessel.ID}: {vessel.ShuttlePath} did not load.");
                    maps.DeleteMap(mapId);
                    continue;
                }

                if (!entities.HasComponent<ShuttleComponent>(grid.Value))
                    failures.Add($"{vessel.ID}: the grid has no Shuttle component.");

                // Fire control refuses a gun that isn't mounted on a hardpoint.
                var query = entities.EntityQueryEnumerator<HardpointAnchorableOnlyComponent, TransformComponent>();
                while (query.MoveNext(out var gun, out var mount, out var xform))
                {
                    if (xform.GridUid != grid.Value.Owner)
                        continue;
                    guns++;
                    if (mount.anchoredTo == null)
                        failures.Add($"{vessel.ID}: {entities.ToPrettyString(gun)} at {xform.LocalPosition} has no compatible hardpoint on its tile.");
                }

                // The pilot is posted beside a console square to the hull when the grid has one with room beside it.
                var squares = new List<Vector2>();
                var others = new List<Vector2>();
                var consoles = entities.EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
                while (consoles.MoveNext(out _, out _, out var consoleXform))
                {
                    if (consoleXform.GridUid != grid.Value.Owner || !consoleXform.Anchored)
                        continue;

                    (WFPilotDutySystem.IsSquareHelm(consoleXform.LocalRotation) ? squares : others).Add(consoleXform.LocalPosition);
                }

                var posts = planner.Plan(grid.Value.Owner);
                // A hull whose consoles are all boxed in has no helm post; its pilot walks from the deck.
                if (squares.Count > 0 && !posts.Any(post => post.Kind == WFCrewPostKind.Marker)
                    && posts.FirstOrDefault(post => post.Kind == WFCrewPostKind.Helm) is { Kind: WFCrewPostKind.Helm } pilot)
                {
                    helmShips++;
                    if (!squares.Any(console => Vector2.Distance(console, pilot.Coordinates.Position) <= 1.1f)
                        && others.Any(console => Vector2.Distance(console, pilot.Coordinates.Position) <= 1.1f))
                        failures.Add($"{vessel.ID}: the pilot is posted beside a turned console, not the square one.");
                }

                maps.DeleteMap(mapId);
            }
        });

        Assert.That(guns, Is.Positive, "No hardpoint-only guns were found on any vessel.");
        Assert.That(helmShips, Is.Positive, "No vessel with a square console was planned.");
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        await pair.CleanReturnAsync();
    }
}
