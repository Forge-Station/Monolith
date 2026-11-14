#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Server._WF.LivingWorld;
using Content.Shared._WF.LivingWorld;
using Content.Server.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.LivingWorld;

/// <summary>Each reactive flora behaviour acts on a mob in range, and not on one out of range or of an ignored faction.</summary>
[TestFixture]
[TestOf(typeof(WFReactiveFloraSystem))]
public sealed class ReactiveFloraTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  abstract: true
  id: WFTestFloraBase
  components:
  - type: Transform
    anchored: true
  - type: Appearance
  - type: Damageable
    damageContainer: Biological

- type: entity
  parent: WFTestFloraBase
  id: WFTestFloraLash
  components:
  - type: WFReactiveFlora
    behaviour: Lash
    range: 2
    cooldown: 30
    maxTargets: 0
    ignoreFactions: [Chimera]
    healInsteadFactions: [Xeno]
    healDamage:
      types:
        Slash: -10
    damage:
      types:
        Slash: 10

- type: entity
  parent: WFTestFloraBase
  id: WFTestFloraGrab
  components:
  - type: WFReactiveFlora
    behaviour: Grab
    range: 1.5
    cooldown: 30
    holdTime: 2
    heldSpeed: 0
    cutFreeDamage: 10

- type: entity
  parent: WFTestFloraBase
  id: WFTestFloraSpit
  components:
  - type: WFReactiveFlora
    behaviour: Spit
    range: 4
    cooldown: 30
    projectile: BulletAcid

- type: entity
  parent: WFTestFloraBase
  id: WFTestFloraCloud
  components:
  - type: WFReactiveFlora
    behaviour: Cloud
    range: 2
    cooldown: 30
    solution:
      reagents:
      - ReagentId: Water
        Quantity: 10

- type: entity
  parent: WFTestFloraBase
  id: WFTestFloraInject
  components:
  - type: WFReactiveFlora
    behaviour: Inject
    range: 2
    cooldown: 30
    solution:
      reagents:
      - ReagentId: Toxin
        Quantity: 10

- type: entity
  parent: WFTestFloraBase
  id: WFTestFloraIrradiate
  components:
  - type: WFReactiveFlora
    behaviour: Irradiate
    range: 2
    cooldown: 1
    maxTargets: 0
    radsPerSecond: 5

- type: entity
  parent: WFTestFloraBase
  id: WFTestFloraBurst
  components:
  - type: WFReactiveFlora
    behaviour: Explode
    range: 2
    armDelay: 1
    burstRadius: 2
    burstDamage:
      types:
        Piercing: 10
