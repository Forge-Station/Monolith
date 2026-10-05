using System.Numerics;
using Content.Server.Movement.Systems;
using Content.Shared._WF.Planets;
using Content.Shared._WF.Planets.Jetpack;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
public sealed class AtmosphericJetpackTest
{
    private const string AtmosphericPack = "WFJetpackAtmospheric";
    private const string GasPack = "JetpackBlueFilled";

    /// <summary>Spawns a mob on a layer wearing a pack on its back, and returns both.</summary>
    private static async Task<(EntityUid Mob, EntityUid Pack)> Wear(Pair.TestPair pair, EntityUid layer, string proto)
    {
        var em = pair.Server.EntMan;
        EntityUid mob = default;
        EntityUid pack = default;

        await pair.Server.WaitPost(() =>
        {
            mob = em.SpawnEntity("MobHuman", new EntityCoordinates(layer, new Vector2(3.5f)));
            em.RunMapInit(mob, em.GetComponent<MetaDataComponent>(mob));
            pack = em.SpawnEntity(proto, new EntityCoordinates(layer, new Vector2(3.5f)));

            Assert.That(pair.Server.System<InventorySystem>().TryEquip(mob, pack, "back", force: true), Is.True,
                "The pack did not go on the mob's back.");
        });

        await pair.Server.WaitRunTicks(1);
        return (mob, pack);
    }

    private static async Task SetEnabled(Pair.TestPair pair, EntityUid pack, EntityUid mob, bool enabled)
    {
        var em = pair.Server.EntMan;

        await pair.Server.WaitPost(() =>
            pair.Server.System<JetpackSystem>().SetEnabled(pack, em.GetComponent<JetpackComponent>(pack), enabled, mob));
        await pair.Server.WaitRunTicks(1);
    }

    private static async Task SetGravity(Pair.TestPair pair, EntityUid layer, float gravity)
    {
        await pair.Server.WaitPost(() =>
            pair.Server.EntMan.GetComponent<WFPlanetLayerComponent>(layer).Gravity = gravity);
    }

    /// <summary>An atmospheric pack lights on the ground at 1 g, refuses at 3 g and in orbit; a gas pack refuses on the ground.</summary>
    [Test]
    public async Task AnAtmosphericJetpackFliesInAirAndNowhereElse()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        await LayTiles(pair, layers[0], new Vector2i(-4, -4), new Vector2i(8, 8));
        var em = pair.Server.EntMan;
        var atmospheric = pair.Server.System<WFAtmosphericJetpackSystem>();

        await SetGravity(pair, layers[0], 1f);
        var (mob, pack) = await Wear(pair, layers[0], AtmosphericPack);

