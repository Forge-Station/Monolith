#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._EinsteinEngines.Silicon.WeldingHealing;
using Content.Server.Medical.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// W7: the complete treatment matrix. Every wound prototype the game ships is declared here with what
/// reaches it and what does not, and the declaration is checked against the live prototype data rather
/// than restating it - so a wound whose damage types, healing multiplier or supporting profiles change
/// fails here, and a wound added without a declared treatment fails here too.
/// </summary>
/// <remarks>
/// <para>
/// The sibling <see cref="WolfmedTreatmentMatrixTest"/> drives three real heals through the routing system
/// to pin the capability gate itself. This one is the breadth pass over all 44 wounds, so it reads the
/// gate rather than exercising it: an item reaches a wound when the item's treatable half of its healing
/// spec (W0's <see cref="HealingComponent.TreatedDamageTypes"/>) overlaps the wound's
/// <c>damageTypes</c>, the wound's <c>healingMultiplier</c> is above zero, and the item's
/// <see cref="TreatmentCapability"/> set overlaps that of some body-part profile listing the wound.
/// </para>
/// <para>
/// Both halves matter. <c>damageTypes</c> alone decides nothing: <c>WFWolfmedTendonCutWound</c> carries
/// Slash so that a suture's spec resolves a part at all, and <c>healingMultiplier: 0</c> is what makes
/// the suture land nothing. A row of all-false items with a declared <see cref="Exit"/> is the normal
/// shape of a wound that only a procedure clears.
/// </para>
/// <para>
/// The capability column is the union over every profile that lists the wound, so a cell means "some body
/// that can carry this wound can be treated with this item". The handful of wounds listed by both the
/// organic and the mechanical profiles read accordingly.
/// </para>
/// </remarks>
[TestFixture]
[TestOf(typeof(WoundPrototype))]
public sealed class WolfmedWoundTreatmentMatrixTest : GameTest
{
    /// <summary>Items with a <see cref="HealingComponent"/> whose reach is under test, in matrix order.</summary>
    private static readonly string[] Topicals =
    [
        "Brutepack", "Ointment", "Gauze", "MedicatedSuture", "WFWolfmedSuture", "WFWolfmedMakeshiftSuture",
        "RegenerativeMesh", "CableApcStack",
    ];

    /// <summary>
    /// Tools with a <see cref="WeldingHealingComponent"/>. They are gated by <c>damageContainers</c>
    /// rather than by <see cref="TreatmentCapability"/>, so the capability column for them is asserted
    /// separately in <see cref="WeldingToolsAreMechanicalOnlyTest"/> and taken as Mechanical here.
    /// </summary>
    private static readonly string[] WeldingTools = ["Welder", "Wrench"];

    /// <summary>What clears a wound when no item can: a surgery id, a verb, or time.</summary>
    private enum Exit : byte
    {
        /// <summary>Items are the whole answer; nothing else is needed.</summary>
        Items,

        /// <summary>Nothing clears it. Amputation and a replacement part are the only way out.</summary>
        Permanent,

        /// <summary>It fades on its own, or is shed by a system on a tick.</summary>
        Time,

        /// <summary>An in-world verb or interaction, not an item and not the operating table.</summary>
        Verb,

        /// <summary>A surgery prototype, named in <see cref="Row.Surgery"/>.</summary>
        Surgery,
    }

    /// <summary>
    /// One row of the matrix. <see cref="Items"/> is the set expected to reach the wound; every other
    /// shipped item is expected not to. <see cref="Cause"/> and <see cref="Danger"/> are the same two
    /// columns the guidebook's quick reference prints, kept here so the two cannot drift apart silently.
    /// </summary>
    private sealed record Row(string[] Items, Exit Exit, string Cause, string Danger, string Surgery = "");

