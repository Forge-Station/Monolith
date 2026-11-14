#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Damage;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Server.Body.Components;
using Content.Server.Medical.Components;
using Content.Shared._Onyx.Targeting;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Steps.Parts;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Damage;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// EVISC: a torso opened by a hit its 250 cap could not absorb. The torso is the one part D9 never severs,
/// so the overflow that would take a limb off buys disembowelment instead.
/// </summary>
/// <remarks>
/// Everything here drives the real damage routing rather than the system directly, because the trigger is
/// the interesting half: the hit has to land on the torso, be the right damage type, overflow the cap and
/// be big enough on its own. <see cref="WolfmedEviscerationSystem.ForcedRoll"/> is pinned wherever a chance
/// is involved, so no test here can flake on a die roll.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedEviscerationSystem))]
public sealed class WolfmedEviscerationTest : GameTest
{
    /// <summary>The torso's structural cap (Body/parts.yml). One hit of this puts it exactly at the line.</summary>
    private const int Cap = 250;

    /// <summary>Past the profile's 35 Slash bar, so the hit after the cap tears the belly open.</summary>
    private const int BigSlash = 50;

    /// <summary>The abdominal slots a Slash evisceration always empties.</summary>
    private static readonly string[] Abdominal = ["stomach", "liver", "kidneys"];

    /// <summary>The slots behind the ribs, which only a blast reaches.</summary>
    private static readonly string[] Vital = ["heart", "lungs"];

    /// <summary>
    /// The whole trigger and the whole effect on a patient who is still alive: the wound lands, the
    /// abdominal organs are out of the body and on the deck, and what is left bleeds hard enough for G2 to
    /// call it a major source.
    /// </summary>
    [Test]
    public async Task BigSlashOnACappedTorsoEviscerationTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = Patient(entities, map.GridCoords, out var torso);
            var before = Organs(entities, torso);
            Cut(entities, body, Cap);

            Assert.That(entities.GetComponent<MobStateComponent>(body).CurrentState, Is.EqualTo(MobState.Alive),
                "the trigger has to work on a living patient, so this one must still be one.");

            Forced(entities, 1f); // Nothing rolled: the chest stays shut for a cut.
            Cut(entities, body, BigSlash);

            var wounds = entities.System<WoundSystem>();
            var wound = FindWound(entities, wounds, torso, "WFWolfmedEviscerationWound");
            Assert.That(wound, Is.Not.Null, "a big cut on a capped torso opens it.");

            var after = Organs(entities, torso);
            Assert.Multiple(() =>
            {
                foreach (var slot in Abdominal)
                {
                    Assert.That(before.ContainsKey(slot), Is.True, $"the fixture had no {slot} to lose.");
                    Assert.That(after.ContainsKey(slot), Is.False, $"{slot} is still in the torso.");
                    Assert.That(entities.GetComponent<TransformComponent>(before[slot]).ParentUid,
                        Is.Not.EqualTo(torso), $"{slot} never left the body.");
                    Assert.That(entities.System<SharedContainerSystem>().IsEntityInContainer(before[slot]),
                        Is.False, $"{slot} is not on the floor.");
                }

                foreach (var slot in Vital)
                {
                    Assert.That(after.ContainsKey(slot), Is.True,
                        $"{slot} is behind the ribs; a cut does not reach it.");
                }
            });

            var bleeding = entities.GetComponent<WoundBleedingComponent>(wound!.Value);
            var profile = entities.System<WolfmedWoundSfxSystem>().Profile;
            Assert.Multiple(() =>
            {
                Assert.That(bleeding.CurrentRate, Is.GreaterThan(0f));
                Assert.That(profile, Is.Not.Null);
                Assert.That(bleeding.CurrentRate, Is.GreaterThanOrEqualTo(profile!.BleedSpurt.MajorRate),
                    "G2 reads anything at or above majorRate as a source worth spurting from.");
                Assert.That(bleeding.AutomaticClottingAt, Is.Null, "an open belly never gets a clotting deadline.");
            });

