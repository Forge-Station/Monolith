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
                Assert.That(prototypes.Index<EntityPrototype>(id).TryGetComponent<WFSoftShieldComponent>(out _, factory), Is.True, id);
        });

        await pair.CleanReturnAsync();
    }
}