    private static readonly string[] Brute = ["Brutepack"];
    private static readonly string[] Cuts = ["Gauze", "MedicatedSuture", "WFWolfmedSuture", "WFWolfmedMakeshiftSuture"];
    private static readonly string[] Burns = ["Ointment", "RegenerativeMesh"];
    private static readonly string[] Coil = ["CableApcStack"];
    private static readonly string[] Welding = ["Welder"];
    private static readonly string[] Panel = ["Welder", "Wrench"];

    /// <summary>Burn topicals plus the coil, for a Shock wound both flesh and a chassis can carry.</summary>
    private static readonly string[] BurnsOrCoil = ["Ointment", "RegenerativeMesh", "CableApcStack"];

    /// <summary>Every tool that reaches ordinary chassis damage: welder, wrench and coil between them.</summary>
    private static readonly string[] Chassis = ["CableApcStack", "Welder", "Wrench"];

    private static readonly string[] Nothing = [];

    /// <summary>
    /// Every wound prototype in the game. A new one without a row fails
    /// <see cref="EveryWoundPrototypeHasADeclaredTreatmentTest"/>; a wrong row fails
    /// <see cref="DeclaredTreatmentsMatchPrototypeDataTest"/>.
    /// </summary>
    private static readonly Dictionary<string, Row> Matrix = new()
    {
        // Onyx's per-damage-type defaults.
        ["BluntWound"] = new(Brute, Exit.Items,
            "a solid blunt hit", "pain, some bleeding, loss of limb use when severe"),
        ["SlashWound"] = new(Cuts, Exit.Items,
            "a cut", "steady bleeding and pain"),
        ["PiercingWound"] = new(Cuts, Exit.Items,
            "a puncture", "heavier bleeding than a cut, and more pain"),
        ["BurnWound"] = new(Burns, Exit.Items,
            "heat, cold or acid", "mounting pain, and scars"),
        // Every profile lists this one, so its capability column is the union of flesh and chassis: burn
        // topicals reach the flesh version and the coil reaches the chassis version.
        ["ElectricalWound"] = new(BurnsOrCoil, Exit.Items,
            "a strong shock", "one-time pain, then loss of limb use"),
        ["BoneFractureWound"] = new(Nothing, Exit.Surgery,
            "repeated or heavy blunt force", "slower movement and slower hand work",
            "WFSurgeryMendFracture"),
        ["SystemicBleedingWound"] = new(Nothing, Exit.Surgery,
            "bleeding with no part to blame", "blood loss", "WFSurgeryStopBleeding"),
        ["InternalBleedingWound"] = new(Nothing, Exit.Surgery,
            "a destroyed organ, or a crushing blow", "invisible blood loss; no dressing reaches it",
            "WFSurgeryStopInternalBleeding"),
        ["SurgicalIncisionWound"] = new(Nothing, Exit.Surgery,
            "an opened incision", "bleeding while open, and an infection risk", "WFSurgeryStopBleeding"),
        ["DismembermentWound"] = new(Nothing, Exit.Surgery,
            "a lost limb", "fast bleeding from the stump", "WFSurgeryStopBleeding"),
        ["AmputationConsequenceWound"] = new(Nothing, Exit.Surgery,
            "an untreated stump", "hides the surgeries that would attach a new part",
            "WFSurgeryHealAmputationConsequence"),
        ["MedicalScarWound"] = new(Nothing, Exit.Permanent,
            "treatment that finished", "none; it is a record"),

        // Onyx's chassis defaults.
        ["IpcMechanicalDamageWound"] = new(Chassis, Exit.Items,
            "any hit on an IPC chassis", "the total chassis damage the analyzer reports"),
        ["CyberneticMechanicalDamageWound"] = new(Chassis, Exit.Items,
            "any hit on a cybernetic limb", "the total limb damage the analyzer reports"),
        ["CyberneticFrameFractureWound"] = new(Nothing, Exit.Surgery,
            "heavy blunt force on a frame", "slower movement and slower hand work",
            "WFSurgeryMendFracture"),

        // Onyx's non-human tissue. Same treatments as flesh, different wound records.
        ["SlimeBluntWound"] = new(Brute, Exit.Items, "a blunt hit on slime", "pain and bleeding"),
        ["SlimeSlashWound"] = new(Cuts, Exit.Items, "a cut on slime", "bleeding and pain"),
        ["SlimePiercingWound"] = new(Cuts, Exit.Items, "a puncture in slime", "bleeding and pain"),
        ["SlimeBurnWound"] = new(Burns, Exit.Items, "heat, cold or acid on slime", "mounting pain"),
        ["PlantBluntWound"] = new(Brute, Exit.Items, "a blunt hit on plant tissue", "pain and sap loss"),
        ["PlantSlashWound"] = new(Cuts, Exit.Items, "a cut in plant tissue", "sap loss and pain"),
        ["PlantPiercingWound"] = new(Cuts, Exit.Items, "a puncture in plant tissue", "sap loss and pain"),
        ["PlantBurnWound"] = new(Burns, Exit.Items, "heat, cold or acid on plant tissue", "mounting pain"),

        // W1, ballistic.
        ["WFWolfmedGrazeWound"] = new(Cuts, Exit.Items,
            "a round that only caught the limb", "a brief bleed that stops on its own"),
        ["WFWolfmedGunshotWound"] = new(Cuts, Exit.Items,
            "a round that went through", "good bleeding, real pain and a high infection risk"),
        ["WFWolfmedLodgedRoundWound"] = new(Nothing, Exit.Verb,
            "a heavy round that stayed in", "never clots, never closes and refuses every treatment"),
        ["WFWolfmedShrapnelWound"] = new(Nothing, Exit.Verb,
            "a blast, or buckshot", "several fragments, each of which blocks treatment"),

        // W2, slash and bite.
        ["WFWolfmedArterialBleedWound"] = new(Cuts, Exit.Surgery,
            "a very deep cut or puncture", "bleeds several times faster than anything else, never clots",
            "WFSurgeryRepairArtery"),
        ["WFWolfmedTendonCutWound"] = new(Nothing, Exit.Surgery,
            "a deep cut to a limb", "slow hand work, or a limp", "WFSurgeryRepairTendon"),
        ["WFWolfmedAvulsionWound"] = new(Cuts, Exit.Items,
            "a bite that tore tissue away", "bleeding, near-certain scarring and a high infection risk"),

        // W3, blunt trauma.
        ["WFWolfmedCrushInjuryWound"] = new(Brute, Exit.Items,
            "a heavy blunt hit", "slows the limb, seeps at its worst, may start internal bleeding"),
        ["WFWolfmedConcussionWound"] = new(Nothing, Exit.Time,
            "a hard knock to the head", "knockdown, blurred sight and slurred speech"),
        ["WFWolfmedDislocationWound"] = new(Nothing, Exit.Verb,
            "a heavy swing, a throw or a bad landing", "costs the limb its use, exactly as a fracture does"),
        ["WFWolfmedOrganContusionWound"] = new(Brute, Exit.Items,
            "a blow to the chest", "takes condition off an organ without destroying it"),

        // W4, burns.
        ["WFWolfmedCharringWound"] = new(Nothing, Exit.Surgery,
            "a burn that reached its critical stage", "dead tissue: no limb use, and it risks rotting",
            "WFSurgeryGraftSkin"),
        ["WFWolfmedFrostbiteWound"] = new(Burns, Exit.Items,
            "cold", "numbs the part, so the patient under-reports it; risks rotting when deep"),
        ["WFWolfmedChemicalBurnWound"] = new(Burns, Exit.Verb,
            "acid", "keeps eating the part until the patient is washed"),
        ["WFWolfmedInternalBurnWound"] = new(Burns, Exit.Items,
            "a strong shock", "heart damage, and locked muscles for a moment"),

        // W5, time.
        ["WFWolfmedNecrosisWound"] = new(Nothing, Exit.Permanent,
            "a tourniquet left on, a frozen or charred limb, a late reattachment",
            "dead tissue: permanent, and it keeps the patient septic"),

        // W6, mechanical.
        ["WFWolfmedDentWound"] = new(Panel, Exit.Items,
            "blunt force on a chassis", "cosmetic until deep, then it slows the limb"),
        ["WFWolfmedBreachWound"] = new(Welding, Exit.Items,
            "a cut or a puncture in a chassis", "the mechanical bleed; it never clots"),
        ["WFWolfmedShortCircuitWound"] = new(Coil, Exit.Items,
            "a shock to a chassis", "the frame locks up and throws sparks each time it worsens"),
        ["WFWolfmedServoDamageWound"] = new(Nothing, Exit.Surgery,
            "a deep cut to a chassis limb", "slow hand work, or a limp", "WFSurgeryReplaceServo"),
        ["WFWolfmedOverheatingWound"] = new(Nothing, Exit.Time,
            "heat on a chassis", "the part runs too hot to work properly"),

        // EVISC, a torso opened by damage its cap could not absorb.
        ["WFWolfmedEviscerationWound"] = new(Nothing, Exit.Surgery,
            "a heavy cut or a blast on a torso already at its cap",
            "the organs end up on the floor, and only the table closes it",
            "WFSurgeryCloseEvisceration"),
        ["WFWolfmedChassisBreachWound"] = new(Nothing, Exit.Surgery,
            "the same hit on a chassis",
            "coolant that never clots, and the components on the deck",
            "WFSurgeryWeldChassisBreach"),
    };

