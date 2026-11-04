#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WF.LivingWorld;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Shared._Shitmed.Body.Components;
using Content.Shared._WF.LivingWorld;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.EntityTable;
using Content.Shared.FixedPoint;
using Content.Shared.Kitchen;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Tools.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Planets.Biomes;

/// <summary>Thrascias's wildlife spawns and survives its air, and its plants harvest, lash and burst as described.</summary>
[TestFixture]
public sealed class ThrasciasLifeTest
{
    private static readonly string[] Mobs =
    {
        "WFMobThrasciasPenguin", "WFMobThrasciasSnowFox", "WFMobThrasciasFrostBear", "WFMobThrasciasYeti",
        "WFMobThrasciasCrystalCrab", "WFMobThrasciasSnowSpider",
    };

    private static readonly string[] Tables =
    {
        "WFFaunaThrasciasTundra", "WFFaunaThrasciasIce", "WFFaunaThrasciasCrystal", "WFFaunaThrasciasBoreal", "WFFaunaThrascias",
    };

    /// <summary>A plating floor under Thrascias's air: no oxygen, far below freezing.</summary>
    private static async Task<(MapGridComponent Grid, EntityUid GridUid)> Rig(TestPair pair, bool thrasciasAir)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        await server.WaitPost(() =>
        {
            var air = new GasMixture(Atmospherics.CellVolume);
            if (thrasciasAir)
            {
                air.Temperature = 180f;
                air.AdjustMoles(Gas.Nitrogen, 100f);
            }
            else
            {
                air.Temperature = Atmospherics.T20C;
                air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
                air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            }
            server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, air);
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -8; x <= 8; x++)
            for (var y = -8; y <= 8; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
        });
        return (map.Grid.Comp, map.Grid.Owner);
    }

    private static FixedPoint2 Damage(TestPair pair, EntityUid uid, string type)
    {
        return pair.Server.EntMan.GetComponent<DamageableComponent>(uid).Damage.DamageDict.GetValueOrDefault(type);
    }

    [Test]
    public async Task EveryMobSpawnsAndEveryTableEntryResolves()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (_, grid) = await Rig(pair, false);
        await server.WaitAssertion(() =>
        {
            foreach (var id in Mobs)
            {
                var mob = entMan.SpawnEntity(id, new EntityCoordinates(grid, 0.5f, 0.5f));
                Assert.That(entMan.HasComponent<MobStateComponent>(mob), Is.True, $"{id} is not a mob.");
                entMan.DeleteEntity(mob);
            }

            var factions = server.System<NpcFactionSystem>();
            var bear = entMan.SpawnEntity("WFMobThrasciasFrostBear", new EntityCoordinates(grid, 0.5f, 0.5f));
            var yeti = entMan.SpawnEntity("WFMobThrasciasYeti", new EntityCoordinates(grid, 0.5f, 0.5f));
            Assert.That(factions.IsMember(bear, "SimpleHostile"), Is.True, "Frost bears hunt.");
            Assert.That(factions.IsMember(yeti, "Passive"), Is.True, "The yeti only fights back.");

            var tables = server.System<EntityTableSystem>();
            var proto = server.ProtoMan;
            var seen = new HashSet<string>();
            foreach (var table in Tables)
            {
                var spawns = new HashSet<string>();
                for (var i = 0; i < 300; i++)
                    foreach (var spawn in tables.GetSpawns(proto.Index<EntityTablePrototype>(table).Table))
                        spawns.Add(spawn.Id);
                Assert.That(spawns, Is.Not.Empty, $"{table} rolled nothing.");
                foreach (var spawn in spawns)
                {
                    Assert.That(proto.HasIndex<EntityPrototype>(spawn), Is.True, $"{table} names a missing {spawn}.");
                    seen.Add(spawn);
                }
            }
            foreach (var id in Mobs)
                Assert.That(seen, Does.Contain(id), $"{id} is in no spawner table.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AirBreathingNativesSurviveTheColdAirlessSurface()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (_, grid) = await Rig(pair, true);
        var natives = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            foreach (var id in new[] { "WFMobThrasciasPenguin", "WFMobThrasciasSnowFox", "WFMobThrasciasYeti" })
                natives.Add(entMan.SpawnEntity(id, new EntityCoordinates(grid, 0.5f, 0.5f)));
        });
        await pair.RunSeconds(40);

        await server.WaitAssertion(() =>
        {
            foreach (var mob in natives)
            {
                Assert.That(entMan.HasComponent<BreathingImmunityComponent>(mob), Is.True, $"{entMan.ToPrettyString(mob)} would suffocate.");
                Assert.That(Damage(pair, mob, "Asphyxiation"), Is.EqualTo(FixedPoint2.Zero), $"{entMan.ToPrettyString(mob)} suffocated.");
                Assert.That(Damage(pair, mob, "Cold"), Is.EqualTo(FixedPoint2.Zero), $"{entMan.ToPrettyString(mob)} froze.");
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PlantsYieldTheirLootAndIceCrystalsNeedAShovel()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (_, grid) = await Rig(pair, false);
        var harvest = server.System<WFRegrowingHarvestSystem>();
        var tools = server.System<SharedToolSystem>();
        await server.WaitAssertion(() =>
        {
            int Count(string id) => entMan.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype?.ID == id);
            void Pick(string plant, string loot)
            {
                var before = Count(loot);
                var uid = entMan.SpawnEntity(plant, new EntityCoordinates(grid, 0.5f, 0.5f));
                harvest.Harvest((uid, entMan.GetComponent<WFRegrowingHarvestComponent>(uid)), null);
                Assert.That(Count(loot), Is.GreaterThan(before), $"{plant} gave no {loot}.");
                Assert.That(entMan.GetComponent<WFRegrowingHarvestComponent>(uid).Harvested, Is.True);
            }

            Pick("WFFloraFrostbelle", "WFFrostbelleBloom");
            Pick("WFFloraIceCrystal", "ShardCrystalCyan");
            Pick("WFFloraCrystalBloom", "ShardCrystalCyan");
            Pick("WFFloraFrozenFungus", "WFFrozenFungus");
            Pick("WFFloraWhipvine", "WFPlantFibre");

            var crystal = entMan.SpawnEntity("WFFloraIceCrystal", new EntityCoordinates(grid, 2.5f, 0.5f));
            var needs = entMan.GetComponent<WFRegrowingHarvestComponent>(crystal).Tool;
            Assert.That(needs?.Id, Is.EqualTo("Digging"), "Ice crystals are chipped with a digging tool.");
            var shovel = entMan.SpawnEntity("Shovel", new EntityCoordinates(grid, 2.5f, 0.5f));
            Assert.That(tools.HasQuality(shovel, needs!.Value.Id), Is.True, "A shovel should be able to chip an ice crystal.");
            Assert.That(entMan.GetComponent<WFRegrowingHarvestComponent>(crystal).ToolTags.Select(tag => tag.Id), Does.Contain("Pickaxe"),
                "A pickaxe should chip an ice crystal too.");
            Assert.That(entMan.HasComponent<WFRegrowingHarvestComponent>(
                entMan.SpawnEntity("WFFloraIceCrystal", new EntityCoordinates(grid, 3.5f, 0.5f))), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ProduceCarriesItsReagentsAndFungusCooksIntoSoup()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (_, grid) = await Rig(pair, false);
        await server.WaitAssertion(() =>
        {
            var solutions = server.System<SharedSolutionContainerSystem>();
            var bloom = entMan.SpawnEntity("WFFrostbelleBloom", new EntityCoordinates(grid, 0.5f, 0.5f));
            Assert.That(solutions.TryGetSolution(bloom, "food", out _, out var food), Is.True);
            Assert.That(food!.GetReagentQuantity(new ReagentId("WFWolfmedAnalgesic", null)), Is.GreaterThan(FixedPoint2.Zero),
                "A frostbelle bloom is a painkiller.");
            Assert.That(food.GetReagentQuantity(new ReagentId("WFFrostbelleChill", null)), Is.GreaterThan(FixedPoint2.Zero),
                "A frostbelle bloom slows you.");

            var soup = entMan.SpawnEntity("WFFoodSoupFrozenFungus", new EntityCoordinates(grid, 0.5f, 0.5f));
            Assert.That(solutions.TryGetSolution(soup, "food", out _, out var broth), Is.True);
            Assert.That(broth!.GetReagentQuantity(new ReagentId("WFWarmBroth", null)), Is.GreaterThan(FixedPoint2.Zero));

            var recipe = server.ProtoMan.Index<FoodRecipePrototype>("RecipeWFFrozenFungusSoup");
            Assert.That(recipe.Result, Is.EqualTo("WFFoodSoupFrozenFungus"));
            Assert.That(recipe.IngredientsSolids.ContainsKey("WFFrozenFungus"), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WhipvineLashesAndKnocksDownButSparesTheWildlife()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (_, grid) = await Rig(pair, false);
        EntityUid vine = default, victim = default, fox = default, far = default;
        await server.WaitPost(() =>
        {
            vine = entMan.SpawnEntity("WFFloraWhipvine", new EntityCoordinates(grid, 0.5f, 0.5f));
            victim = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 1.5f, 0.5f));
            far = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 5.5f, 0.5f));
            fox = entMan.SpawnEntity("WFMobThrasciasSnowFox", new EntityCoordinates(grid, -0.5f, 0.5f));
        });
        await pair.RunSeconds(1.5f);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Damage(pair, victim, "Slash"), Is.GreaterThan(FixedPoint2.Zero), "The whipvine should cut a passer-by.");
                Assert.That(entMan.HasComponent<KnockedDownComponent>(victim), Is.True, "The whipvine should knock a passer-by down.");
                Assert.That(Damage(pair, victim, "Slash"), Is.LessThanOrEqualTo(FixedPoint2.New(6)), "The lash should be light.");
                Assert.That(Damage(pair, fox, "Slash"), Is.EqualTo(FixedPoint2.Zero), "The wildlife is spared.");
                Assert.That(Damage(pair, far, "Slash"), Is.EqualTo(FixedPoint2.Zero), "Out of range is safe.");
                Assert.That(entMan.GetComponent<WFReactiveFloraComponent>(vine).State, Is.Not.EqualTo(WFReactiveFloraState.Spent));
            });
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FrostPodBurstsIntoAFreezingCloud()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (_, grid) = await Rig(pair, false);
        EntityUid victim = default;
        await server.WaitPost(() =>
        {
            entMan.SpawnEntity("WFFloraFrostPod", new EntityCoordinates(grid, 0.5f, 0.5f));
            victim = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 1f, 0.5f));
        });
        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            var solutions = server.System<SharedSolutionContainerSystem>();
            var clouds = 0;
            var frost = false;
            var query = entMan.EntityQueryEnumerator<SmokeComponent>();
            while (query.MoveNext(out var cloud, out _))
            {
                clouds++;
                frost |= solutions.TryGetSolution(cloud, "solutionArea", out _, out var solution) &&
                         solution.GetReagentQuantity(new ReagentId("FrostOil", null)) > FixedPoint2.Zero;
            }
            Assert.That(clouds, Is.GreaterThan(0), "The pod should release a cloud.");
            Assert.That(frost, Is.True, "The cloud should be frost oil.");
        });
        await pair.CleanReturnAsync();
    }
}
