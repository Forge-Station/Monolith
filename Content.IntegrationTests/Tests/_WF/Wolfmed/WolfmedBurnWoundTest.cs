#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Server.Medical.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// W4: what heat, cold, acid and current each leave behind, and what it takes to get rid of it. Charring
/// comes off the burn's top stage, frostbite numbs the part it freezes, a chemical burn keeps working
/// until the patient is rinsed, and a shock reaches inside.
/// </summary>
/// <remarks>
/// The wounds and their thresholds are data (<c>_WF/Wolfmed/Wounds/burns.yml</c>, <c>cautery.yml</c> and
/// the rules). The new behaviour is <see cref="WolfmedCharringSystem"/>, <see cref="WolfmedCauterySystem"/>,
/// <see cref="WolfmedFrostbiteSystem"/>, <see cref="WolfmedChemicalBurnSystem"/> and
/// <see cref="WolfmedElectricalBurnSystem"/>. Damage goes to the torso wherever a limb would be at risk of
/// coming off before the wound under test appears.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedCauterySystem))]
public sealed class WolfmedBurnWoundTest : GameTest
{
    /// <summary>
    /// A burn that reaches its critical stage leaves charred tissue, which nothing in a medkit reaches and
    /// which the graft surgery is the only exit from.
    /// </summary>
    [Test]
    public async Task CharringComesOffTheBurnsTopStageTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var healing = entities.System<WoundHealingSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(entities, body, BodyPartType.Torso);

            // Two moderate burns: 60 severity is short of the critical stage at 80.
            Damage(entities, body, TargetBodyPart.Torso, "Heat", 30);
            Damage(entities, body, TargetBodyPart.Torso, "Heat", 30);
            Assert.That(Prototypes(entities, wounds, torso), Does.Not.Contain("WFWolfmedCharringWound"),
                "a burn short of its top stage has not killed anything yet.");

            // The third crosses it.
            Damage(entities, body, TargetBodyPart.Torso, "Heat", 30);
            Assert.That(Prototypes(entities, wounds, torso), Does.Contain("WFWolfmedCharringWound"));

            var charring = FindWound(entities, wounds, torso, "WFWolfmedCharringWound");
            var severity = entities.GetComponent<WoundComponent>(charring).Severity;

            // Staying in the top stage does not char again; the crossing is what counts.
            Damage(entities, body, TargetBodyPart.Torso, "Heat", 30);
            Assert.That(entities.GetComponent<WoundComponent>(charring).Severity, Is.EqualTo(severity));

            foreach (var item in new[] { "Ointment1", "RegenerativeMesh" })
            {
                var topical = entities.SpawnEntity(item, map.GridCoords);
                healing.TryApplyHealing(body, torso,
                    (topical, entities.GetComponent<HealingComponent>(topical)), body, out _, out _);
            }

            Assert.That(entities.GetComponent<WoundComponent>(charring).Severity, Is.EqualTo(severity),
                "no damage type means no topical and no damage removal can reach dead tissue.");

            // The graft step treats it outright, which is the whole of the surgery's effect.
            Assert.Multiple(() =>
            {
                Assert.That(prototypes.HasIndex<EntityPrototype>("WFWolfmedSkinGraft"));
                Assert.That(prototypes.HasIndex<EntityPrototype>("WFSurgeryStepGraftSkin"));
                Assert.That(prototypes.HasIndex<EntityPrototype>("WFSurgeryGraftSkin"));
                Assert.That(prototypes.Index<WoundPrototype>("WFWolfmedCharringWound").DamageTypes, Is.Empty);
            });