    /// <summary>
    /// The gate a wound added in a later phase has to pass through: no wound prototype exists without a
    /// declared treatment, and no row survives the wound it described being deleted.
    /// </summary>
    [Test]
    public async Task EveryWoundPrototypeHasADeclaredTreatmentTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var shipped = prototypes.EnumeratePrototypes<WoundPrototype>().Select(wound => wound.ID).ToHashSet();
            Assert.Multiple(() =>
            {
                Assert.That(shipped.Except(Matrix.Keys), Is.Empty,
                    "a wound prototype with no row in the matrix: declare what treats it and what does not.");
                Assert.That(Matrix.Keys.Except(shipped), Is.Empty,
                    "a matrix row for a wound prototype that no longer exists.");
            });
        });
    }

    /// <summary>
    /// The matrix itself: for all 44 wounds and every shipped treatment item, the declared cell and
    /// the cell the prototype data produces agree. Both directions, so a row that over-claims and a row
    /// that under-claims both fail.
    /// </summary>
    [Test]
    public async Task DeclaredTreatmentsMatchPrototypeDataTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            // Which capability sets can carry each wound, read off the profiles that list it. A wound in no
            // profile at all is unreachable content and fails here rather than quietly never being treated.
            var carriers = new Dictionary<string, HashSet<TreatmentCapability>>();
            foreach (var profile in prototypes.EnumeratePrototypes<BodyPartProfilePrototype>())
            {
                foreach (var wound in profile.SupportedWounds)
                {
                    if (!carriers.TryGetValue(wound.Id, out var set))
                        carriers[wound.Id] = set = new HashSet<TreatmentCapability>();
                    set.UnionWith(profile.TreatmentCapabilities);
                }
            }

            // Every item's reach, resolved once from its real components.
            var reach = new Dictionary<string, (HashSet<string> Types, HashSet<TreatmentCapability> Capabilities)>();
            foreach (var id in Topicals)
            {
                var item = entities.SpawnEntity(id, map.GridCoords);
                var healing = entities.GetComponent<HealingComponent>(item);
                var treatable = entities.System<WoundHealingSystem>().GetTreatableDamage(healing);
                reach[id] = (treatable.DamageDict.Where(pair => pair.Value < 0).Select(pair => pair.Key).ToHashSet(),
                    healing.TreatmentCapabilities.ToHashSet());
                entities.DeleteEntity(item);
            }

            foreach (var id in WeldingTools)
            {
                var item = entities.SpawnEntity(id, map.GridCoords);
                var welding = entities.GetComponent<WeldingHealingComponent>(item);
                reach[id] = (welding.Damage.DamageDict.Where(pair => pair.Value < 0).Select(pair => pair.Key).ToHashSet(),
                    [TreatmentCapability.Mechanical, TreatmentCapability.Electrical]);
                entities.DeleteEntity(item);
            }

            Assert.Multiple(() =>
            {
                foreach (var (id, row) in Matrix.OrderBy(pair => pair.Key))
                {
                    var wound = prototypes.Index<WoundPrototype>(id);
                    Assert.That(carriers.ContainsKey(id), Is.True,
                        $"{id} is in no body-part profile's supportedWounds, so nothing can ever carry it.");
                    var capabilities = carriers.GetValueOrDefault(id, []);

                    var woundTypes = wound.DamageTypes.Keys.Select(type => type.Id).ToHashSet();
                    foreach (var item in Topicals.Concat(WeldingTools))
                    {
                        var (types, itemCapabilities) = reach[item];
                        var reaches = wound.HealingMultiplier > 0f
                            && types.Overlaps(woundTypes)
                            && itemCapabilities.Overlaps(capabilities);
                        Assert.That(reaches, Is.EqualTo(row.Items.Contains(item)),
                            $"{id} vs {item}: the matrix says {(row.Items.Contains(item) ? "treats" : "does not treat")}, " +
                            $"the data says {(reaches ? "treats" : "does not treat")}. " +
                            $"healingMultiplier {wound.HealingMultiplier}, damageTypes " +
                            $"[{string.Join(", ", wound.DamageTypes.Keys)}], carried by [{string.Join(", ", capabilities)}].");
                    }

                    // A row that no item reaches must name something else that clears it, and a row that
                    // items do cover must not pretend a surgery is required.
                    if (row.Items.Length == 0)
                        Assert.That(row.Exit, Is.Not.EqualTo(Exit.Items),
                            $"{id} has no item that reaches it and no other exit declared.");

                    Assert.That(row.Cause, Is.Not.Empty, $"{id} has no cause for the guidebook.");
                    Assert.That(row.Danger, Is.Not.Empty, $"{id} has no danger for the guidebook.");
                }
            });
        });
    }

    /// <summary>
    /// The surgeries the matrix names exist and are reachable procedures, not dangling ids. Keeps a
    /// renamed or deleted surgery from leaving a wound with a treatment that only the table believes in.
    /// </summary>
    [Test]
    public async Task DeclaredSurgeriesExistTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var (id, row) in Matrix.OrderBy(pair => pair.Key))
                {
                    if (row.Exit == Exit.Surgery)
                    {
                        Assert.That(row.Surgery, Is.Not.Empty, $"{id} declares a surgery exit but names none.");
                        Assert.That(prototypes.HasIndex<EntityPrototype>(row.Surgery), Is.True,
                            $"{id} names surgery {row.Surgery}, which does not exist.");
                    }
                    else
                    {
                        Assert.That(row.Surgery, Is.Empty,
                            $"{id} names a surgery but its exit is {row.Exit}.");
                    }
                }
            });
        });
    }

    /// <summary>
    /// The welding tools' own gate. They are filtered by <c>damageContainers</c> rather than by
    /// capability, so the Mechanical column the matrix assumes for them is pinned here directly: both
    /// accept the two synthetic containers and neither accepts the organic one.
    /// </summary>
    [Test]
    public async Task WeldingToolsAreMechanicalOnlyTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var id in WeldingTools)
                {
                    var item = entities.SpawnEntity(id, map.GridCoords);
                    var welding = entities.GetComponent<WeldingHealingComponent>(item);
                    Assert.That(welding.DamageContainers, Does.Contain("WFSiliconWolfmed"), $"{id}");
                    Assert.That(welding.DamageContainers, Does.Contain("WFInorganicWolfmed"), $"{id}");
                    Assert.That(welding.DamageContainers.Any(container =>
                            container.Contains("Biological", StringComparison.Ordinal)), Is.False,
                        $"{id} must never repair flesh.");
                    entities.DeleteEntity(item);
                }
            });
        });
    }
}
