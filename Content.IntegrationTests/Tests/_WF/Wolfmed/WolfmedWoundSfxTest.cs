#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Medical.Tourniquet;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// V124: the feedback layer. A wound that lands makes a noise keyed by what it is and what it is in, a
/// hit throws blood mist or sparks, and a limb coming off does both plus a puddle.
/// </summary>
/// <remarks>
/// Audio cannot be heard from a test, so what is asserted is the selection: which collection the profile
/// picks, which effect prototype a severity earns, and whether the throttle and the damage gate let it
/// through at all. The five interaction sounds are asserted as data on their own components.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedWoundSfxSystem))]
public sealed class WolfmedWoundSfxTest : GameTest
{
    /// <summary>
    /// The profile answers each wound with the collection its cause and its tissue call for, and every
    /// collection and effect it names exists.
    /// </summary>
    [Test]
    public async Task ProfileSelectsSoundByWoundAndTissueTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var profile = prototypes.Index<WolfmedSfxProfilePrototype>(WolfmedWoundSfxSystem.DefaultProfile);

            Assert.Multiple(() =>
            {
                // Bone first: a fracture is a Blunt wound and must not answer with the bruise sound. Playtest 4: its
                // entry is silent, because the crack comes from WolfmedBodySoundSystem.
                Assert.That(profile.GetWoundSound("BoneFractureWound",
                    prototypes.Index<WoundPrototype>("BoneFractureWound"), true, FixedPoint2.New(40)), Is.Null,
                    "the fracture entry claims the wound silently.");
                Assert.That(Collection(profile, prototypes, "WFWolfmedDislocationWound", organic: true, 40),
                    Is.EqualTo("WFWolfmedWoundBone"));
                Assert.That(Collection(profile, prototypes, "CyberneticFrameFractureWound", organic: false, 40),
                    Is.EqualTo("WFWolfmedWoundFrame"));

                // Flesh, by cause.
                Assert.That(Collection(profile, prototypes, "SlashWound", organic: true, 30),
                    Is.EqualTo("WFWolfmedWoundFlesh"));
                Assert.That(Collection(profile, prototypes, "WFWolfmedGunshotWound", organic: true, 30),
                    Is.EqualTo("WFWolfmedWoundPierce"));
                Assert.That(Collection(profile, prototypes, "BurnWound", organic: true, 30),
                    Is.EqualTo("WFWolfmedWoundBurn"));
                Assert.That(Collection(profile, prototypes, "BluntWound", organic: true, 30),
                    Is.EqualTo("WFWolfmedWoundBlunt"));

                // The same wound ids never reach a chassis, but the tissue filter is what proves the
                // selector is reading the part and not the wound: a chassis answers with metal.
                Assert.That(Collection(profile, prototypes, "WFWolfmedDentWound", organic: false, 30),
                    Is.EqualTo("WFWolfmedWoundChassis"));
                Assert.That(Collection(profile, prototypes, "WFWolfmedShortCircuitWound", organic: false, 30),
                    Is.EqualTo("WFWolfmedWoundShock"));
                Assert.That(Collection(profile, prototypes, "WFWolfmedBreachWound", organic: false, 30),
                    Is.EqualTo("WFWolfmedWoundChassis"));

                // A bruise below the entry's own floor says nothing at all.
                Assert.That(profile.GetWoundSound("BluntWound", prototypes.Index<WoundPrototype>("BluntWound"),
                    true, FixedPoint2.New(6)), Is.Null, "a light bruise is not worth a sound.");
            });

