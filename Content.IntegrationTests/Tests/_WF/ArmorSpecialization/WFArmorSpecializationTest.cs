using System.Collections.Generic;
using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.ArmorSpecialization;
using Content.Shared._WF.SoftShield;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.ArmorSpecialization;

/// <summary>Checks each armour specialization does its job through the real Wolfmed paths.</summary>
[TestFixture]
public sealed class WFArmorSpecializationTest
{
    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type,
        BodyPartSymmetry symmetry = BodyPartSymmetry.None)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .First(p => p.Component.PartType == type && (symmetry == BodyPartSymmetry.None || p.Component.Symmetry == symmetry)).Id;
    }

    private static EntityUid Wear(IEntityManager entities, MapCoordinates coords, string suit)
    {
        var human = entities.SpawnEntity("MobHuman", coords);
        var item = entities.SpawnEntity(suit, coords);
        Assert.That(entities.System<InventorySystem>().TryEquip(human, item, "outerClothing", silent: true, force: true), Is.True, suit);
        return human;
    }

    private static bool HasWound(IEntityManager entities, EntityUid body, string wound)
    {
        var wounds = entities.System<WoundSystem>();
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Any(p => wounds.GetWounds(p.Id).Any(w => w.Comp.Prototype == wound));
    }

    [Test]
    public async Task SpallLinerStopsLodgedRounds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var rules = entities.System<WolfmedWoundRuleSystem>();

        await server.WaitAssertion(() =>
        {
            entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            var bare = entities.SpawnEntity("MobHuman", coords);
            var lined = Wear(entities, coords, "ClothingOuterHardsuitAshen");

            rules.ForcedRoll = 0f;
            try
            {
                foreach (var body in new[] { bare, lined })
                {
                    var bullet = entities.SpawnEntity("Bullet9x19mmFMJ", coords);
                    var hit = new DamageSpecifier();
                    hit.DamageDict["Piercing"] = FixedPoint2.New(60);
                    entities.System<DamageableSystem>().TryChangeDamage(body, hit, origin: bare, tool: bullet,
                        targetPart: TargetBodyPart.Torso);
                }
            }
            finally
            {
                rules.ForcedRoll = null;
            }

            Assert.That(HasWound(entities, bare, "WFWolfmedLodgedRoundWound"), Is.True, "Without a liner the round lodges.");
            Assert.That(HasWound(entities, lined, "WFWolfmedLodgedRoundWound"), Is.False, "The CV-32's spall liner stops it.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WardsAnswerThroughTheRelay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);

            var plated = Wear(entities, coords, "ClothingOuterHardsuitM86");
            var crush = new WolfmedWoundRuleAttemptEvent(Part(entities, plated, BodyPartType.Torso),
                "WFWolfmedCrushInjuryWound", FixedPoint2.New(40));
            entities.EventBus.RaiseLocalEvent(plated, ref crush);
            Assert.That(crush.Cancelled, Is.False);
            Assert.That(crush.MinDamageMultiplier, Is.EqualTo(2f), "The M86's bone plating doubles the crush threshold.");

            var commissar = Wear(entities, coords, "ClothingOuterHardsuitOfficerCombat");
            var concussion = new WolfmedWoundRuleAttemptEvent(Part(entities, commissar, BodyPartType.Head),
                "WFWolfmedConcussionWound", FixedPoint2.New(80));
            entities.EventBus.RaiseLocalEvent(commissar, ref concussion);
            Assert.That(concussion.Cancelled, Is.True, "The commissar can't be concussed.");

            var bare = entities.SpawnEntity("MobHuman", coords);
            var untouched = new WolfmedWoundRuleAttemptEvent(Part(entities, bare, BodyPartType.Head),
                "WFWolfmedConcussionWound", FixedPoint2.New(80));
            entities.EventBus.RaiseLocalEvent(bare, ref untouched);
            Assert.That(untouched.Cancelled, Is.False);
            Assert.That(untouched.MinDamageMultiplier, Is.EqualTo(1f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LimbLockKeepsLimbsOn()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);

            bool Severs(EntityUid body, float hit)
            {
                var ev = new WolfmedSeverAttemptEvent(Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left), FixedPoint2.New(hit));
                entities.EventBus.RaiseLocalEvent(body, ref ev);
                return !ev.Cancelled;
            }

            var juggernaut = Wear(entities, coords, "ClothingOuterHardsuitJuggernaut");
            Assert.That(Severs(juggernaut, 500), Is.False, "Nothing takes a Juggernaut's limb.");

            var basilisk = Wear(entities, coords, "ClothingOuterHardsuitTarkhan");
            Assert.That(Severs(basilisk, 30), Is.False, "The CV-280 holds against a 30-damage hit.");
            Assert.That(Severs(basilisk, 45), Is.True, "A 45-damage hit breaks through its 40 limit.");

            Assert.That(Severs(entities.SpawnEntity("MobHuman", coords), 30), Is.True, "Without armour the limb goes.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PainDampenerAddsRelief()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            var relief = entities.System<WolfmedPainReliefSystem>();

            var scout = Wear(entities, coords, "ClothingOuterHardsuitUsspM10");
            var bare = entities.SpawnEntity("MobHuman", coords);
            Assert.That(relief.GetRelief(scout) - relief.GetRelief(bare), Is.EqualTo(15f).Within(0.01f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AblativeCoatSoaksBurns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            var human = Wear(entities, coords, "ClothingOuterHardsuitPirateScaf");
            var suit = entities.System<InventorySystem>().TryGetSlotEntity(human, "outerClothing", out var worn) ? worn!.Value : default;
            var coat = entities.GetComponent<WFAblativeCoatComponent>(suit);
            var before = coat.Charge;

            var brute = new DamageSpecifier();
            brute.DamageDict["Blunt"] = FixedPoint2.New(20);
            entities.System<DamageableSystem>().TryChangeDamage(human, brute, origin: human);
            Assert.That(coat.Charge, Is.EqualTo(before).Within(0.01f), "Brute passes the coat untouched.");

            var burn = new DamageSpecifier();
            burn.DamageDict["Heat"] = FixedPoint2.New(20);
            entities.System<DamageableSystem>().TryChangeDamage(human, burn, origin: human);
            Assert.That(coat.Charge, Is.EqualTo(before - 10f).Within(0.5f), "The coat soaks half of a burn.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AutoTourniquetClampsArteries()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        EntityUid human = default, suit = default, artery = default;
        await server.WaitAssertion(() =>
        {
            entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            human = Wear(entities, coords, "ClothingOuterHardsuitPDVMedic");
            entities.System<InventorySystem>().TryGetSlotEntity(human, "outerClothing", out var worn);
            suit = worn!.Value;
            var arm = Part(entities, human, BodyPartType.Arm, BodyPartSymmetry.Left);
            artery = entities.System<WoundSystem>().CreateOrMergeWound(arm, "WFWolfmedArterialBleedWound", 30)!.Value;
            Assert.That(entities.HasComponent<WoundBleedingComponent>(artery), Is.True, "The artery bleeds.");
        });

        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<WoundBleedingComponent>(artery).Treatment, Is.EqualTo(BleedingTreatment.Clamped),
                "The CV-82 clamps the artery by itself.");
            Assert.That(entities.GetComponent<WFAutoTourniquetComponent>(suit).Charges, Is.EqualTo(1));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LastHardShieldsAreSoft()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            foreach (var id in new[] { "ClothingOuterHardsuitViperGroupShield", "ClothingOuterHardsuitUIEnforcerMKII" })
                Assert.That(prototypes.Index<EntityPrototype>(id).TryComp<WFSoftShieldComponent>(out _, factory), Is.True, id);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ImpactDampersRefuseOnlyThrownDislocations()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var human = Wear(entities, new MapCoordinates(0, 0, mapId), "ClothingOuterHardsuitSpatio");
            var arm = Part(entities, human, BodyPartType.Arm, BodyPartSymmetry.Left);

            var thrown = new WolfmedWoundRuleAttemptEvent(arm, "WFWolfmedDislocationWound", FixedPoint2.New(30))
                { Rule = "WFWolfmedRuleDislocationThrown" };
            entities.EventBus.RaiseLocalEvent(human, ref thrown);
            Assert.That(thrown.Cancelled, Is.True, "A throw can't dislocate the spationaut's joints.");

            var punched = new WolfmedWoundRuleAttemptEvent(arm, "WFWolfmedDislocationWound", FixedPoint2.New(30))
                { Rule = "WFWolfmedRuleDislocation" };
            entities.EventBus.RaiseLocalEvent(human, ref punched);
            Assert.That(punched.Cancelled, Is.False, "An ordinary blow still can.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FireRatedSoaksOnlyHeat()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var human = Wear(entities, new MapCoordinates(0, 0, mapId), "ClothingOuterHardsuitAtmos");
            entities.System<InventorySystem>().TryGetSlotEntity(human, "outerClothing", out var suit);
            var coat = entities.GetComponent<WFAblativeCoatComponent>(suit!.Value);
            var full = coat.Charge;

            var shock = new DamageSpecifier();
            shock.DamageDict["Shock"] = FixedPoint2.New(20);
            entities.System<DamageableSystem>().TryChangeDamage(human, shock, origin: human);
            Assert.That(coat.Charge, Is.EqualTo(full).Within(0.01f), "Shock passes a fire-rated lining.");

            var heat = new DamageSpecifier();
            heat.DamageDict["Heat"] = FixedPoint2.New(20);
            entities.System<DamageableSystem>().TryChangeDamage(human, heat, origin: human);
            Assert.That(coat.Charge, Is.EqualTo(full - 10f).Within(0.5f), "It soaks half of the heat.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OreSenseShowsOre()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var miner = Wear(entities, new MapCoordinates(0, 0, mapId), "ClothingOuterHardsuitSalvage");
            Assert.That(entities.HasComponent<Content.Shared.Mining.Components.MiningScannerViewerComponent>(miner), Is.True,
                "The mining hardsuit's built-in scanner shows ore to its wearer.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PlasmaReserveFeedsOnlyThePatient()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var bloodstream = entities.System<Content.Server.Body.Systems.BloodstreamSystem>();
        var reserves = entities.System<Content.Server._WF.ArmorSpecialization.WFPlasmaReserveSystem>();
        EntityUid medic = default, patient = default, suit = default, pack = default;

        await server.WaitAssertion(() =>
        {
            entities.System<SharedMapSystem>().CreateMap(out var mapId);
            medic = Wear(entities, new MapCoordinates(0, 0, mapId), "ClothingOuterHardsuitVoidParamed");
            patient = entities.SpawnEntity("MobHuman", new MapCoordinates(0.5f, 0, mapId));
            pack = entities.SpawnEntity("Bloodpack", new MapCoordinates(0, 0, mapId));
            Assert.That(entities.System<InventorySystem>().TryGetSlotEntity(medic, "outerClothing", out var worn), Is.True);
            suit = worn!.Value;

            bloodstream.TryModifyBloodLevel(patient, FixedPoint2.New(-150));
            bloodstream.TryModifyBloodLevel(medic, FixedPoint2.New(-150));
            reserves.Connect((suit, entities.GetComponent<WFPlasmaReserveComponent>(suit)), patient, medic);
        });

        var patientBefore = 0f;
        await server.WaitPost(() =>
        {
            patientBefore = bloodstream.GetBloodLevelPercentage(patient);
            Assert.That(entities.GetComponent<Content.Shared.Physics.JointVisualsComponent>(suit).Target,
                Is.EqualTo(entities.GetNetEntity(patient)), "The line is drawn from the suit to the patient.");
            Assert.That(entities.HasComponent<Content.Server._WF.Wolfmed.Medical.WolfmedIvLinedComponent>(patient), Is.True,
                "The patient gets the IV Connected alert.");
        });
        await server.WaitRunTicks(150); // 5 seconds at the pool's 30 tick/s rate.

        await server.WaitAssertion(() =>
        {
            var reserve = entities.GetComponent<WFPlasmaReserveComponent>(suit);
            Assert.That(reserve.MaxReserve, Is.EqualTo(90f));
            Assert.That(reserve.Reserve, Is.LessThan(reserve.MaxReserve - 10f), "The line draws on it.");
            Assert.That(reserve.Reserve, Is.GreaterThan(reserve.MaxReserve - 20f), "Only the line draws on it, not the low wearer.");
            Assert.That(bloodstream.GetBloodLevelPercentage(patient), Is.GreaterThan(patientBefore), "The patient on the line is fed.");
            Assert.That(reserves.Give((suit, reserve), medic, 10f), Is.EqualTo(0f), "The suit never feeds its own wearer.");

            // Walking off pulls the line free.
            var xform = entities.System<SharedTransformSystem>();
            xform.SetWorldPosition(patient, new System.Numerics.Vector2(5f, 0f));
        });
        await server.WaitRunTicks(45);

        await server.WaitAssertion(() =>
        {
            var reserve = entities.GetComponent<WFPlasmaReserveComponent>(suit);
            Assert.That(reserve.Patient, Is.Null, "The line breaks past a tile and a half.");
            Assert.That(entities.HasComponent<Content.Shared.Physics.JointVisualsComponent>(suit), Is.False, "The drawn line goes with it.");
            Assert.That(entities.HasComponent<Content.Server._WF.Wolfmed.Medical.WolfmedIvLinedComponent>(patient), Is.False,
                "So does the alert.");

            // A blood pack used on the suit opens into the reserve.
            var before = reserve.Reserve;
            var packs = entities.GetComponent<Content.Shared.Stacks.StackComponent>(pack).Count;
            entities.System<Content.Shared.Interaction.SharedInteractionSystem>().InteractUsing(medic, pack, suit,
                entities.GetComponent<TransformComponent>(medic).Coordinates, checkCanInteract: false, checkCanUse: false);
            Assert.That(reserve.Reserve, Is.GreaterThan(before), "The pack refills the reserve.");
            Assert.That(entities.GetComponent<Content.Shared.Stacks.StackComponent>(pack).Count, Is.EqualTo(packs - 1));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CivilianPassivesAnswerThroughTheRelay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            var bare = entities.SpawnEntity("MobHuman", coords);

            var paramedic = Wear(entities, coords, "ClothingOuterHardsuitVoidParamed");
            var steady = new WolfmedSteadyHandsEvent();
            entities.EventBus.RaiseLocalEvent(paramedic, ref steady);
            Assert.That(steady.Multiplier, Is.EqualTo(3f), "The paramedic's steady hands triple the interrupt line.");
            var unsteady = new WolfmedSteadyHandsEvent();
            entities.EventBus.RaiseLocalEvent(bare, ref unsteady);
            Assert.That(unsteady.Multiplier, Is.EqualTo(1f));

            var infection = entities.System<Content.Server._WF.Wolfmed.Wounds.WolfmedInfectionSystem>();
            var medic = Wear(entities, coords, "ClothingOuterEVASuitMedic");
            Assert.That(infection.IsSterile(medic, Part(entities, medic, BodyPartType.Torso)), Is.True,
                "Wounds under the sterile weave can't go bad.");
            Assert.That(infection.IsSterile(medic), Is.True, "The sterile weave's wearer treats clean.");
            Assert.That(infection.IsSterile(bare), Is.False);

            var engineer = Wear(entities, coords, "ClothingOuterHardsuitEngineering");
            var tools = new WFToolSpeedEvent();
            entities.EventBus.RaiseLocalEvent(engineer, ref tools);
            Assert.That(tools.Multiplier, Is.EqualTo(1.25f), "Servo assist speeds tool work.");

            var salvager = Wear(entities, coords, "ClothingOuterHardsuitMaxim");
            var arm = Part(entities, salvager, BodyPartType.Arm, BodyPartSymmetry.Left);
            var blast = new WolfmedSeverAttemptEvent(arm, FixedPoint2.New(200)) { IsExplosion = true };
            entities.EventBus.RaiseLocalEvent(salvager, ref blast);
            Assert.That(blast.Cancelled, Is.True, "The blast liner keeps limbs on through explosions.");
            var chop = new WolfmedSeverAttemptEvent(arm, FixedPoint2.New(200));
            entities.EventBus.RaiseLocalEvent(salvager, ref chop);
            Assert.That(chop.Cancelled, Is.False, "It doesn't stop an ordinary finishing hit.");
            var bruise = new WolfmedWoundRuleAttemptEvent(Part(entities, salvager, BodyPartType.Torso),
                "WFWolfmedOrganContusionWound", FixedPoint2.New(40)) { Cause = WolfmedWoundCause.Explosion };
            entities.EventBus.RaiseLocalEvent(salvager, ref bruise);
            Assert.That(bruise.Cancelled, Is.True, "Explosions can't bruise organs under a blast liner.");

            var chief = Wear(entities, coords, "ClothingOuterHardsuitEngineeringWhite");
            var carried = entities.SpawnEntity("Crowbar", coords);
            entities.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().TryPickupAnyHand(chief, carried);
            var specs = entities.System<WFCivilianSpecializationSystem>();
            Assert.That(specs.ShieldsFromEmp(chief), Is.True, "An EMP-hardened suit shields its wearer.");
            Assert.That(specs.ShieldsFromEmp(carried), Is.True, "It shields what they carry, too.");
            Assert.That(specs.ShieldsFromEmp(bare), Is.False);

            var janitor = Wear(entities, coords, "ClothingOuterEVASuitJanitor");
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var acid = prototypes.Index<Content.Shared.Chemistry.Reagent.ReagentPrototype>("SulfuricAcid");
            var water = prototypes.Index<Content.Shared.Chemistry.Reagent.ReagentPrototype>("Water");
            Assert.That(specs.BlocksTouch(janitor, acid), Is.True, "Decon coating keeps splashes off.");
            Assert.That(specs.BlocksTouch(janitor, water), Is.False, "Water still puts the wearer out.");
            Assert.That(specs.BlocksTouch(bare, acid), Is.False);

            var mime = Wear(entities, coords, "ClothingOuterHardsuitMime");
            var noise = new WolfmedPainNoiseAttemptEvent();
            entities.EventBus.RaiseLocalEvent(mime, ref noise);
            Assert.That(noise.Cancelled, Is.True, "The mime makes no pain noises.");
            Assert.That(entities.System<InventorySystem>().TryGetSlotEntity(mime, "outerClothing", out var mimeSuit), Is.True);
            Assert.That(entities.HasComponent<Content.Shared.Slippery.NoSlipComponent>(mimeSuit), Is.False,
                "The mime no longer inherits the clown's treads.");
            Assert.That(entities.HasComponent<WFWoundWardComponent>(mimeSuit), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RadSinkAndWildlifePaddingCutDamage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            var damageable = entities.System<DamageableSystem>();

            var radiation = new DamageSpecifier { DamageDict = { ["Radiation"] = FixedPoint2.New(20) } };
            // The same suit with its rad sink pulled, so only the sink differs.
            var plain = Wear(entities, coords, "ClothingOuterHardsuitEngineering");
            Assert.That(entities.System<InventorySystem>().TryGetSlotEntity(plain, "outerClothing", out var plainSuit), Is.True);
            entities.RemoveComponent<WFRadSinkComponent>(plainSuit!.Value);
            var engineer = Wear(entities, coords, "ClothingOuterHardsuitEngineering");
            var unsunk = damageable.TryChangeDamage(plain, radiation)?.DamageDict.GetValueOrDefault("Radiation") ?? FixedPoint2.Zero;
            var sunk = damageable.TryChangeDamage(engineer, radiation)?.DamageDict.GetValueOrDefault("Radiation") ?? FixedPoint2.Zero;
            Assert.That(unsunk, Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(sunk.Float(), Is.EqualTo(unsunk.Float() * 0.5f).Within(0.2f), "The rad sink takes half.");

            var blunt = new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(20) } };
            var carp = entities.SpawnEntity("MobCarp", coords);
            var person = entities.SpawnEntity("MobHuman", coords);
            var byPerson = damageable.TryChangeDamage(Wear(entities, coords, "ClothingOuterHardsuitGoliath"), blunt, origin: person)
                ?.GetTotal() ?? FixedPoint2.Zero;
            var byCarp = damageable.TryChangeDamage(Wear(entities, coords, "ClothingOuterHardsuitGoliath"), blunt, origin: carp)
                ?.GetTotal() ?? FixedPoint2.Zero;
            Assert.That(byPerson, Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(byCarp.Float(), Is.EqualTo(byPerson.Float() * 0.4f).Within(0.5f),
                "The Goliath's wildlife padding takes 60% off a creature's hit, not a person's.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StasisWeaveHoldsOnCrit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var now = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();

        await server.WaitAssertion(() =>
        {
            entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var cmo = Wear(entities, new MapCoordinates(0, 0, mapId), "ClothingOuterHardsuitMedical");
            Assert.That(entities.System<InventorySystem>().TryGetSlotEntity(cmo, "outerClothing", out var suit), Is.True);
            var weave = (suit!.Value, entities.GetComponent<WFStasisWeaveComponent>(suit.Value));
            var upkeep = entities.System<Content.Server._WF.ArmorSpecialization.WFSuitUpkeepSystem>();

            Assert.That(upkeep.TryFireWeave(weave, cmo, now.CurTime), Is.False, "A conscious wearer doesn't trigger it.");

            // Forced here; Wolfmed would put the body back on its next tick.
            entities.System<Content.Shared.Mobs.Systems.MobStateSystem>().ChangeMobState(cmo, Content.Shared.Mobs.MobState.Critical);
            Assert.That(upkeep.TryFireWeave(weave, cmo, now.CurTime), Is.True, "Going critical fires the weave.");
            Assert.That(entities.GetComponent<Content.Server._WF.Wolfmed.Stasis.WolfmedStasisHoldComponent>(cmo).HeldUntil,
                Is.Not.Null, "It holds the bleeding.");
            Assert.That(weave.Item2.NextReady, Is.GreaterThan(now.CurTime), "It then recharges.");

            entities.RemoveComponent<Content.Server._WF.Wolfmed.Stasis.WolfmedStasisHoldComponent>(cmo);
            Assert.That(upkeep.TryFireWeave(weave, cmo, now.CurTime), Is.False, "A spent weave can't fire again yet.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AntiSlipTreadsWorkOnSuits()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);

            foreach (var suit in new[] { "ClothingOuterHardsuitClown", "ClothingOuterHardsuitERTJanitor" })
            {
                var treaded = new Content.Shared.Slippery.SlipAttemptEvent();
                entities.EventBus.RaiseLocalEvent(Wear(entities, coords, suit), treaded);
                Assert.That(treaded.NoSlip, Is.True, suit);
            }

            var bare = new Content.Shared.Slippery.SlipAttemptEvent();
            entities.EventBus.RaiseLocalEvent(entities.SpawnEntity("MobHuman", coords), bare);
            Assert.That(bare.NoSlip, Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BlastLinerKeepsExplosionsOffOrgans()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            var routing = entities.System<WoundDamageRoutingSystem>();
            var body = entities.System<SharedBodySystem>();

            FixedPoint2 OrganHealth(EntityUid human)
            {
                var health = FixedPoint2.Zero;
                foreach (var (organ, _) in body.GetPartOrgans(Part(entities, human, BodyPartType.Torso)))
                {
                    if (entities.TryGetComponent<Content.Shared._WF.Wolfmed.Body.WolfmedOrganComponent>(organ, out var data))
                        health += data.Health;
                }

                return health;
            }

            FixedPoint2 OrganLoss(EntityUid human, bool explosion)
            {
                var before = OrganHealth(human);
                var blast = new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(120) } };
                routing.TryRouteDistributedDamage(human, blast, TargetBodyPart.Torso,
                    Content.Shared._Onyx.Targeting.DamageDistribution.SplitEvenly, ignoreResistances: true,
                    variation: 0f, isExplosion: explosion);
                return before - OrganHealth(human);
            }

            Assert.That(OrganLoss(entities.SpawnEntity("MobHuman", coords), true), Is.GreaterThan(FixedPoint2.Zero),
                "A blast reaches a bare body's organs.");
            Assert.That(OrganLoss(Wear(entities, coords, "ClothingOuterHardsuitMaxim"), true), Is.EqualTo(FixedPoint2.Zero),
                "A blast liner keeps it off the organs.");
            Assert.That(OrganLoss(Wear(entities, coords, "ClothingOuterHardsuitMaxim"), false), Is.GreaterThan(FixedPoint2.Zero),
                "It doesn't stop an ordinary blow.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ServoAssistSpeedsPrying()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            entities.System<SharedMapSystem>().CreateMap(out var mapId);

            // Each user gets a door of their own, a step away so it can't obstruct them. An unpowered door otherwise pries
            // instantly, leaving no do-after to measure.
            double PryDelay(EntityUid user, float y)
            {
                var crowbar = entities.SpawnEntity("Crowbar", new MapCoordinates(0, y, mapId));
                Assert.That(entities.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().TryPickupAnyHand(user, crowbar), Is.True);
                var door = entities.SpawnEntity("Airlock", new MapCoordinates(0.8f, y, mapId));
                entities.GetComponent<Content.Shared.Prying.Components.PryingComponent>(crowbar).InstaPry = false;
                Assert.That(entities.System<Content.Shared.Prying.Systems.PryingSystem>().TryPry(door, user, out _, crowbar), Is.True);
                var doAfters = entities.GetComponent<Content.Shared.DoAfter.DoAfterComponent>(user).DoAfters;
                Assert.That(doAfters, Is.Not.Empty, "The pry started.");
                return doAfters.Values.First().Args.Delay.TotalSeconds;
            }

            var plain = PryDelay(entities.SpawnEntity("MobHuman", new MapCoordinates(0, 0, mapId)), 0f);
            var servo = PryDelay(Wear(entities, new MapCoordinates(0, 5, mapId), "ClothingOuterHardsuitEngineering"), 5f);
            Assert.That(servo, Is.EqualTo(plain / 1.25).Within(0.01), "Servo assist speeds prying like other tool work.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EmptyPlasmaReserveTakesTheLineOut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var reserves = entities.System<Content.Server._WF.ArmorSpecialization.WFPlasmaReserveSystem>();
        EntityUid patient = default, suit = default;

        await server.WaitAssertion(() =>
        {
            entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var medic = Wear(entities, new MapCoordinates(0, 0, mapId), "ClothingOuterHardsuitVoidParamed");
            patient = entities.SpawnEntity("MobHuman", new MapCoordinates(0.5f, 0, mapId));
            Assert.That(entities.System<InventorySystem>().TryGetSlotEntity(medic, "outerClothing", out var worn), Is.True);
            suit = worn!.Value;

            entities.System<Content.Server.Body.Systems.BloodstreamSystem>().TryModifyBloodLevel(patient, FixedPoint2.New(-150));
            var reserve = entities.GetComponent<WFPlasmaReserveComponent>(suit);
            reserve.Reserve = 1f;
            reserves.Connect((suit, reserve), patient, medic);
        });

        await server.WaitRunTicks(90); // 3 seconds at the pool's 30 tick/s rate.

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<WFPlasmaReserveComponent>(suit).Patient, Is.Null, "An empty reserve drops the line.");
            Assert.That(entities.HasComponent<Content.Shared.Physics.JointVisualsComponent>(suit), Is.False);
            Assert.That(entities.HasComponent<Content.Server._WF.Wolfmed.Medical.WolfmedIvLinedComponent>(patient), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HaulFrameAndThrusters()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            var speed = entities.System<Content.Shared.Movement.Systems.MovementSpeedModifierSystem>();

            var hauler = Wear(entities, coords, "ClothingOuterHardsuitGoliath");
            speed.RefreshMovementSpeedModifiers(hauler);
            var suitOnly = entities.GetComponent<Content.Shared.Movement.Components.MovementSpeedModifierComponent>(hauler)
                .WalkSpeedModifier;
            var crate = entities.SpawnEntity("CrateGenericSteel", coords);
            Assert.That(entities.System<Content.Shared.Movement.Pulling.Systems.PullingSystem>().TryStartPull(hauler, crate),
                Is.True);
            speed.RefreshMovementSpeedModifiers(hauler);
            Assert.That(entities.GetComponent<Content.Shared.Movement.Components.MovementSpeedModifierComponent>(hauler)
                .WalkSpeedModifier, Is.EqualTo(suitOnly).Within(0.001f), "The haul frame ignores the pull.");

            // The spationaut's thrusters push in zero gravity; this map has no grid.
            var spationaut = Wear(entities, coords, "ClothingOuterHardsuitSpatio");
            Assert.That(entities.System<InventorySystem>().TryGetSlotEntity(spationaut, "outerClothing", out var suit), Is.True);
            Assert.That(entities.GetComponent<WFSuitThrustersComponent>(suit!.Value).ActionEntity, Is.Not.Null,
                "Wearing the suit grants the action.");
            var burst = new WFSuitThrustersEvent { Performer = spationaut };
            entities.EventBus.RaiseLocalEvent(suit.Value, burst);
            Assert.That(burst.Handled, Is.True);
            Assert.That(entities.GetComponent<Robust.Shared.Physics.Components.PhysicsComponent>(spationaut).LinearVelocity.Length(),
                Is.GreaterThan(1f), "The burst pushes the wearer.");
        });

        await pair.CleanReturnAsync();
    }
}