            // V3: the torso shows its worst degradation while the wound is there, and nothing whittles it down.
            var degradation = entities.System<WolfmedDegradationVisualsSystem>();
            var visuals = prototypes.Index<WolfmedDegradationProfilePrototype>("WFWolfmedDegradationDefault");
            Assert.That(degradation.GetStage(torso, visuals), Is.EqualTo(WolfmedPartDegradation.Bone),
                "severity 70 is past the degradation profile's stage 2, so the overlay sits at its worst.");
        });
    }

    /// <summary>
    /// M1b (plan §6.2): the hit past the cap still arrives as one event after evisceration has had its
    /// overflow: nothing stored, the whole cut as Overflow. (The cut's wound is already at its 200 here;
    /// wound growth on overflow is SaturatedTorsoTest's.)
    /// </summary>
    [Test]
    public async Task OverflowHitFollowsTheTearTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var hits = entities.System<WolfmedPartHitSystem>();

        try
        {
            await server.WaitAssertion(() =>
            {
                var body = Patient(entities, map.GridCoords, out var torso);
                Cut(entities, body, Cap);
                Forced(entities, 1f);

                var wounds = entities.System<WoundSystem>();
                var seen = new List<(PartDamageAppliedEvent Hit, bool Torn)>();
                hits.Observer = (part, hit) =>
                {
                    if (part == torso)
                        seen.Add((hit, FindWound(entities, wounds, torso, "WFWolfmedEviscerationWound") != null));
                };

                Cut(entities, body, BigSlash);

                Assert.Multiple(() =>
                {
                    Assert.That(seen, Has.Count.EqualTo(1), "the cut past the cap was not exactly one event.");
                    Assert.That(seen[0].Torn, Is.True, "the hit's event came before evisceration had its overflow.");
                    Assert.That(seen[0].Hit.Damage.GetTotal(), Is.EqualTo(FixedPoint2.Zero), "a capped torso stored some of the cut.");
                    Assert.That(seen[0].Hit.Overflow?.GetTotal(), Is.EqualTo(FixedPoint2.New(BigSlash)),
                        "the event does not carry the whole cut as Overflow.");
                });
            });
        }
        finally
        {
            hits.Observer = null;
            await server.WaitPost(() => entities.System<WolfmedEviscerationSystem>().ForcedRoll = null);
        }
    }

    /// <summary>
    /// Everything that must NOT open a torso: a small cut, the three damage types the profile leaves out of
    /// its table, and a big cut on a torso that is nowhere near its cap.
    /// </summary>
    [Test]
    public async Task WrongHitsLeaveTheTorsoShutTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            Forced(entities, 1f);

            // A capped torso, hit too lightly to finish anything.
            var small = Patient(entities, map.GridCoords, out var smallTorso);
            Cut(entities, small, Cap);
            Cut(entities, small, 20);
            Assert.That(FindWound(entities, wounds, smallTorso, "WFWolfmedEviscerationWound"), Is.Null,
                "20 Slash is under the profile's 35 bar.");

            // A capped torso, hit hard with everything the table leaves out.
            foreach (var type in new[] { "Blunt", "Piercing", "Heat" })
            {
                var body = Patient(entities, map.GridCoords, out var torso);
                Cut(entities, body, Cap);
                Hit(entities, body, type, 120);
                Assert.That(FindWound(entities, wounds, torso, "WFWolfmedEviscerationWound"), Is.Null,
                    $"{type} is not in finishingDamage, so it can never open a belly.");
            }

            // An intact torso: a big cut, but nothing overflows, so nothing is offered.
            var fresh = Patient(entities, map.GridCoords, out var freshTorso);
            Cut(entities, fresh, BigSlash);
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<WoundableComponent>(freshTorso).AmputationOverflow,
                    Is.EqualTo(FixedPoint2.Zero));
                Assert.That(FindWound(entities, wounds, freshTorso, "WFWolfmedEviscerationWound"), Is.Null,
                    "the cap has to be reached first.");
            });
        });
    }

    /// <summary>A second hit on an open belly worsens nothing and creates nothing. One at a time.</summary>
    [Test]
    public async Task SecondHitDoesNotOpenASecondTearTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var body = Patient(entities, map.GridCoords, out var torso);
            Forced(entities, 1f);
            Cut(entities, body, Cap);
            Cut(entities, body, BigSlash);

            var first = FindWound(entities, wounds, torso, "WFWolfmedEviscerationWound");
            Assert.That(first, Is.Not.Null);
            var severity = entities.GetComponent<WoundComponent>(first!.Value).Severity;

            Cut(entities, body, BigSlash);
            Cut(entities, body, BigSlash);

            var all = wounds.GetWounds((torso, entities.GetComponent<WoundableComponent>(torso)))
                .Count(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>("WFWolfmedEviscerationWound"));

            Assert.Multiple(() =>
            {
                Assert.That(all, Is.EqualTo(1), "one evisceration per torso.");
                Assert.That(entities.GetComponent<WoundComponent>(first.Value).Severity, Is.EqualTo(severity),
                    "and it is not deepened by hits the system refuses.");
            });
        });
    }

    /// <summary>A blast reaches behind the ribs; the same body cut open does not.</summary>
    [Test]
    public async Task ExplosionEmptiesTheChestTooTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var routing = entities.System<WoundDamageRoutingSystem>();
            var wounds = entities.System<WoundSystem>();
            var body = Patient(entities, map.GridCoords, out var torso);
            Forced(entities, 1f); // The chest comes out on the explosion path alone, not on a roll.
            Cut(entities, body, Cap);

            Assert.That(routing.TryRouteDistributedDamage(body, Spec("Slash", BigSlash),
                TargetBodyPart.Torso, DamageDistribution.SplitEvenly,
                origin: null, ignoreResistances: true, interruptsDoAfters: false, variation: 0f,
                isExplosion: true, woundSeverityMultiplier: 1f,
                originFlag: DamageableSystem.DamageOriginFlag.Explosion), Is.True);

            var after = Organs(entities, torso);
            Assert.Multiple(() =>
            {
                Assert.That(FindWound(entities, wounds, torso, "WFWolfmedEviscerationWound"), Is.Not.Null);
                foreach (var slot in Vital)
                    Assert.That(after.ContainsKey(slot), Is.False, $"a blast takes the {slot} as well.");
                foreach (var slot in Abdominal)
                    Assert.That(after.ContainsKey(slot), Is.False, $"and the {slot} with it.");
            });
        });
    }

    /// <summary>
    /// What a medic can and cannot do with items: gauze holds the flow and nothing more, sutures and
    /// brute packs do nothing at all, and the surgery is the only exit.
    /// </summary>
    [Test]
    public async Task ItemsSlowItAndOnlySurgeryClosesItTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var healing = entities.System<WoundHealingSystem>();
            var body = Patient(entities, map.GridCoords, out var torso);
            Forced(entities, 1f);
            Cut(entities, body, Cap);
            Cut(entities, body, BigSlash);

            var wound = FindWound(entities, wounds, torso, "WFWolfmedEviscerationWound");
            Assert.That(wound, Is.Not.Null);
            var bleeding = entities.GetComponent<WoundBleedingComponent>(wound!.Value);
            var openRate = bleeding.CurrentRate;
            var severity = entities.GetComponent<WoundComponent>(wound.Value).Severity;

            var gauze = entities.SpawnEntity("Gauze1", map.GridCoords);
            Assert.That(healing.TryApplyHealing(body, torso,
                (gauze, entities.GetComponent<HealingComponent>(gauze)), body, out _, out _), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(bleeding.Treatment, Is.EqualTo(BleedingTreatment.Bandaged), "gauze dresses it,");
                Assert.That(bleeding.CurrentRate, Is.GreaterThan(0f).And.LessThan(openRate), "and only slows it.");
                Assert.That(FindWound(entities, wounds, torso, "WFWolfmedEviscerationWound"), Is.Not.Null);
            });

            foreach (var item in new[] { "MedicatedSuture", "BrutepackAdvanced1" })
            {
                var topical = entities.SpawnEntity(item, map.GridCoords);
                healing.TryApplyHealing(body, torso,
                    (topical, entities.GetComponent<HealingComponent>(topical)), body, out _, out _);
            }

            Assert.That(entities.GetComponent<WoundComponent>(wound.Value).Severity, Is.EqualTo(severity),
                "healingMultiplier 0 keeps every item out of it.");

            // The surgery: clamp, then close. The clamp is what makes the close possible at all.
            Step(entities, "WFSurgeryStepClampEvisceration", body, torso);
            Step(entities, "WFSurgeryStepCloseEvisceration", body, torso);

            var left = wounds.GetWounds((torso, entities.GetComponent<WoundableComponent>(torso)))
                .Where(found => found.Comp.Prototype == new ProtoId<WoundPrototype>("SlashWound"))
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(FindWound(entities, wounds, torso, "WFWolfmedEviscerationWound"), Is.Null,
                    "closing it removes the tear,");
                Assert.That(left, Is.Not.Empty, "and leaves an ordinary cut behind.");
                Assert.That(entities.GetComponent<WoundBleedingComponent>(left[0].Owner).Treatment,
                    Is.EqualTo(BleedingTreatment.Sutured), "sutured shut.");
                Assert.That(entities.HasComponent<WolfmedEviscerationComponent>(torso), Is.False);
            });
        });
    }

    /// <summary>
    /// The shortcut: the belly is already open, so Shitmed's incision state is already satisfied and an
    /// organ goes back in through the shipped insertion surgery with no scalpel and no retractor. Closing
    /// the tear then takes the granted state away again.
    /// </summary>
    [Test]
    public async Task OpenBellyCountsAsAnOpenIncisionTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var body = Patient(entities, map.GridCoords, out var torso);
            var before = Organs(entities, torso);
            Forced(entities, 1f);
            Cut(entities, body, Cap);
            Cut(entities, body, BigSlash);

            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<IncisionOpenComponent>(torso), Is.True,
                    "a torn belly is an open incision as far as the surgery system is concerned,");
                Assert.That(entities.HasComponent<SkinRetractedComponent>(torso), Is.True,
                    "and the skin is already back.");
                Assert.That(StepComplete(entities, "SurgeryStepOpenIncisionScalpel", body, torso), Is.True,
                    "so the scalpel step reports done");
                Assert.That(StepComplete(entities, "SurgeryStepRetractSkin", body, torso), Is.True,
                    "and so does the retractor step.");
            });

            // The shipped insertion surgery, driven exactly as WolfmedWoundSurgeryTest drives a step.
            var liver = before["liver"];
            var surgery = entities.SpawnEntity("SurgeryInsertLiver", map.GridCoords);
            var step = entities.SpawnEntity("SurgeryStepInsertOrgan", map.GridCoords);
            var insert = new SurgeryStepEvent(body, body, torso, new List<EntityUid> { liver }, surgery);
            entities.EventBus.RaiseLocalEvent(step, ref insert);

            Assert.That(Organs(entities, torso).ContainsKey("liver"), Is.True,
                "the existing organ insertion works on an eviscerated torso.");

            Step(entities, "WFSurgeryStepClampEvisceration", body, torso);
            Step(entities, "WFSurgeryStepCloseEvisceration", body, torso);

            Assert.Multiple(() =>
            {
                Assert.That(FindWound(entities, wounds, torso, "WFWolfmedEviscerationWound"), Is.Null);
                Assert.That(entities.HasComponent<IncisionOpenComponent>(torso), Is.False,
                    "the open-belly state was this system's, so it goes with the wound.");
                Assert.That(entities.HasComponent<SkinRetractedComponent>(torso), Is.False);
            });
        });
    }

    /// <summary>A chassis breaches rather than eviscerates: its own wound, its own contents, and its own fluid.</summary>
    [Test]
    public async Task ChassisBreachesInsteadTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var torso = Part(entities, body, BodyPartType.Torso);
            Raise(entities, body);
            var before = Organs(entities, torso);
            Forced(entities, 1f);

            Cut(entities, body, Cap);
            Cut(entities, body, BigSlash);

            var after = Organs(entities, torso);
            Assert.Multiple(() =>
            {
                Assert.That(FindWound(entities, wounds, torso, "WFWolfmedChassisBreachWound"), Is.Not.Null,
                    "a chassis gets the mechanical wound,");
                Assert.That(FindWound(entities, wounds, torso, "WFWolfmedEviscerationWound"), Is.Null,
                    "and never the organic one.");
                Assert.That(before.ContainsKey("pump"), Is.True, "the fixture had a pump to lose.");
                Assert.That(after.ContainsKey("pump"), Is.False, "which is now on the deck.");
                Assert.That(after.ContainsKey("posbrain"), Is.True,
                    "the brain never comes out, positronic or otherwise.");
                Assert.That(entities.GetComponent<BloodstreamComponent>(body).BloodReagent, Is.EqualTo("WFWolfmedHydraulicFluid"),
                    "so what it leaks is hydraulic fluid, not blood: the spill is the body's own reagent.");
            });

            // The mechanical repair: seat the plating, then weld the seam.
            Step(entities, "WFSurgeryStepSeatChassisPlating", body, torso);
            Step(entities, "WFSurgeryStepWeldChassisBreach", body, torso);

            Assert.Multiple(() =>
            {
                Assert.That(FindWound(entities, wounds, torso, "WFWolfmedChassisBreachWound"), Is.Null);
                Assert.That(FindWound(entities, wounds, torso, "WFWolfmedBreachWound"), Is.Not.Null,
                    "welded shut leaves the ordinary hole in the casing.");
            });
        });
    }

    /// <summary>An eviscerated body still gibs and still deletes, with its organs already gone.</summary>
    [Test]
    public async Task GibbingAnOpenBodyDoesNotThrowTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var gibbed = Patient(entities, map.GridCoords, out _);
            var deleted = Patient(entities, map.GridCoords, out _);
            Forced(entities, 1f);

            foreach (var body in new[] { gibbed, deleted })
            {
                Cut(entities, body, Cap);
                Cut(entities, body, BigSlash);
            }

            Assert.DoesNotThrow(() => entities.System<SharedBodySystem>().GibBody(gibbed, gibOrgans: true));
            Assert.DoesNotThrow(() => entities.DeleteEntity(deleted));
        });
    }

    /// <summary>The two wounds name themselves and their surgeries exist.</summary>
    [Test]
    public async Task NamesAndSurgeriesExistTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(locale.HasString("wolfmed-wound-name-evisceration"));
                Assert.That(locale.HasString("wolfmed-wound-name-chassis-breach"));
                Assert.That(locale.HasString("wolfmed-evisceration-popup"));
                Assert.That(locale.HasString("wolfmed-chassis-breach-popup"));
                Assert.That(prototypes.HasIndex<EntityPrototype>("WFSurgeryCloseEvisceration"));
                Assert.That(prototypes.HasIndex<EntityPrototype>("WFSurgeryWeldChassisBreach"));
                Assert.That(prototypes.HasIndex<WolfmedEviscerationProfilePrototype>("WFWolfmedEviscerationDefault"));
            });
        });
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>A human whose thresholds are lifted out of the way, so a capped torso is still a live patient.</summary>
    private static EntityUid Patient(IEntityManager entities, EntityCoordinates where, out EntityUid torso)
    {
        var body = entities.SpawnEntity("MobHuman", where);
        Raise(entities, body);
        torso = Part(entities, body, BodyPartType.Torso);
        return body;
    }

    /// <summary>
    /// 250 Slash on a torso is far past what kills a human, and the feature is specified to work on a
    /// living one, so the thresholds move rather than the damage.
    /// </summary>
    private static void Raise(IEntityManager entities, EntityUid body)
    {
        var thresholds = entities.System<MobThresholdSystem>();
        thresholds.SetMobStateThreshold(body, FixedPoint2.New(4000), MobState.Critical);
        thresholds.SetMobStateThreshold(body, FixedPoint2.New(5000), MobState.Dead);
    }

    private static void Forced(IEntityManager entities, float roll) =>
        entities.System<WolfmedEviscerationSystem>().ForcedRoll = roll;

    /// <summary>
    /// A cut on the torso with somebody behind it, as a weapon would be: damage with no origin is ambient harm
    /// and goes through the M1b ceilings.
    /// </summary>
    private static void Cut(IEntityManager entities, EntityUid body, int amount) =>
        Hit(entities, body, "Slash", amount);

    private static void Hit(IEntityManager entities, EntityUid body, string type, int amount)
    {
        entities.System<DamageableSystem>().TryChangeDamage(body, Spec(type, amount),
            ignoreResistances: true, origin: body, targetPart: TargetBodyPart.Torso);
    }

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .First(part => part.Component.PartType == type)
            .Id;
    }

    /// <summary>Slot id to organ, for the organs still inside the part.</summary>
    private static Dictionary<string, EntityUid> Organs(IEntityManager entities, EntityUid part)
    {
        var result = new Dictionary<string, EntityUid>();
        foreach (var (organ, comp) in entities.System<SharedBodySystem>().GetPartOrgans(part))
        {
            if (comp.SlotId is { } slot)
                result[slot] = organ;
        }

        return result;
    }

    private static EntityUid? FindWound(IEntityManager entities, WoundSystem wounds, EntityUid part, string prototype)
    {
        if (!entities.TryGetComponent(part, out WoundableComponent? woundable))
            return null;

        foreach (var wound in wounds.GetWounds((part, woundable)))
        {
            if (wound.Comp.Prototype == new ProtoId<WoundPrototype>(prototype))
                return wound.Owner;
        }

        return null;
    }

    /// <summary>
    /// One shipped surgery step, driven the way WolfmedWoundSurgeryTest drives one: the do-after, the BUI
    /// and the tool check are all bypassed and the effect event raised on the step entity itself.
    /// </summary>
    private static void Step(IEntityManager entities, string prototype, EntityUid body, EntityUid part)
    {
        var step = entities.System<Content.Server._Shitmed.Medical.Surgery.SurgerySystem>().GetSingleton(prototype);
        Assert.That(step, Is.Not.Null, $"{prototype} has no singleton.");
        var ev = new SurgeryStepEvent(body, body, part, new List<EntityUid>(), step!.Value);
        entities.EventBus.RaiseLocalEvent(step.Value, ref ev);
    }

    private static bool StepComplete(IEntityManager entities, string prototype, EntityUid body, EntityUid part)
    {
        var surgery = entities.System<Content.Server._Shitmed.Medical.Surgery.SurgerySystem>();
        var step = surgery.GetSingleton(prototype);
        Assert.That(step, Is.Not.Null, $"{prototype} has no singleton.");
        return surgery.IsStepComplete(body, part, prototype, step!.Value);
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
