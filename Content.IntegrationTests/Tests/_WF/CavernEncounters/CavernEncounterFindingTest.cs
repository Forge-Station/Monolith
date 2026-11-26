#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.CavernEncounters;
using Content.Server.Light.EntitySystems;
using Content.Shared.Light.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Client.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.CavernEncounters;

/// <summary>
/// Finding cavern encounters: their telegraph sounds play while a player is in earshot and not otherwise, the
/// Lamplighter's omen lanterns die as a player comes near, and the cavernencounters command lists and teleports.
/// </summary>
[TestFixture]
[TestOf(typeof(WFCavernTelegraphSystem))]
public sealed class CavernEncounterFindingTest
{
    /// <summary>A crawler clicks when a player is within its range, keeps quiet when none is, and stays quiet dead.</summary>
    [Test]
    public async Task TelegraphPlaysOnlyInEarshot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var xforms = server.System<SharedTransformSystem>();

        EntityUid crawler = default, player = default;
        await server.WaitPost(() =>
        {
            crawler = entMan.SpawnEntity("WFMobBlindCrawler", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            player = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 40.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, player);
        });
        await pair.RunTicksSync(pair.SecondsToTicks(1));

        var range = 0f;
        await server.WaitAssertion(() =>
        {
            var telegraph = entMan.GetComponent<WFCavernTelegraphComponent>(crawler);
            range = telegraph.Range;
            Assert.That(range, Is.InRange(16f, 24f), "The crawler can't be heard from about 20 tiles.");
            Assert.That(telegraph.LastPlayed, Is.Null, "The crawler clicked with nobody in earshot.");
            xforms.SetCoordinates(player, new EntityCoordinates(map.Grid.Owner, range - 2f, 0.5f));
        });
        await pair.RunTicksSync(pair.SecondsToTicks(1));

        await server.WaitAssertion(() =>
        {
            var telegraph = entMan.GetComponent<WFCavernTelegraphComponent>(crawler);
            Assert.That(telegraph.LastPlayed, Is.Not.Null, "The crawler didn't click for a player in earshot.");

            telegraph.LastPlayed = null;
            telegraph.NextPlay = TimeSpan.Zero;
            server.System<MobStateSystem>().ChangeMobState(crawler, MobState.Dead);
            Assert.That(server.System<MobStateSystem>().IsAlive(crawler), Is.False, "Precondition: the crawler is dead.");
        });
        await pair.RunTicksSync(pair.SecondsToTicks(1));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFCavernTelegraphComponent>(crawler).LastPlayed, Is.Null, "A dead crawler clicked."));

        await pair.CleanReturnAsync();
    }

    /// <summary>An omen lantern burns until a player comes near, then dies; the Lamplighter beside it neither douses it nor freezes in it.</summary>
    [Test]
    public async Task OmenLanternDiesAsYouCome()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var xforms = server.System<SharedTransformSystem>();
        var handheld = server.System<HandheldLightSystem>();

        EntityUid lantern = default, lamplighter = default, player = default;
        var trigger = 0f;
        await server.WaitPost(() =>
        {
            lantern = entMan.SpawnEntity("WFCavernOmenLantern", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            lamplighter = entMan.SpawnEntity("WFMobLamplighter", new EntityCoordinates(map.Grid.Owner, 1.5f, 0.5f));
            entMan.GetComponent<WFLamplighterComponent>(lamplighter).NextDouse = TimeSpan.Zero;
            Assert.That(handheld.TurnOn(lantern, (lantern, entMan.GetComponent<HandheldLightComponent>(lantern))), Is.True, "The omen lantern won't light.");

            trigger = entMan.GetComponent<WFCavernOmenLightComponent>(lantern).TriggerRange;
            player = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, trigger + 10f, 0.5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, player);
        });
        await pair.RunTicksSync(pair.SecondsToTicks(3));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<HandheldLightComponent>(lantern).Activated, Is.True, "The omen died with nobody near, or the Lamplighter doused it.");
            Assert.That(entMan.GetComponent<WFLamplighterComponent>(lamplighter).Lit, Is.False, "The Lamplighter froze in its own omen's light.");
            xforms.SetCoordinates(player, new EntityCoordinates(map.Grid.Owner, trigger - 2f, 0.5f));
        });
        await pair.RunTicksSync(pair.SecondsToTicks(3));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<HandheldLightComponent>(lantern).Activated, Is.False, "The omen lantern didn't die as the player came near."));

        await pair.CleanReturnAsync();
    }

    /// <summary>cavernencounters lists every planned encounter of a world with its number, and goto lays it out and moves the caller there.</summary>
    [Test]
    public async Task CommandListsAndTeleports()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var system = server.System<WFCavernEncounterSystem>();
        var console = pair.Client.ResolveDependency<IClientConsoleHost>();
        var output = new List<string>();

        EventHandler<AddStringArgs> capture = (_, args) =>
        {
            if (!args.Local)
                output.Add(args.Text.ToString());
        };

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, "WFSurfaceAerumna");
        await pair.Client.WaitPost(() => console.AddString += capture);

        try
        {
            var viewer = await PlanetFixture.AttachViewer(pair, world.Ground, Vector2.Zero);

            var sites = 0;
            await server.WaitPost(() => sites = system.GetSites(world.Cavern).Count);
            Assert.That(sites, Is.GreaterThan(0), "Precondition: Aerumna planned no encounters.");

            var list = await Run(pair, output, "Aerumna");
            Assert.That(list.Count(line => line.StartsWith("#")), Is.EqualTo(sites), $"list should print one row per encounter:\n{string.Join('\n', list)}");
            Assert.That(list.Any(line => line.Contains($"{sites} encounters")), Is.True, $"list has no world header:\n{string.Join('\n', list)}");

            var unknown = await Run(pair, output, "Nowhere");
            Assert.That(unknown.Any(line => line.Contains("Nowhere")), Is.True, "An unknown world wasn't reported.");

            // Number 1 is the first of the only world, planned near the gate.
            await Run(pair, output, "goto 1");
            await server.WaitAssertion(() =>
            {
                var site = system.GetSites(world.Cavern)[0];
                var xform = entMan.GetComponent<TransformComponent>(viewer);
                Assert.That(site.State, Is.Not.EqualTo(WFCavernEncounterState.Planned), "goto didn't lay the encounter out.");
                Assert.That(xform.MapUid, Is.EqualTo(world.Cavern), "goto didn't move the caller into the cavern.");

                var centre = site.Centre ?? site.Planned;
                var position = server.System<SharedTransformSystem>().GetWorldPosition(xform);
                Assert.That(Vector2.Distance(position, new Vector2(centre.X + 0.5f, centre.Y + 0.5f)), Is.LessThanOrEqualTo(8f),
                    "goto left the caller away from the encounter.");
            });

        }
        finally
        {
            await pair.Client.WaitPost(() => console.AddString -= capture);
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Runs cavernencounters as the test player and returns what the server wrote back.</summary>
    private static async Task<List<string>> Run(TestPair pair, List<string> output, string args)
    {
        await pair.Client.WaitPost(output.Clear);
        await pair.Server.WaitPost(() =>
            pair.Server.ConsoleHost.ExecuteCommand(pair.Player, $"cavernencounters {args}"));
        await pair.RunTicksSync(5);

        var lines = new List<string>();
        await pair.Client.WaitPost(() => lines.AddRange(output));
        return lines;
    }
}