            Assert.Multiple(() =>
            {
                foreach (var entry in profile.WoundSounds)
                {
                    if (entry.Sound == null)
                        continue;

                    Assert.That(entry.Sound, Is.InstanceOf<SoundCollectionSpecifier>(),
                        "every wound sound is a collection, so it can be swapped in one file.");
                    Assert.That(prototypes.HasIndex<SoundCollectionPrototype>(Id(entry.Sound)), Is.True,
                        $"{Id(entry.Sound)} has no collection.");
                }

                foreach (var tier in profile.OrganicDebris.Concat(profile.MechanicalDebris))
                    Assert.That(prototypes.HasIndex<EntityPrototype>(tier.Effect), Is.True,
                        $"{tier.Effect} has no prototype.");

                foreach (var spec in new[] { profile.OrganicDismemberment, profile.MechanicalDismemberment })
                {
                    Assert.That(spec.Sound, Is.Not.Null);
                    Assert.That(prototypes.HasIndex<SoundCollectionPrototype>(Id(spec.Sound!)), Is.True);
                    Assert.That(spec.Effect, Is.Not.Null);
                    Assert.That(prototypes.HasIndex<EntityPrototype>(spec.Effect!.Value), Is.True);
                }
            });
        });
    }

    /// <summary>Debris comes in three coarse sizes per tissue, and nothing at all below the smallest.</summary>
    [Test]
    public async Task DebrisTiersScaleWithSeverityTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var profile = prototypes.Index<WolfmedSfxProfilePrototype>(WolfmedWoundSfxSystem.DefaultProfile);

            Assert.Multiple(() =>
            {
                // Organic tiers are keyed on the bleed increase now, and hitSplatter.minBleedIncrease is the floor,
                // so the smallest tier starts at nothing.
                Assert.That(profile.GetDebris(true, FixedPoint2.New(1))?.Id, Is.EqualTo("WFWolfmedBloodMistSmall"));
                Assert.That(profile.GetDebris(true, FixedPoint2.New(8))?.Id, Is.EqualTo("WFWolfmedBloodMistMedium"));
                Assert.That(profile.GetDebris(true, FixedPoint2.New(60))?.Id, Is.EqualTo("WFWolfmedBloodMistLarge"));

                Assert.That(profile.GetDebris(false, FixedPoint2.New(1)), Is.Null);
                Assert.That(profile.GetDebris(false, FixedPoint2.New(5))?.Id, Is.EqualTo("WFWolfmedSparkBurstSmall"));
                Assert.That(profile.GetDebris(false, FixedPoint2.New(60))?.Id, Is.EqualTo("WFWolfmedSparkBurstLarge"));

                // Every debris entity is cheap: a sprite and a clock, nothing that ticks.
                foreach (var tier in profile.OrganicDebris.Concat(profile.MechanicalDebris))
                {
                    var entity = prototypes.Index<EntityPrototype>(tier.Effect);
                    Assert.That(entity.Components.ContainsKey("TimedDespawn"), Is.True,
                        $"{tier.Effect} would never be cleaned up.");
                }
            });
        });
    }

    /// <summary>
    /// A hit that wounds throws debris at the body and is then throttled: nine pellets are one spray, not
    /// nine. The sound shares the same gate.
    /// </summary>
    [Test]
    public async Task HitDebrisSpawnsOnceAndDespawnsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid mist = default;

        await server.WaitAssertion(() =>
        {
            var sfx = entities.System<WolfmedWoundSfxSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);

            Damage(entities, body, TargetBodyPart.Torso, "Slash", 30);
            var spawned = Debris(entities, map);
            Assert.That(spawned, Has.Count.EqualTo(1), "the hit throws exactly one spray.");
            mist = spawned[0];

            // A second hit in the same breath adds nothing: the body is inside its debris interval.
            Damage(entities, body, TargetBodyPart.Torso, "Slash", 30);
            Assert.That(Debris(entities, map), Has.Count.EqualTo(1), "buckshot is one spray, not nine.");

            var state = entities.GetComponent<WolfmedWoundSfxComponent>(body);
            var profile = sfx.Profile!;
            Assert.Multiple(() =>
            {
                Assert.That(state.NextDebris, Is.GreaterThan(sfx.Now));
                Assert.That(state.NextSound, Is.GreaterThan(sfx.Now));
                Assert.That(sfx.TrySpawnDebris(body, state, profile, true, FixedPoint2.New(50)), Is.Null,
                    "and the throttle refuses a third.");
                Assert.That(sfx.TryPlayWound(body, state, profile, "SlashWound", null, true, FixedPoint2.New(50)),
                    Is.False);
            });
        });

        // Past every effect's lifetime, the 1.2 s hit splatter included.
        await Pair.RunTicksSync(120);
        await server.WaitAssertion(() =>
            Assert.That(entities.Deleted(mist), Is.True, "debris cleans itself up."));
    }

    /// <summary>
    /// The damage gate. A wound that appears without a hit behind it - an infection landing, a necrosis
    /// clock running out - is silent and throws nothing, which is the whole reason the gate exists.
    /// </summary>
    [Test]
    public async Task TimeBasedWoundsAreSilentTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(entities, body, BodyPartType.Torso);

            // Created straight on the part, exactly as the infection and necrosis timers create theirs.
            Assert.That(wounds.CreateOrMergeWound(torso, "SlashWound", FixedPoint2.New(40)), Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(Debris(entities, map), Is.Empty, "a wound nothing hit throws no debris.");
                Assert.That(entities.HasComponent<WolfmedWoundSfxComponent>(body), Is.False,
                    "and never even stamps the body.");
            });

            // The same wound worsening under a hit does both.
            Damage(entities, body, TargetBodyPart.Torso, "Slash", 30);
            Assert.That(Debris(entities, map), Has.Count.EqualTo(1));
        });
    }

    /// <summary>
    /// FIX1: the trigger is the bleed, not the damage type. A burn wounds and hurts and never bleeds, so
    /// it throws nothing; a blunt hit that raises a bleeding wound throws blood like any other.
    /// </summary>
    [Test]
    public async Task BloodOnlyFliesWhenTheHitBleedsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var sfx = entities.System<WolfmedWoundSfxSystem>();
            var burned = entities.SpawnEntity("MobHuman", map.GridCoords);

            // BurnWound carries no WoundBleedingBehavior at any stage, so this cannot open a bleed.
            Damage(entities, burned, TargetBodyPart.Torso, "Heat", 60);
            Assert.Multiple(() =>
            {
                Assert.That(sfx.TotalBleeding(burned), Is.EqualTo(FixedPoint2.Zero),
                    "a burn is the case that must not bleed; the test is meaningless if it does.");
                Assert.That(Debris(entities, map), Is.Empty, "a hit that opens no bleed throws no blood.");
            });
        });

        await server.WaitAssertion(() =>
        {
            var sfx = entities.System<WolfmedWoundSfxSystem>();
            var wounds = entities.System<WoundSystem>();
            var bruised = entities.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 3, 3));
            var torso = Part(entities, bruised, BodyPartType.Torso);

            // A bruise that is already weeping. Placed by hand rather than rolled for, because the blunt
            // bleeding behaviour has a chance and this test may not depend on it.
            // Well under the wound's maximum severity, so the hit below has room to widen it.
            var wound = wounds.CreateOrMergeWound(torso, "BluntWound", FixedPoint2.New(10));
            Assert.That(wound, Is.Not.Null);
            var bleeding = entities.EnsureComponent<WoundBleedingComponent>(wound!.Value);
            bleeding.BleedingSeverity = FixedPoint2.New(10);
            Assert.That(Debris(entities, map), Is.Empty, "creating that wound was not a hit.");

            // Now hit it. Blunt, no cut anywhere, and the bleed goes up: that is a spray.
            // Ten, not forty: a heavy blunt hit becomes a crush injury (a different wound) and can roll a fracture.
            Damage(entities, bruised, TargetBodyPart.Torso, "Blunt", 10);
            Assert.That(sfx.TotalBleeding(bruised), Is.GreaterThan(FixedPoint2.New(10)),
                "the hit should have driven the bleed up.");
            Assert.That(Debris(entities, map), Has.Count.EqualTo(1),
                "a blunt hit that opens a bleed sprays like anything else.");
        });
    }

    /// <summary>
    /// FIX1: the burst budget. Automatic fire lands a bleeding hit per round, and the answer is a few
    /// sprays a second rather than one sprite per bullet.
    /// </summary>
    [Test]
    public async Task BurstBudgetCapsASustainedBurstTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var sfx = entities.System<WolfmedWoundSfxSystem>();
            var spec = sfx.Profile!.HitSplatter;
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var state = entities.EnsureComponent<WolfmedWoundSfxComponent>(body);

            Assert.That(spec.BurstBudget, Is.GreaterThan(0), "a budget of zero would disable the cap.");

            for (var i = 0; i < spec.BurstBudget; i++)
                Assert.That(sfx.TryTakeBurstBudget(state, spec), Is.True, $"spray {i} is inside the budget.");

            Assert.Multiple(() =>
            {
                Assert.That(sfx.TryTakeBurstBudget(state, spec), Is.False, "the budget has to run out.");
                Assert.That(state.SpraysInWindow, Is.EqualTo(spec.BurstBudget),
                    "a refused spray must not be counted against the window.");
            });

            // The window is restarted lazily, so backdating it is what a quiet second looks like.
            state.BurstWindowStart = sfx.Now - spec.BurstWindow;
            Assert.That(sfx.TryTakeBurstBudget(state, spec), Is.True, "and it has to come back.");
        });
    }

    /// <summary>
    /// A limb torn off sounds different, and leaves the body's own fluid on the deck: blood for flesh,
    /// oil for a chassis, because the reagent is read off the bloodstream rather than named in the profile.
    /// </summary>
    [Test]
    public async Task DismembermentSoundsAndSpillsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ProtoMan;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var amputation = entities.System<AmputationSystem>();
            var dismemberment = entities.System<WolfmedDismembermentSystem>();
            var profile = prototypes.Index<WolfmedSfxProfilePrototype>(WolfmedWoundSfxSystem.DefaultProfile);

            var flesh = entities.SpawnEntity("MobHuman", map.GridCoords);
            var machine = entities.SpawnEntity("MobIPC", map.GridCoords);

            Assert.Multiple(() =>
            {
                Assert.That(Id(profile.OrganicDismemberment.Sound!), Is.EqualTo("WFWolfmedDismemberOrganic"),
                    "flesh tears.");
                Assert.That(Id(profile.MechanicalDismemberment.Sound!), Is.EqualTo("WFWolfmedDismemberMechanical"),
                    "a chassis shears.");
                Assert.That(profile.MechanicalDismemberment.Effect?.Id, Does.StartWith("WFWolfmedSparkBurst"));
            });

            // The spill is the body's own reagent. Asserted through the public helper so the puddle is
            // attributable to this call and not to any bleeding that ran alongside it.
            var bloodPuddle = dismemberment.TrySpill(flesh, profile.OrganicDismemberment.SpillVolume);
            var oilPuddle = dismemberment.TrySpill(machine, profile.MechanicalDismemberment.SpillVolume);

            Assert.Multiple(() =>
            {
                Assert.That(bloodPuddle, Is.Not.Null);
                Assert.That(entities.HasComponent<PuddleComponent>(bloodPuddle!.Value), Is.True);
                Assert.That(oilPuddle, Is.Not.Null);
                Assert.That(entities.HasComponent<PuddleComponent>(oilPuddle!.Value), Is.True);
            });

            // And the real path: an amputation from damage raises the event the system listens to.
            var arm = entities.System<SharedBodySystem>().GetBodyChildren(flesh)
                .First(part => part.Component.PartType == BodyPartType.Arm).Id;
            Assert.That(amputation.TryAmputate(flesh, arm), Is.True);
            Assert.That(entities.GetComponent<WolfmedWoundSfxComponent>(flesh).NextSound,
                Is.GreaterThan(entities.System<WolfmedWoundSfxSystem>().Now),
                "the tear stamps the shared throttle, so the stump's wounds do not double it.");
        });
    }

    /// <summary>
    /// The five interactions phase 8 left silent all have a sound, and every one of them names a shipped
    /// collection. Data only: the systems play whatever these fields hold.
    /// </summary>
    [Test]
    public async Task InteractionSoundsAreDeclaredTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ProtoMan;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var cautery = prototypes.Index<WolfmedCauteryProfilePrototype>(WolfmedCauterySystem.DefaultProfile);
            var dislocation = prototypes.Index<WoundPrototype>("WFWolfmedDislocationWound");
            Assert.That(dislocation.TryGetBehavior(FixedPoint2.New(40), out WolfmedDislocationBehavior relocate),
                Is.True);

            var embedded = new WolfmedEmbeddedObjectComponent();
            var tourniquet = new WolfmedTourniquetComponent();
            var wrench = entities.SpawnEntity("Wrench", map.GridCoords);

            var sounds = new (string What, SoundSpecifier? Sound)[]
            {
                ("embedded removal start", embedded.BeginSound),
                ("embedded removal finish", embedded.EndSound),
                ("relocate start", relocate.BeginSound),
                ("relocate finish", relocate.EndSound),
                ("deliberate cautery start", cautery.DeliberateBeginSound),
                ("deliberate cautery finish", cautery.DeliberateEndSound),
                ("tourniquet loosen", tourniquet.LoosenSound),
                ("wrench repair finish",
                    entities.GetComponent<WolfmedRepairSoundComponent>(wrench).EndSound),
            };

            Assert.Multiple(() =>
            {
                foreach (var (what, sound) in sounds)
                {
                    Assert.That(sound, Is.Not.Null, $"{what} has no sound.");
                    Assert.That(prototypes.HasIndex<SoundCollectionPrototype>(Id(sound!)), Is.True,
                        $"{what} names a collection that does not exist.");
                }

                // The tourniquet item's own two sounds are Onyx's and were already set; this is the pair
                // taking it off, which had nothing.
                Assert.That(prototypes.Index<EntityPrototype>("Tourniquet")
                    .Components.ContainsKey("Tourniquet"), Is.True);
            });
        });
    }

    /// <summary>Both CVars exist, default on, and silence their half of the layer when turned off.</summary>
    [Test]
    public async Task CVarsDisableTheLayerTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        Assert.Multiple(() =>
        {
            Assert.That(server.CfgMan.GetCVar(WolfmedCVars.WoundSfx), Is.True);
            Assert.That(server.CfgMan.GetCVar(WolfmedCVars.HitDebris), Is.True);
        });

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(WolfmedCVars.HitDebris.Name, false);
            server.CfgMan.SetCVar(WolfmedCVars.WoundSfx.Name, false);
        });

        await server.WaitAssertion(() =>
        {
            var sfx = entities.System<WolfmedWoundSfxSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);

            Damage(entities, body, TargetBodyPart.Torso, "Slash", 40);

            var state = entities.GetComponent<WolfmedWoundSfxComponent>(body);
            Assert.Multiple(() =>
            {
                Assert.That(Debris(entities, map), Is.Empty, "wolfmed.hit_debris off spawns nothing.");
                Assert.That(state.NextSound, Is.EqualTo(TimeSpan.Zero),
                    "wolfmed.wound_sfx off never even opens the throttle.");
                Assert.That(sfx.TryPlayWound(body, state, sfx.Profile!, "SlashWound", null, true,
                    FixedPoint2.New(50)), Is.False);
            });
        });

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(WolfmedCVars.HitDebris.Name, true);
            server.CfgMan.SetCVar(WolfmedCVars.WoundSfx.Name, true);
        });
    }

    private static string Collection(
        WolfmedSfxProfilePrototype profile,
        IPrototypeManager prototypes,
        string wound,
        bool organic,
        int severity)
    {
        var sound = profile.GetWoundSound(wound, prototypes.Index<WoundPrototype>(wound), organic,
            FixedPoint2.New(severity));
        Assert.That(sound, Is.Not.Null, $"{wound} picked no sound.");
        return Id(sound!);
    }

    private static string Id(SoundSpecifier sound) =>
        sound is SoundCollectionSpecifier collection ? collection.Collection ?? string.Empty : string.Empty;

    /// <summary>
    /// Every Wolfmed debris effect currently alive on the test map. GORE/G1 put the directional spray in
    /// front of the blood mist for flesh, so it counts here too: it is the same throttled spawn.
    /// </summary>
    private static List<EntityUid> Debris(IEntityManager entities, TestMapData map)
    {
        var found = new List<EntityUid>();
        var query = entities.EntityQueryEnumerator<TimedDespawnComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var meta))
        {
            if (meta.EntityPrototype?.ID.StartsWith("WFWolfmedBloodMist") == true ||
                meta.EntityPrototype?.ID.StartsWith("WFWolfmedHitSplatter") == true ||
                meta.EntityPrototype?.ID.StartsWith("WFWolfmedSparkBurst") == true)
                found.Add(uid);
        }

        return found;
    }

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type) =>
        entities.System<SharedBodySystem>().GetBodyChildren(body)
            .First(part => part.Component.PartType == type).Id;

    private static void Damage(
        IEntityManager entities,
        EntityUid body,
        TargetBodyPart target,
        string type,
        int amount)
    {
        var spec = new DamageSpecifier
        {
            DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
        };
        entities.System<DamageableSystem>().TryChangeDamage(body, spec, origin: null, targetPart: target);
        // FIX1: the blood spray is decided once the hit's wounds all exist, which in play is the system's
        // own tick. Drained here so an assertion in the same block sees the same thing a player would.
        entities.System<WolfmedWoundSfxSystem>().Update(0f);
    }
}