        await SetEnabled(pair, pack, mob, true);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<ActiveJetpackComponent>(pack), Is.True, "The atmospheric pack did not light on the ground at 1 g.");
            Assert.That(em.HasComponent<JetpackUserComponent>(mob), Is.True);
        });

        await SetEnabled(pair, pack, mob, false);
        await SetGravity(pair, layers[0], 3f);
        await SetEnabled(pair, pack, mob, true);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<ActiveJetpackComponent>(pack), Is.False, "The atmospheric pack lit at 3 g.");
            Assert.That(atmospheric.GetRefusal((pack, em.GetComponent<WFAtmosphericJetpackComponent>(pack)), mob),
                Is.EqualTo("wf-jetpack-atmospheric-too-heavy"));
        });

        // Lit at 1 g, then the world turns heavy under it: the pack cuts out.
        await SetGravity(pair, layers[0], 1f);
        await SetEnabled(pair, pack, mob, true);
        await pair.Server.WaitAssertion(() => Assert.That(em.HasComponent<ActiveJetpackComponent>(pack), Is.True));
        await SetGravity(pair, layers[0], 3f);
        await pair.Server.WaitRunTicks(3);
        await pair.Server.WaitAssertion(() =>
            Assert.That(em.HasComponent<ActiveJetpackComponent>(pack), Is.False, "The pack stayed lit after the gravity rose past its limit."));
        await SetGravity(pair, layers[0], 1f);

        // A gas jetpack on the same ground still refuses.
        var (gasMob, gasPack) = await Wear(pair, layers[0], GasPack);
        await SetEnabled(pair, gasPack, gasMob, true);
        await pair.Server.WaitAssertion(() =>
            Assert.That(em.HasComponent<ActiveJetpackComponent>(gasPack), Is.False, "A gas jetpack lit in the atmosphere."));

        // In orbit there is no air to burn; the gas pack is the one that flies there.
        var (orbitMob, orbitPack) = await Wear(pair, layers[^1], AtmosphericPack);
        await SetEnabled(pair, orbitPack, orbitMob, true);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<ActiveJetpackComponent>(orbitPack), Is.False, "The atmospheric pack lit in orbit.");
            Assert.That(atmospheric.GetRefusal((orbitPack, em.GetComponent<WFAtmosphericJetpackComponent>(orbitPack)), orbitMob),
                Is.EqualTo("wf-jetpack-atmospheric-no-air"));
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>A lit pack drains its tank while hovering and switches off when the tank runs dry.</summary>
    [Test]
    public async Task AnAtmosphericJetpackBurnsFuelAndCutsOutEmpty()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        await LayTiles(pair, layers[0], new Vector2i(-4, -4), new Vector2i(8, 8));
        var em = pair.Server.EntMan;
        var atmospheric = pair.Server.System<WFAtmosphericJetpackSystem>();
        var solutions = pair.Server.System<SharedSolutionContainerSystem>();

        await SetGravity(pair, layers[0], 1f);
        var (mob, pack) = await Wear(pair, layers[0], AtmosphericPack);
        var packEnt = new Entity<WFAtmosphericJetpackComponent>(pack, em.GetComponent<WFAtmosphericJetpackComponent>(pack));
        var full = FixedPoint2.Zero;

        await pair.Server.WaitAssertion(() =>
        {
            full = atmospheric.GetFuel(packEnt);
            Assert.That(full, Is.EqualTo(FixedPoint2.New(100)), "A fresh pack is not full.");
        });

        await SetEnabled(pair, pack, mob, true);
        await pair.Server.WaitAssertion(() => Assert.That(em.HasComponent<ActiveJetpackComponent>(pack), Is.True));
        await pair.Server.WaitRunTicks(pair.SecondsToTicks(3f));

        await pair.Server.WaitAssertion(() =>
        {
            var left = atmospheric.GetFuel(packEnt);

            Assert.That(em.HasComponent<ActiveJetpackComponent>(pack), Is.True, "The pack cut out with fuel to spare.");
            Assert.That(left, Is.LessThan(full), "Three seconds of hovering burnt nothing.");
            // Hovering at 1 g is one unit a second; the vertical settle right after lighting may cost a little more.
            Assert.That(left, Is.GreaterThan(FixedPoint2.New(90)), $"Three seconds of hovering burnt {full - left} units.");
        });

        // Leave a sip in the tank and let it burn dry.
        await pair.Server.WaitPost(() =>
        {
            Assert.That(atmospheric.TryGetFuelSolution(packEnt, out var soln, out var solution), Is.True);
            solutions.SplitSolution(soln!.Value, solution!.Volume - FixedPoint2.New(0.3));
        });
        await pair.Server.WaitRunTicks(pair.SecondsToTicks(2f));

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<ActiveJetpackComponent>(pack), Is.False, "The pack kept flying on an empty tank.");
            Assert.That(em.HasComponent<JetpackUserComponent>(mob), Is.False, "The wearer is still flagged as flying.");
            Assert.That(atmospheric.GetFuel(packEnt), Is.EqualTo(FixedPoint2.Zero));
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
