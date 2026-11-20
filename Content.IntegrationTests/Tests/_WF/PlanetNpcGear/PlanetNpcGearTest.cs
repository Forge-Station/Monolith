#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.PlanetNpcGear;
using Content.Shared._WF.Planets;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.PlanetNpcGear;

/// <summary>Strangers put down on each world live through its air: dressed for it where it would hurt them, left as they are where it wouldn't.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetNpcGearSystem))]
public sealed class PlanetNpcGearTest
{
    /// <summary>What a world's air does to a body: suffocation, burns and frostbite, and poisoning by what's in it.</summary>
    private static readonly string[] AirDamage = { "Asphyxiation", "Heat", "Cold", "Poison" };

    private static readonly string[] Npcs =
    {
        "WFStrangerDealer", "WFStrangerMugger", "WFStrangerPreacher", "WFStrangerConspiracist", "WFStrangerBeggar",
        "WFStrangerStalker", "WFStrangerSquatter",
    };

    [TestCase("WFSurfaceFervidus", true)]
    [TestCase("WFSurfaceThrascias", true)]
    [TestCase("WFSurfaceAerumna", true)]
    // Carcinoma's carbon dioxide (_WF/PlanetVents).
    [TestCase("WFSurfaceCarcinoma", true)]
    [TestCase("WFSurfaceMerak", false)]
    [TestCase("WFSurfaceAsclepiu", false)]
    public async Task StrangersLiveThroughTheAir(string surface, bool unsafeAir)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var npcs = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -12; x <= 12; x++)
            for (var y = -12; y <= 12; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            // The world's air over the whole map, as its z-network stamps it on every layer.
            var proto = server.ProtoMan.Index<WFPlanetSurfacePrototype>(surface);
            entMan.AddComponents(map.MapUid, proto.NetworkComponents!);
            entMan.EnsureComponent<WFPlanetLayerComponent>(map.MapUid);
            for (var i = 0; i < Npcs.Length; i++)
                npcs.Add(entMan.SpawnEntity(Npcs[i], new EntityCoordinates(map.Grid.Owner, -9.5f + i * 3f, 0.5f)));
        });

        // Whatever they spawned with doesn't count, only what the air does to them after.
        await pair.RunSeconds(3);
        var before = new Dictionary<EntityUid, Dictionary<string, FixedPoint2>>();
        await server.WaitPost(() =>
        {
            foreach (var npc in npcs)
                before[npc] = new Dictionary<string, FixedPoint2>(entMan.GetComponent<DamageableComponent>(npc).Damage.DamageDict);
        });
        await pair.RunSeconds(60);

        await server.WaitAssertion(() =>
        {
            var mobs = server.System<MobStateSystem>();
            var inventory = server.System<InventorySystem>();
            foreach (var npc in npcs)
            {
                var name = entMan.GetComponent<MetaDataComponent>(npc).EntityPrototype!.ID;
                var now = entMan.GetComponent<DamageableComponent>(npc).Damage.DamageDict;
                var hurt = AirDamage
                    .Select(type => (Type: type, Taken: now.GetValueOrDefault(type) - before[npc].GetValueOrDefault(type)))
                    .Where(entry => entry.Taken > 0)
                    .Select(entry => $"{entry.Type} {entry.Taken}")
                    .ToList();
                Assert.That(mobs.IsAlive(npc), Is.True, $"{name} died on {surface}: {string.Join(", ", hurt)}.");
                Assert.That(hurt, Is.Empty, $"{name} is being hurt by the air on {surface}.");

                var gear = entMan.GetComponent<WFPlanetNpcGearComponent>(npc);
                if (unsafeAir)
                    Assert.That(gear.Tank, Is.Not.Null, $"{name} should be breathing from a tank on {surface}.");
                else
                {
                    Assert.That(gear.Tank, Is.Null, $"{name} needs no tank on {surface}.");
                    Assert.That(inventory.TryGetSlotEntity(npc, "mask", out var mask)
                                && entMan.GetComponent<MetaDataComponent>(mask.Value).EntityPrototype?.ID == "ClothingMaskGas",
                        Is.False, $"{name} needs no gas mask on {surface}.");
                }
            }
        });
        await pair.CleanReturnAsync();
    }
}