";

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

    /// <summary>Damage of one type; the bare test map chips a little blunt and heat off everyone, so tests read the plant's own.</summary>
    private static FixedPoint2 Damage(TestPair pair, EntityUid uid, string type)
    {
        return pair.Server.EntMan.GetComponent<DamageableComponent>(uid).Damage.DamageDict.GetValueOrDefault(type);
    }

    [Test]
    public async Task LashHitsInRangeSparesIgnoredAndTendsFriends()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var (_, mobs) = await Rig(pair, "WFTestFloraLash", 1f, 1.2f, 1.4f, 5f);
        var (near, ignored, friend, far) = (mobs[0], mobs[1], mobs[2], mobs[3]);
        await server.WaitPost(() =>
        {
            var factions = server.System<NpcFactionSystem>();
            factions.AddFaction(ignored, "Chimera");
            factions.AddFaction(friend, "Xeno");
            var slash = server.ProtoMan.Index<DamageTypePrototype>("Slash");
            server.System<DamageableSystem>().TryChangeDamage(friend, new DamageSpecifier(slash, 20), true);
        });
        await pair.RunSeconds(1.5f);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Damage(pair, near, "Slash"), Is.GreaterThan(FixedPoint2.Zero), "The mob in range should be lashed.");
                Assert.That(Damage(pair, ignored, "Slash"), Is.EqualTo(FixedPoint2.Zero), "An ignored faction should be spared.");
                Assert.That(Damage(pair, friend, "Slash"), Is.LessThan(FixedPoint2.New(20)), "A heal-instead faction should be tended.");
                Assert.That(Damage(pair, far, "Slash"), Is.EqualTo(FixedPoint2.Zero), "A mob out of range should be left alone.");
            });
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GrabHoldsThenLetsGoAndCanBeCutFree()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (plant, mobs) = await Rig(pair, "WFTestFloraGrab", 1f, 4f);
        var (victim, far) = (mobs[0], mobs[1]);
        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFFloraGrabbedComponent>(victim), Is.True, "The plant should take hold.");
            Assert.That(entMan.HasComponent<WFFloraGrabbedComponent>(far), Is.False);
            Assert.That(entMan.GetComponent<MovementSpeedModifierComponent>(victim).WalkSpeedModifier, Is.EqualTo(0f).Within(0.001f),
                "A held mob can't walk.");
            Assert.That(entMan.GetComponent<WFReactiveFloraComponent>(plant).State, Is.EqualTo(WFReactiveFloraState.Armed));
        });
        await pair.RunSeconds(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFFloraGrabbedComponent>(victim), Is.False, "The hold should run out.");
            Assert.That(entMan.GetComponent<MovementSpeedModifierComponent>(victim).WalkSpeedModifier, Is.EqualTo(1f).Within(0.001f));
            // Let it look again straight away.
            server.System<WFReactiveFloraSystem>().CheckNow((plant, entMan.GetComponent<WFReactiveFloraComponent>(plant)));
        });
        await pair.RunSeconds(0.5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFFloraGrabbedComponent>(victim), Is.True, "It should grab again.");
            var blunt = server.ProtoMan.Index<DamageTypePrototype>("Blunt");
            server.System<DamageableSystem>().TryChangeDamage(plant, new DamageSpecifier(blunt, 12), true);
            Assert.That(entMan.HasComponent<WFFloraGrabbedComponent>(victim), Is.False, "Hacking at the plant should cut the mob free.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpitHitsTheTarget()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var (_, mobs) = await Rig(pair, "WFTestFloraSpit", 2.5f, 8f);
        await pair.RunSeconds(1.5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(pair, mobs[0], "Caustic"), Is.GreaterThan(FixedPoint2.Zero), "The spit should hit the mob in range.");
            Assert.That(Damage(pair, mobs[1], "Caustic"), Is.EqualTo(FixedPoint2.Zero));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CloudReleasesSmoke()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        await Rig(pair, "WFTestFloraCloud", 1f);
        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityQuery<SmokeComponent>().Any(), Is.True, "The plant should release a cloud.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CloudStaysShutWithNoOneNear()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        await Rig(pair, "WFTestFloraCloud", 6f);
        await pair.RunSeconds(1.5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityQuery<SmokeComponent>().Any(), Is.False, "Nothing in range, so no cloud.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InjectFillsTheBloodstream()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (_, mobs) = await Rig(pair, "WFTestFloraInject", 1f, 5f);
        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            var solutions = server.System<SharedSolutionContainerSystem>();
            FixedPoint2 Toxin(EntityUid mob)
            {
                var blood = entMan.GetComponent<BloodstreamComponent>(mob);
                return solutions.TryGetSolution(mob, blood.ChemicalSolutionName, out _, out var chem)
                    ? chem.GetTotalPrototypeQuantity("Toxin")
                    : FixedPoint2.Zero;
            }
            Assert.That(Toxin(mobs[0]), Is.GreaterThan(FixedPoint2.Zero), "The mob in range should be injected.");
            Assert.That(Toxin(mobs[1]), Is.EqualTo(FixedPoint2.Zero));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task IrradiateBurnsEveryoneInRange()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var (_, mobs) = await Rig(pair, "WFTestFloraIrradiate", 1f, -1.5f, 5f);
        await pair.RunSeconds(2.5f);

        await server.WaitAssertion(() =>
        {
            FixedPoint2 Rads(EntityUid mob) => server.EntMan.GetComponent<DamageableComponent>(mob).Damage.DamageDict
                .GetValueOrDefault("Radiation");
            Assert.Multiple(() =>
            {
                Assert.That(Rads(mobs[0]), Is.GreaterThan(FixedPoint2.Zero));
                Assert.That(Rads(mobs[1]), Is.GreaterThan(FixedPoint2.Zero));
                Assert.That(Rads(mobs[2]), Is.EqualTo(FixedPoint2.Zero));
            });
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExplodeSwellsThenBurstsAndIsGone()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (plant, mobs) = await Rig(pair, "WFTestFloraBurst", 1f, 6f);
        await pair.RunSeconds(0.8f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFReactiveFloraComponent>(plant).State, Is.EqualTo(WFReactiveFloraState.Armed), "It should swell first.");
            Assert.That(Damage(pair, mobs[0], "Piercing"), Is.EqualTo(FixedPoint2.Zero), "No damage before the burst.");
        });
        await pair.RunSeconds(1.5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(plant), Is.True, "The burst consumes the plant.");
            Assert.That(Damage(pair, mobs[0], "Piercing"), Is.GreaterThan(FixedPoint2.Zero), "The burst hurts the mob beside it.");
            Assert.That(Damage(pair, mobs[1], "Piercing"), Is.EqualTo(FixedPoint2.Zero), "Out of the burst radius.");
        });
        await pair.CleanReturnAsync();
    }
}