            wounds.TreatWound(charring, FixedPoint2.MaxValue);
            Assert.That(Prototypes(entities, wounds, torso), Does.Not.Contain("WFWolfmedCharringWound"));
        });
    }

    /// <summary>
    /// M1b (plan §6.3): a burn at its maximum does not drop further Heat. It escalates, one charring point per
    /// point of Heat (wolfmed.char_escalation 1), up to the charring's own maximum.
    /// </summary>
    [Test]
    public async Task BurnAtItsCapEscalatesIntoCharringTest()
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

            for (var i = 0; i < 7; i++)
                Damage(entities, body, TargetBodyPart.Torso, "Heat", 30);

            var burn = FindWound(entities, wounds, torso, "BurnWound");
            var charring = FindWound(entities, wounds, torso, "WFWolfmedCharringWound");
            Assert.That(entities.GetComponent<WoundComponent>(burn).Severity, Is.EqualTo(FixedPoint2.New(200)),
                "the burn is not at its maximum, so the test proves nothing.");
            var before = entities.GetComponent<WoundComponent>(charring).Severity;

            Damage(entities, body, TargetBodyPart.Torso, "Heat", 30);
            Assert.That(entities.GetComponent<WoundComponent>(charring).Severity, Is.EqualTo(before + 30),
                "Heat past a burn's maximum did not escalate into charring.");

            for (var i = 0; i < 5; i++)
                Damage(entities, body, TargetBodyPart.Torso, "Heat", 30);
            Assert.That(entities.GetComponent<WoundComponent>(charring).Severity, Is.EqualTo(FixedPoint2.New(120)),
                "charring went past its own maximum.");
        });
    }

    /// <summary>
    /// Heat seals bleeding. An ordinary cut closes to a stray hot hit; a severed artery does not, and
    /// needs someone holding the tool against it on purpose.
    /// </summary>
    [Test]
    public async Task HeatSealsBleedingWoundsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var cautery = entities.System<WolfmedCauterySystem>();

            // Arterial bleeds are the case this system owns: Wolfgate's bloodloss modifier set already
            // takes ordinary bleeding severity off a heat hit, and W2 exempts arteries from that path.
            // 24 Slash at the rule's 0.5 is severity 12, the most a stray hot hit can close.
            var nicked = entities.SpawnEntity("MobHuman", map.GridCoords);
            Damage(entities, nicked, TargetBodyPart.Torso, "Slash", 24);

            var nickedTorso = Part(entities, nicked, BodyPartType.Torso);
            var nick = FindWound(entities, wounds, nickedTorso, "WFWolfmedArterialBleedWound");
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<WoundComponent>(nick).Severity, Is.EqualTo(FixedPoint2.New(12)));
                Assert.That(Rate(entities, nick), Is.GreaterThan(0f), "an opened artery pumps.");
            });

            // Below the 8-Heat floor nothing is hot enough for long enough.
            Damage(entities, nicked, TargetBodyPart.Torso, "Heat", 4);
            Assert.That(Rate(entities, nick), Is.GreaterThan(0f));

            Damage(entities, nicked, TargetBodyPart.Torso, "Heat", 20);
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<WoundBleedingComponent>(nick).Treatment,
                    Is.EqualTo(BleedingTreatment.Cauterized));
                Assert.That(Rate(entities, nick), Is.EqualTo(0f), "a sealed bleed does not bleed.");
                Assert.That(Prototypes(entities, wounds, nickedTorso), Does.Contain("BurnWound"),
                    "and it costs a burn: the searing is charged, not free.");
            });

            // A severed artery: severity 15 (30 Slash) is past the 12 a stray hit can close.
            var severed = entities.SpawnEntity("MobHuman", map.GridCoords);
            Damage(entities, severed, TargetBodyPart.Torso, "Slash", 30);

            var severedTorso = Part(entities, severed, BodyPartType.Torso);
            var artery = FindWound(entities, wounds, severedTorso, "WFWolfmedArterialBleedWound");
            Assert.That(entities.GetComponent<WoundComponent>(artery).Severity, Is.EqualTo(FixedPoint2.New(15)));

            Damage(entities, severed, TargetBodyPart.Torso, "Heat", 20);
            Assert.That(Rate(entities, artery), Is.GreaterThan(0f),
                "a stray laser does not close a severed artery.");

            // Doing it on purpose does, at nearly three times the burn.
            Assert.That(cautery.TryCauterize(severedTorso, deliberate: true), Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(Rate(entities, artery), Is.EqualTo(0f));
                Assert.That(cautery.HasSealableBleed(severedTorso), Is.False);
                Assert.That(cautery.TryCauterize(severedTorso, deliberate: true), Is.Zero,
                    "nothing left to seal.");
            });
        });
    }

    /// <summary>
    /// Cold freezes rather than burns: it replaces the burn wound, numbs the part it is on, and flags the
    /// part for W5 once it is frozen through.
    /// </summary>
    [Test]
    public async Task FrostbiteNumbsAndThenRisksTheLimbTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var traits = entities.System<WolfmedWoundTraitSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(entities, body, BodyPartType.Torso);

            Damage(entities, body, TargetBodyPart.Torso, "Cold", 30);

            Assert.Multiple(() =>
            {
                Assert.That(Prototypes(entities, wounds, torso), Does.Contain("WFWolfmedFrostbiteWound")
                        .And.Not.Contain("BurnWound"),
                    "cold freezes; the frostbite takes the burn wound's place.");
                Assert.That(entities.HasComponent<WolfmedFrostbiteComponent>(torso), Is.True);
                Assert.That(Suppression(entities, torso), Is.GreaterThan(FixedPoint2.Zero),
                    "the part stops reporting what it feels.");
                Assert.That(entities.GetComponent<WolfmedFrostbiteComponent>(torso).NecrosisRisk,
                    Is.EqualTo(0f), "frozen, not yet dying.");
            });

            var moderate = Suppression(entities, torso);

            // Frozen through: the critical stage numbs far more and raises the flag W5 reads.
            Damage(entities, body, TargetBodyPart.Torso, "Cold", 60);
            var frostbite = entities.GetComponent<WolfmedFrostbiteComponent>(torso);

            Assert.Multiple(() =>
            {
                Assert.That(Suppression(entities, torso), Is.GreaterThan(moderate));
                Assert.That(frostbite.NecrosisRisk, Is.GreaterThan(0f));
                Assert.That(frostbite.NecrosisOnset, Is.GreaterThan(TimeSpan.Zero));
                // Review fix: the part's reader is GetPartNecrosisOnset (M6, the onset over the risk).
                Assert.That(traits.GetPartNecrosisOnset(torso),
                    Is.EqualTo(frostbite.NecrosisOnset / frostbite.NecrosisRisk),
                    "and the same figure is readable off the part's wounds.");
            });

            // Thawed out: the wound goes, the flag goes with it, and the numbness is left to fade.
            var wound = FindWound(entities, wounds, torso, "WFWolfmedFrostbiteWound");
            wounds.RemoveWound(wound);
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<WolfmedFrostbiteComponent>(torso), Is.False);
                Assert.That(traits.GetPartNecrosisOnset(torso), Is.EqualTo(TimeSpan.Zero));
            });
        });
    }

    /// <summary>
    /// Acid left on the skin keeps eating the part on its own tick, and healing the burn does nothing
    /// about it. Water does.
    /// </summary>
    [Test]
    public async Task ChemicalBurnsTickUntilWashedTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var residue = entities.System<WolfmedChemicalBurnSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(entities, body, BodyPartType.Torso);

            Damage(entities, body, TargetBodyPart.Torso, "Caustic", 20);

            Assert.Multiple(() =>
            {
                Assert.That(Prototypes(entities, wounds, torso), Does.Contain("WFWolfmedChemicalBurnWound")
                        .And.Not.Contain("BurnWound"),
                    "acid that is still working is its own wound.");
                Assert.That(entities.HasComponent<WolfmedChemicalBurnComponent>(torso), Is.True);
            });

            var before = Damage(entities, torso, "Caustic");
            residue.Update(5f);
            Assert.That(Damage(entities, torso, "Caustic"), Is.GreaterThan(before),
                "the residue keeps biting with nothing new touching the patient.");

            // Water, from wherever: the effect on the humanoid base's touch reaction calls this.
            Assert.That(residue.Wash(body), Is.EqualTo(1));
            Assert.That(entities.HasComponent<WolfmedChemicalBurnComponent>(torso), Is.False);

            var washed = Damage(entities, torso, "Caustic");
            residue.Update(5f);
            Assert.Multiple(() =>
            {
                Assert.That(Damage(entities, torso, "Caustic"), Is.EqualTo(washed),
                    "and stops once the patient is rinsed.");
                Assert.That(Prototypes(entities, wounds, torso), Does.Contain("WFWolfmedChemicalBurnWound"),
                    "the burn itself stays and heals like any other.");
                Assert.That(residue.Wash(body), Is.Zero);
            });
        });
    }

    /// <summary>
    /// A shock burns along the path the current took: an internal wound on top of the surface mark, a
    /// band of heart damage on the Shock past 15 (M3, OD15), and hands that let go.
    /// </summary>
    [Test]
    public async Task ShockBurnsInsideAndSpasmsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var electrical = entities.System<WolfmedElectricalBurnSystem>();
            var hands = entities.System<SharedHandsSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(entities, body, BodyPartType.Torso);

            Damage(entities, body, TargetBodyPart.Torso, "Shock", 40);

            Assert.Multiple(() =>
            {
                Assert.That(Prototypes(entities, wounds, torso), Does.Contain("WFWolfmedInternalBurnWound")
                        .And.Contain("ElectricalWound"),
                    "the mark on the skin and the damage along the path are two findings.");
                // severityMultiplier 0.6: 40 Shock is a moderate internal burn.
                Assert.That(entities.GetComponent<WoundComponent>(
                        FindWound(entities, wounds, torso, "WFWolfmedInternalBurnWound")).Severity,
                    Is.EqualTo(FixedPoint2.New(24)));
            });

            // M3 (OD15): no roll. The hit's own Shock took 0.2 × (40 - 15) = 5 off the heart through the band, and
            // the torso's Shock reach line (15) sent more of it into the chest's organs by weight.
            var heart = electrical.TryShockOrgan(body, WolfmedElectricalBurnSystem.HeartSlot, FixedPoint2.New(0.01f));
            Assert.That(heart, Is.Not.Null);
            var health = entities.GetComponent<WolfmedOrganComponent>(heart!.Value);
            Assert.Multiple(() =>
            {
                Assert.That(health.Health, Is.LessThanOrEqualTo(health.MaxHealth - FixedPoint2.New(5)),
                    "the band did not reach the heart.");
                Assert.That(health.Health, Is.GreaterThan(FixedPoint2.Zero), "shocked, not stopped.");
            });

            // On a fresh patient: the shocked one is already on the floor from the spasm the wound itself
            // caused, and a stunned mob cannot be handed anything.
            var bystander = entities.SpawnEntity("MobHuman", map.GridCoords);
            var held = entities.SpawnEntity("Crowbar", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(bystander, held), Is.True);

            electrical.Spasm(bystander, TimeSpan.FromSeconds(1), dropHeld: true);
            Assert.That(hands.EnumerateHeld(bystander), Is.Empty,
                "the muscles the current crossed let go of everything.");
        });
    }

    /// <summary>The analyzer names the four wounds and the guide and popups have words for them.</summary>
    [Test]
    public async Task NamesExistTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var analyzer = entities.System<Content.Server.Medical.HealthAnalyzerSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Damage(entities, body, TargetBodyPart.Torso, "Cold", 30);

            var diagnostics = analyzer.BuildWoundDiagnostics(body);
            Assert.That(diagnostics, Is.Not.Null);
            Assert.That(diagnostics!.Parts.TryGetValue(TargetBodyPart.Torso, out var torso));

            Assert.Multiple(() =>
            {
                Assert.That(torso.VisibleWounds.Any(wound => wound.Name == "wolfmed-wound-name-frostbite"));
                Assert.That(locale.HasString("wolfmed-wound-name-charring"));
                Assert.That(locale.HasString("wolfmed-wound-name-frostbite"));
                Assert.That(locale.HasString("wolfmed-wound-name-chemical-burn"));
                Assert.That(locale.HasString("wolfmed-wound-name-internal-burn"));
                Assert.That(locale.HasString("wolfmed-cauterize-verb"));
                Assert.That(locale.HasString("wolfmed-cauterize-start"));
                Assert.That(locale.HasString("wolfmed-cauterize-start-self"));
                Assert.That(locale.HasString("wolfmed-cauterize-success"));
                Assert.That(locale.HasString("wolfmed-cauterize-nothing"));
                Assert.That(locale.HasString("wolfmed-chemical-burn-washed"));
            });
        });
    }

    private static void Damage(IEntityManager entities, EntityUid body, TargetBodyPart target,
        string type, int amount)
    {
        entities.System<DamageableSystem>().TryChangeDamage(body, Spec(type, amount),
            ignoreResistances: true, origin: null, targetPart: target);
    }

    private static FixedPoint2 Damage(IEntityManager entities, EntityUid part, string type) =>
        entities.GetComponent<DamageableComponent>(part).Damage.DamageDict
            .GetValueOrDefault(new ProtoId<DamageTypePrototype>(type));

    private static float Rate(IEntityManager entities, EntityUid wound) =>
        entities.TryGetComponent(wound, out WoundBleedingComponent? bleeding) ? bleeding.CurrentRate : 0f;

    private static FixedPoint2 Suppression(IEntityManager entities, EntityUid part) =>
        entities.TryGetComponent(part, out PainComponent? pain) &&
        pain.SuppressionModifiers.TryGetValue(WolfmedFrostbiteSystem.SuppressionKey, out var modifier)
            ? modifier.Amount
            : FixedPoint2.Zero;

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type,
        BodyPartSymmetry symmetry = BodyPartSymmetry.None)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }

    private static List<string> Prototypes(IEntityManager entities, WoundSystem wounds, EntityUid part)
    {
        return wounds.GetWounds((part, entities.GetComponent<WoundableComponent>(part)))
            .Select(wound => wound.Comp.Prototype.Id)
            .ToList();
    }

    private static EntityUid FindWound(IEntityManager entities, WoundSystem wounds, EntityUid part, string prototype)
    {
        return wounds.GetWounds((part, entities.GetComponent<WoundableComponent>(part)))
            .First(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>(prototype))
            .Owner;
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
