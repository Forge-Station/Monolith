#nullable enable
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WF.LivingWorld;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.LivingWorld;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Fluids.Components;
using Content.Shared.Movement.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Tools.Systems;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Planets.Biomes;

/// <summary>Carcinoma's plants do what their descriptions say: grab, mist, and give vitae, shards and fruit.</summary>
[TestFixture]
public sealed class CarcinomaPlantsTest
{
    /// <summary>A plating floor with the plant at the centre and a human at each distance along +x.</summary>
    private static async Task<(EntityUid Plant, EntityUid[] Mobs)> Rig(TestPair pair, string plant, params float[] distances)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        EntityUid plantUid = default;
        var mobs = new EntityUid[distances.Length];
        await server.WaitPost(() =>
        {
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, air);
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -8; x <= 8; x++)
            for (var y = -8; y <= 8; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            plantUid = entMan.SpawnEntity(plant, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            for (var i = 0; i < distances.Length; i++)
                mobs[i] = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f + distances[i], 0.5f));
        });
        return (plantUid, mobs);
    }

    [Test]
    public async Task TendonVineHoldsAnOutsiderForTwoSecondsAndSparesTheFlesh()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (vine, mobs) = await Rig(pair, "WFCarcinomaTendonVine", 1f, 1.2f, 5f);
        var (victim, flesh, far) = (mobs[0], mobs[1], mobs[2]);
        await server.WaitPost(() => server.System<NpcFactionSystem>().AddFaction(flesh, "AberrantFleshExpeditionNF"));
        // Only the outsider is near enough to be taken: put the flesh mob out of the nearest slot's way.
        await pair.RunSeconds(0.9f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFReactiveFloraComponent>(vine).HoldTime, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(entMan.HasComponent<WFFloraGrabbedComponent>(victim), Is.True, "The vine should take hold of an outsider.");
            Assert.That(entMan.HasComponent<WFFloraGrabbedComponent>(flesh), Is.False, "The flesh is spared.");
            Assert.That(entMan.HasComponent<WFFloraGrabbedComponent>(far), Is.False, "Out of range is left alone.");
            Assert.That(entMan.GetComponent<MovementSpeedModifierComponent>(victim).WalkSpeedModifier, Is.EqualTo(0f).Within(0.001f));
        });
        await pair.RunSeconds(2.5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFFloraGrabbedComponent>(victim), Is.False, "The hold ends after about two seconds.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WeepingPolypMistsAnOutsiderWithThinToxin()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (polyp, _) = await Rig(pair, "WFCarcinomaWeepingPolyp", 1f);
        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityQuery<SmokeComponent>().Any(), Is.True, "It mists an outsider.");
            var solution = entMan.GetComponent<WFReactiveFloraComponent>(polyp).Solution!;
            Assert.That(solution.GetTotalPrototypeQuantity("Blood").Float(), Is.GreaterThan(0), "The mist is blood.");
            Assert.That(solution.GetTotalPrototypeQuantity("Toxin").Float(), Is.InRange(0.1f, 3f), "The toxin is weak.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WeepingPolypStaysShutForTheFleshAndForDistance()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var (_, mobs) = await Rig(pair, "WFCarcinomaWeepingPolyp", 1f, 6f);
        await server.WaitPost(() => server.System<NpcFactionSystem>().AddFaction(mobs[0], "Chimera"));
        await pair.RunSeconds(1.5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.EntityQuery<SmokeComponent>().Any(), Is.False, "Flesh and far-off mobs don't set it off.");
        });
        await pair.CleanReturnAsync();
    }
    [Test]
    public async Task PickablesGiveTheirProduce()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var coords = new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f);
        EntityUid blister = default, pile = default, bush = default;
        await server.WaitPost(() =>
        {
            blister = entMan.SpawnEntity("WFCarcinomaVitaeBlister", coords);
            pile = entMan.SpawnEntity("WFCarcinomaBonePile", coords);
            bush = entMan.SpawnEntity("WFCarcinomaMeatFruitBush", coords);
        });

        int Count(string id) => entMan.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype?.ID == id);
        var harvest = server.System<WFRegrowingHarvestSystem>();
        await server.WaitAssertion(() =>
        {
            // A blister needs a blade; the others come away by hand.
            Assert.That(entMan.GetComponent<WFRegrowingHarvestComponent>(blister).Tool?.Id, Is.EqualTo("Slicing"));
            Assert.That(entMan.GetComponent<WFRegrowingHarvestComponent>(pile).Tool, Is.Null);
            Assert.That(entMan.GetComponent<WFRegrowingHarvestComponent>(bush).Tool, Is.Null);

            harvest.Harvest((blister, entMan.GetComponent<WFRegrowingHarvestComponent>(blister)), null);
            harvest.Harvest((pile, entMan.GetComponent<WFRegrowingHarvestComponent>(pile)), null);
            harvest.Harvest((bush, entMan.GetComponent<WFRegrowingHarvestComponent>(bush)), null);
            Assert.That(Count("WFVitaeBead"), Is.EqualTo(1));
            Assert.That(Count("WFBoneShard"), Is.InRange(1, 2));
            Assert.That(Count("WFMeatFruit"), Is.InRange(1, 2));

            var solutions = server.System<SharedSolutionContainerSystem>();
            Solution Food(string id)
            {
                var item = entMan.EntityQuery<MetaDataComponent>().First(meta => meta.EntityPrototype?.ID == id).Owner;
                Assert.That(solutions.TryGetSolution(item, "food", out _, out var solution), Is.True);
                return solution!;
            }

            // The bead is a weak dose of vitae; the fruit heals with a little mould in it.
            var vitae = Food("WFVitaeBead").GetTotalPrototypeQuantity("WFVitae").Float();
            Assert.That(vitae, Is.InRange(0.5f, 4f), "A bead is a fraction of the fountain's ten-unit dose.");
            var fruit = Food("WFMeatFruit");
            Assert.That(fruit.GetTotalPrototypeQuantity("Bicaridine").Float(), Is.GreaterThan(0));
            Assert.That(fruit.GetTotalPrototypeQuantity("Mold").Float(), Is.InRange(0.1f, 6f));
            Assert.That(fruit.GetTotalPrototypeQuantity("Toxin").Float(), Is.EqualTo(0f));

            // The shard cuts, so it can lance a blister, and hits weakly.
            var shard = entMan.EntityQuery<MetaDataComponent>().First(meta => meta.EntityPrototype?.ID == "WFBoneShard").Owner;
            Assert.That(server.System<SharedToolSystem>().HasQuality(shard, "Slicing"), Is.True);
            Assert.That(entMan.GetComponent<MeleeWeaponComponent>(shard).Damage.GetTotal().Float(), Is.InRange(1f, 10f));
        });
        await pair.CleanReturnAsync();
    }
}
