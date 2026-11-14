#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NPC.HTN;
using Content.Shared._Shitmed.Body.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Damage;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>A pirate caught in a vented hull puts on a void kit, and lives even with no suit at all.</summary>
    [Test]
    public async Task PirateSurvivesVentedHull()
    {
        await AddAtmosphere();
        var deck = await CreateDeck(new Vector2(6, 0), 7, gravity: true);
        EntityUid pirate = default, bare = default, mortal = default;
        await Server.WaitAssertion(() =>
        {
            FillCrewTestAir(deck);
            var setup = Server.System<WFCrewSetupSystem>();
            var posts = new List<WFCrewSetupPost>
            {
                new() { Position = new Vector2(1.5f) },
                new() { Position = new Vector2(3.5f) },
                new() { Position = new Vector2(5.5f) },
            };
            var mission = new WFCrewMission { Profile = "WFCrewProfilePirate", Group = "pirates" };
            Assert.That(setup.TrySpawn(deck, posts, mission, out var spawned), Is.True);
            (pirate, bare, mortal) = (spawned[0], spawned[1], spawned[2]);
            foreach (var mob in new[] { pirate, bare, mortal })
                SEntMan.GetComponent<HTNComponent>(mob).Enabled = false;
            Assert.That(SEntMan.HasComponent<PressureImmunityComponent>(pirate), Is.True);
            Assert.That(SEntMan.HasComponent<BreathingImmunityComponent>(pirate), Is.True);
            Assert.That(SEntMan.HasComponent<WFCrewVoidKitComponent>(pirate), Is.False, "No kit while the air is good.");
            // One pirate loses the suit-up habit, so only his immunity is left to keep him alive.
            SEntMan.RemoveComponent<WFCrewSuitUpComponent>(bare);
            // The control has neither, so the vented hull has to be able to hurt a pirate.
            SEntMan.RemoveComponent<WFCrewSuitUpComponent>(mortal);
            SEntMan.RemoveComponent<PressureImmunityComponent>(mortal);
            SEntMan.RemoveComponent<BreathingImmunityComponent>(mortal);
#pragma warning disable RA0002 // Vent the whole hull without simulating the leak.
            foreach (var tile in SEntMan.GetComponent<GridAtmosphereComponent>(deck).Tiles.Values)
                tile.Air = GasMixture.SpaceGas;
#pragma warning restore RA0002
        });
        await RunTicks(900);
        await Server.WaitAssertion(() =>
        {
            var mobs = Server.System<MobStateSystem>();
            var eva = Server.System<WFCrewEvaSystem>();
            Assert.That(SEntMan.HasComponent<WFCrewVoidKitComponent>(pirate), Is.True, "He should suit up once the air goes bad.");
            Assert.That(SEntMan.HasComponent<WFCrewVoidKitComponent>(bare), Is.False);
            Assert.That(eva.Prepare(bare), Is.True, "An immune crewman is ready for bad air with no suit.");
            Assert.That(Server.System<InventorySystem>().TryGetSlotEntity(pirate, "outerClothing", out var suit)
                && SEntMan.GetComponent<PressureProtectionComponent>(suit.Value).LowPressureMultiplier >= 100, Is.True);
            foreach (var mob in new[] { pirate, bare })
            {
                Assert.That(mobs.IsAlive(mob), Is.True);
                var damage = SEntMan.GetComponent<DamageableComponent>(mob).Damage;
                Assert.That(damage.DamageDict.GetValueOrDefault("Asphyxiation").Float(), Is.Zero, "An immune pirate takes no asphyxiation.");
            }
            Assert.That(SEntMan.GetComponent<DamageableComponent>(mortal).TotalDamage.Float(), Is.GreaterThan(0f),
                "Control: a pirate with no suit and no immunity should be hurt by the vented hull.");
        });
    }

    /// <summary>An immune crewman with a dead tank keeps his job in bad air; a plain deckhand has to drop it.</summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task ImmuneCrewKeepsWorkingWithoutAir(bool immune)
    {
        var deck = await CreateDeck(new Vector2(6, 0), 7, gravity: true);
        EntityUid crew = default;
        var work = Server.System<WFCrewWorkSystem>();
        await Server.WaitAssertion(() =>
        {
            crew = Server.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(2.5f)), "air-work")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            SEntMan.RemoveComponent<WFCrewVoidKitComponent>(crew);
            if (immune)
            {
                SEntMan.AddComponent<PressureImmunityComponent>(crew);
                SEntMan.AddComponent<BreathingImmunityComponent>(crew);
            }
            Server.System<SharedMapSystem>().SetTile(deck,
                SEntMan.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(deck), new Vector2i(3, 2), new Tile(0));
            Assert.That(work.Advance(deck, "air-work", WFCrewObjectiveKind.Repair, default), Is.EqualTo("working"));
            Assert.That(work.HasJob(crew), Is.True);
            Assert.That(Server.System<InventorySystem>().TryGetSlotEntity(crew, "suitstorage", out var worn), Is.True);
#pragma warning disable RA0002 // Drain the tank the way an hour of breathing would.
            SEntMan.GetComponent<GasTankComponent>(worn!.Value).Air.Clear();
#pragma warning restore RA0002
        });
        await RunTicks(60);
        await Server.WaitAssertion(() =>
        {
            Assert.That(work.HasJob(crew), Is.EqualTo(immune),
                immune ? "An immune crewman should not drop his job for want of air." : "A crewman with no air has to drop his job.");
            if (immune)
                Assert.That(Server.System<WFCrewEvaSystem>().Prepare(crew), Is.True);
        });
    }
}
