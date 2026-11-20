#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Medical.Tourniquet;
using Content.Shared._Onyx.Medical;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.EntityEffects;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Alert;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Rejuvenate;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// W5: what an untreated wound turns into, and what stops it. Infection accumulates on the wound, spreads
/// to the body as fever, travels through the parts to the torso (INFECTION, <see cref="WolfmedInfectionSpreadTest"/>)
/// and ends as sepsis; a dressing all but prevents it, antiseptic drains a local one and an antibiotic clears it at
/// any stage. Necrosis is the other half: a tourniquet nobody took off kills the limb under it, and dead tissue keeps
/// the patient septic until it is amputated.
/// </summary>
/// <remarks>
/// Both systems batch on a five-second timer and advance by whatever time has accumulated, so the tests
/// hand <c>Update</c> the minutes directly rather than waiting for them. Every threshold is data
/// (<c>_WF/Wolfmed/Wounds/infection.yml</c>), so the assertions compare stages and directions, not the
/// numbers themselves.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedInfectionSystem))]
public sealed class WolfmedInfectionTest : GameTest
{
    /// <summary>
    /// These walk the infection's own course on a 20-point cut, a minor wound. Playtest 5 spares minor wounds the air;
    /// InfectionNeedsAReasonTest pins that rule, so here it is off.
    /// </summary>
    [SetUp]
    public async Task InfectMinorWounds()
    {
        await OverrideCVar(Content.IntegrationTests.Fixtures.Attributes.Side.Server, WolfmedCVars.InfectionSparesMinor, false);
    }

    /// <summary>An untreated cut works its way through local, spreading and septic in that order.</summary>
    [Test]
    public async Task InfectionClimbsThroughItsStagesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var infection = entities.System<WolfmedInfectionSystem>();
            var profile = infection.Profile;

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(entities, body, BodyPartType.Torso);
            Damage(entities, body, TargetBodyPart.Torso, "Slash", 20);

            var wound = FindWound(entities, torso, "SlashWound");
            Assert.That(entities.HasComponent<WolfmedInfectionComponent>(wound), Is.True,
                "an open cut is contaminated from the moment it exists.");
            Assert.That(infection.GetStage(wound), Is.EqualTo(WolfmedInfectionStage.None),
                "and does nothing at all until it has had time.");

            // Just past the local threshold at 25 progress and 4 a minute (playtest 5).
            infection.Update(Minutes(7));
            Assert.That(infection.GetStage(wound), Is.EqualTo(WolfmedInfectionStage.Local));

            var pain = entities.System<PainSystem>().GetPain(torso);
            var severity = entities.GetComponent<WoundComponent>(wound).Severity;
            infection.Update(Minutes(2));
            Assert.Multiple(() =>
            {
                Assert.That(entities.System<PainSystem>().GetPain(torso), Is.GreaterThan(pain),
                    "a locally infected wound hurts more as it goes.");
                Assert.That(entities.GetComponent<WoundComponent>(wound).Severity, Is.GreaterThan(severity),
                    "and reopens faster than it closes, which is what 'slower healing' means here.");
            });

            var temperature = entities.GetComponent<Content.Server.Temperature.Components.TemperatureComponent>(body)
                .CurrentTemperature;
            infection.Update(Minutes(7));
            Assert.Multiple(() =>
            {
                Assert.That(infection.GetStage(wound), Is.EqualTo(WolfmedInfectionStage.Spreading));
                // M5 (OD13): past the wound it is a fever, not a poisoning; the toxin load is its own route.
                Assert.That(entities.GetComponent<Content.Server.Temperature.Components.TemperatureComponent>(body)
                    .CurrentTemperature, Is.GreaterThan(temperature), "a spreading infection runs no fever.");
                Assert.That(Damage(entities, body, "Poison"), Is.EqualTo(FixedPoint2.Zero),
                    "a spreading infection still deals Poison.");
                Assert.That(entities.GetComponent<WolfmedInfectionComponent>(wound).Progress,
                    Is.GreaterThanOrEqualTo(profile.SpreadingAt));
            });

            // The severity the infection put back is capped, so an ignored scratch cannot become a wound
            // bigger than the model ever intended.
            infection.Update(Minutes(60));
            Assert.That(entities.GetComponent<WolfmedInfectionComponent>(wound).SeverityAdded,
                Is.LessThanOrEqualTo(profile.MaxSeverityAdded));
        });
    }

    /// <summary>
    /// The two cheap answers: a dressing on the wound cuts the rate to a fraction, and antiseptic drains
    /// a local infection back out. Neither touches one that has already spread.
    /// </summary>
    [Test]
    public async Task DressingAndCleaningPreventInfectionTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var infection = entities.System<WolfmedInfectionSystem>();
            var bleeding = entities.System<WoundBleedingSystem>();

            var dressed = entities.SpawnEntity("MobHuman", map.GridCoords);
            var ignored = entities.SpawnEntity("MobHuman", map.GridCoords);
            foreach (var patient in new[] { dressed, ignored })
                Damage(entities, patient, TargetBodyPart.Torso, "Slash", 20);

            var dressedWound = FindWound(entities, Part(entities, dressed, BodyPartType.Torso), "SlashWound");
            var ignoredWound = FindWound(entities, Part(entities, ignored, BodyPartType.Torso), "SlashWound");
            Assert.That(bleeding.SetTreatment(dressedWound, BleedingTreatment.Bandaged), Is.True);

            infection.Update(Minutes(7));
            Assert.Multiple(() =>
            {
                Assert.That(infection.GetStage(ignoredWound), Is.EqualTo(WolfmedInfectionStage.Local));
                Assert.That(infection.GetStage(dressedWound), Is.EqualTo(WolfmedInfectionStage.None));
                Assert.That(Progress(entities, dressedWound), Is.LessThan(Progress(entities, ignoredWound) / 4f),
                    "gauze early is most of the prevention in the model.");
            });

            // Antiseptic on a local infection: it drains away over the next minute instead of climbing.
            Assert.That(infection.Clean(ignored), Is.EqualTo(1));
            var before = Progress(entities, ignoredWound);
            infection.Update(Minutes(1));
            Assert.That(Progress(entities, ignoredWound), Is.LessThan(before));

            // Once it has spread, cleaning the skin is far too late.
            var septic = entities.SpawnEntity("MobHuman", map.GridCoords);
            Damage(entities, septic, TargetBodyPart.Torso, "Slash", 20);
            var septicWound = FindWound(entities, Part(entities, septic, BodyPartType.Torso), "SlashWound");

            infection.Update(Minutes(16));
            Assert.That(infection.GetStage(septicWound), Is.EqualTo(WolfmedInfectionStage.Spreading));

            Assert.That(infection.Clean(septic), Is.EqualTo(1));
            var spread = Progress(entities, septicWound);
            infection.Update(Minutes(2));
            Assert.That(Progress(entities, septicWound), Is.GreaterThan(spread),
                "the antiseptic never reaches what has already left the wound.");

            // M1b (P20): a burn has no bleed to bandage; its own dressing is what counts.
            var fluid = entities.System<WolfmedFluidLossSystem>();
            var dressedBurnBody = entities.SpawnEntity("MobHuman", map.GridCoords);
            var openBurnBody = entities.SpawnEntity("MobHuman", map.GridCoords);
            foreach (var patient in new[] { dressedBurnBody, openBurnBody })
                Damage(entities, patient, TargetBodyPart.Torso, "Heat", 30);

            var dressedBurn = FindWound(entities, Part(entities, dressedBurnBody, BodyPartType.Torso), "BurnWound");
            var openBurn = FindWound(entities, Part(entities, openBurnBody, BodyPartType.Torso), "BurnWound");
            Assert.That(fluid.Dress(Part(entities, dressedBurnBody, BodyPartType.Torso)), Is.True);

            infection.Update(Minutes(3));
            Assert.That(Progress(entities, dressedBurn), Is.LessThan(Progress(entities, openBurn) / 4f),
                "a dressed burn infects as if it were open.");
        });
    }

    /// <summary>
    /// The two reagent seams. Both entity effects live in Shared and the model does not, so they reach it
    /// by event; this drives the events themselves and checks the humanoid base actually carries the
    /// antiseptic touch reaction that raises the first one.
    /// </summary>
    [Test]
    public async Task ReagentSeamsReachTheModelTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var infection = entities.System<WolfmedInfectionSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Damage(entities, body, TargetBodyPart.Torso, "Slash", 20);
            var wound = FindWound(entities, Part(entities, body, BodyPartType.Torso), "SlashWound");
            infection.Update(Minutes(6));

            var cleaned = new WolfmedCleanWoundsEvent();
            entities.EventBus.RaiseLocalEvent(body, ref cleaned);
            Assert.Multiple(() =>
            {
                Assert.That(cleaned.Cleaned, Is.EqualTo(1));
                Assert.That(entities.GetComponent<WolfmedInfectionComponent>(wound).Cleaned, Is.True);
            });

            var antibiotic = new WolfmedAntibioticEvent(20f);
            entities.EventBus.RaiseLocalEvent(body, ref antibiotic);
            Assert.Multiple(() =>
            {
                Assert.That(antibiotic.Treated, Is.True);
                Assert.That(infection.GetStage(wound), Is.EqualTo(WolfmedInfectionStage.None));
            });

            // The wiring that fires the first of those in play.
            var antiseptics = entities.GetComponent<ReactiveComponent>(body).Reactions!
                .Where(entry => entry.Effects.Any(effect => effect is WolfmedCleanWounds))
                .SelectMany(entry => entry.Reagents!)
                .ToList();
            Assert.That(antiseptics, Does.Contain("Ethanol").And.Contain("Spaceacillin"));
        });
    }

    /// <summary>A knife dug around in a wound makes everything that follows worse.</summary>
    [Test]
    public async Task DirtyTreatmentMultipliesContaminationTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var infection = entities.System<WolfmedInfectionSystem>();

            var clean = entities.SpawnEntity("MobHuman", map.GridCoords);
            var dirty = entities.SpawnEntity("MobHuman", map.GridCoords);
            foreach (var patient in new[] { clean, dirty })
                Damage(entities, patient, TargetBodyPart.Torso, "Slash", 20);

            var cleanWound = FindWound(entities, Part(entities, clean, BodyPartType.Torso), "SlashWound");
            var dirtyWound = FindWound(entities, Part(entities, dirty, BodyPartType.Torso), "SlashWound");

            // What W1's improvised embedded-object removal calls.
            infection.Contaminate(dirtyWound);
            Assert.That(entities.GetComponent<WolfmedInfectionComponent>(dirtyWound).Contamination,
                Is.EqualTo(infection.Profile.ContaminationMultiplier));

            infection.Update(Minutes(4));
            Assert.That(Progress(entities, dirtyWound), Is.GreaterThan(Progress(entities, cleanWound)));
        });
    }

    /// <summary>
    /// Sepsis: the body-level stage. It shows on the patient's own alerts and an antibiotic is the only thing that
    /// reverses it. M5 (OD13): it deals no Poison; it kills through its own brain drain.
    /// </summary>
    [Test]
    public async Task SepsisShowsAndAntibioticsClearItTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var infection = entities.System<WolfmedInfectionSystem>();
            var alerts = entities.System<AlertsSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(entities, body, BodyPartType.Torso);
            Damage(entities, body, TargetBodyPart.Torso, "Slash", 20);
            var wound = FindWound(entities, torso, "SlashWound");

            // INFECTION: a chest wound spreads at fifteen minutes and infects the torso itself, which spreads twelve
            // minutes later and starts sepsis: about twenty-seven minutes of nobody doing anything (playtest 5). One
            // 35-minute call covers the one hop from the wound to its own part.
            infection.Update(Minutes(35));
            Assert.Multiple(() =>
            {
                Assert.That(infection.GetStage(wound), Is.EqualTo(WolfmedInfectionStage.Septic));
                Assert.That(infection.GetPartStage(torso), Is.EqualTo(WolfmedInfectionStage.Septic));
                Assert.That(entities.HasComponent<WolfmedSepsisComponent>(body), Is.True);
            });

            infection.Update(Minutes(4));
            Assert.Multiple(() =>
            {
                Assert.That(infection.GetSepsis(body), Is.GreaterThan(0f));
                Assert.That(Damage(entities, body, "Poison"), Is.EqualTo(FixedPoint2.Zero), "sepsis still deals Poison.");
                Assert.That(alerts.IsShowingAlert(body, WolfmedInfectionSystem.SepsisAlert), Is.True);
            });

            // A full course: every wound and every infected part cleared, then the bloodstream.
            Assert.That(infection.Treat(body, 20f), Is.True);
            Assert.That(infection.GetStage(wound), Is.EqualTo(WolfmedInfectionStage.None),
                "the antibiotic reaches contamination no dressing could.");
            Assert.That(infection.GetPartStage(torso), Is.EqualTo(WolfmedInfectionStage.None));

            infection.Treat(body, 40f);
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<WolfmedSepsisComponent>(body), Is.False);
                Assert.That(alerts.IsShowingAlert(body, WolfmedInfectionSystem.SepsisAlert), Is.False);
            });
        });
    }

    /// <summary>
    /// A tourniquet is a ten-minute solution. Left on, the limb under it dies, and the dead limb is then a
    /// standing source of sepsis; loosening it in time gives the bleeding back instead.
    /// </summary>
    [Test]
    public async Task TourniquetLeftOnKillsTheLimbTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var infection = entities.System<WolfmedInfectionSystem>();
            var necrosis = entities.System<WolfmedNecrosisSystem>();
            var tourniquet = entities.System<TourniquetSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            Damage(entities, body, TargetBodyPart.LeftArm, "Slash", 25);

            Assert.That(tourniquet.Apply(body, arm), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<WolfmedTourniquetComponent>(arm), Is.True);
                Assert.That(necrosis.IsAtRisk(arm), Is.False, "the clock starts on the first tick, not on application.");
            });

            necrosis.Update(60f);
            Assert.That(necrosis.IsAtRisk(arm), Is.True, "the analyzer flags the limb from the moment it starts.");

            // Well past the ten minutes the profile allows.
            necrosis.Update((float) infection.Profile.TourniquetOnset.TotalSeconds + 60f);
            Assert.Multiple(() =>
            {
                Assert.That(necrosis.IsNecrotic(arm), Is.True);
                Assert.That(Prototypes(entities, arm), Does.Contain("WFWolfmedNecrosisWound"));
                Assert.That(entities.HasComponent<WolfmedTourniquetComponent>(arm), Is.False,
                    "there is nothing left for the tourniquet to save.");
            });

            // Dead tissue keeps the patient septic on its own, with no other wound involved. INFECTION: later than it
            // used to (about a minute, off the necrosis wound itself): the dead arm is pinned at 100 and has to infect
            // the torso at 3 a minute (playtest 5) before sepsis can start, so none at 19 minutes and sepsis by 21.
            Run(infection, 19f);
            Assert.Multiple(() =>
            {
                Assert.That(infection.GetPartStage(arm), Is.EqualTo(WolfmedInfectionStage.Septic));
                Assert.That(entities.HasComponent<WolfmedSepsisComponent>(body), Is.False,
                    "sepsis started before the arm's infection reached the torso.");
            });

            Run(infection, 2f);
            Assert.Multiple(() =>
            {
                Assert.That(infection.GetPartStage(Part(entities, body, BodyPartType.Torso)),
                    Is.GreaterThanOrEqualTo(WolfmedInfectionStage.Spreading));
                Assert.That(entities.HasComponent<WolfmedSepsisComponent>(body), Is.True);
            });
        });
    }

    /// <summary>Taking the tourniquet off in time costs the bleeding back and stops the clock.</summary>
    [Test]
    public async Task LooseningATourniquetStopsTheClockTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var necrosis = entities.System<WolfmedNecrosisSystem>();
            var bleeding = entities.System<WoundBleedingSystem>();
            var tourniquet = entities.System<TourniquetSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            Damage(entities, body, TargetBodyPart.LeftArm, "Slash", 25);

            Assert.That(tourniquet.Apply(body, arm), Is.True);
            Assert.That(bleeding.GetPartRate(arm), Is.Zero);
            necrosis.Update(60f);

            Assert.That(necrosis.Loosen(body, arm, body), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<WolfmedTourniquetComponent>(arm), Is.False);
                Assert.That(necrosis.IsAtRisk(arm), Is.False);
                Assert.That(bleeding.GetPartRate(arm), Is.GreaterThan(0f), "the trade the verb exists for.");
            });

            necrosis.Update(3600f);
            Assert.That(necrosis.IsNecrotic(arm), Is.False);
        });
    }

    /// <summary>
    /// A full heal has to reach what is not a wound. Sepsis lives on the body and dead tissue on the part,
    /// so clearing every wound entity on its own left the patient septic off a necrotic limb that no
    /// analyzer would show, forever.
    /// </summary>
    [Test]
    public async Task RejuvenateClearsSepsisAndNecrosisTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var infection = entities.System<WolfmedInfectionSystem>();
            var necrosis = entities.System<WolfmedNecrosisSystem>();
            var tourniquet = entities.System<TourniquetSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            Damage(entities, body, TargetBodyPart.LeftArm, "Slash", 25);

            Assert.That(tourniquet.Apply(body, arm), Is.True);
            necrosis.Update((float) infection.Profile.TourniquetOnset.TotalSeconds + 120f);
            // INFECTION: the dead arm infects the torso before sepsis starts, 20 minutes (TourniquetLeftOnKillsTheLimbTest).
            Run(infection, 21f);
            var torso = Part(entities, body, BodyPartType.Torso);
            Assert.Multiple(() =>
            {
                Assert.That(necrosis.IsNecrotic(arm), Is.True);
                Assert.That(entities.HasComponent<WolfmedSepsisComponent>(body), Is.True);
                Assert.That(entities.HasComponent<WolfmedPartInfectionComponent>(torso), Is.True);
            });

            entities.EventBus.RaiseLocalEvent(body, new RejuvenateEvent());

            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<WolfmedSepsisComponent>(body), Is.False);
                Assert.That(entities.HasComponent<WolfmedNecrosisComponent>(arm), Is.False);
                Assert.That(entities.HasComponent<WolfmedTourniquetComponent>(arm), Is.False);
                Assert.That(Prototypes(entities, arm), Does.Not.Contain("WFWolfmedNecrosisWound"));
                // INFECTION: nor any part's own infection, or an infected torso would start sepsis again.
                Assert.That(entities.HasComponent<WolfmedPartInfectionComponent>(arm), Is.False);
                Assert.That(entities.HasComponent<WolfmedPartInfectionComponent>(torso), Is.False);
            });

            // And it stays gone: the dead limb was the source that used to drive sepsis straight back up.
            infection.Update(Minutes(20));
            necrosis.Update(3600f);
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<WolfmedSepsisComponent>(body), Is.False);
                Assert.That(necrosis.IsNecrotic(arm), Is.False);
            });
        });
    }

    /// <summary>
    /// A part has one necrosis clock, so a wound that takes the slot from a tourniquet has to hand it back
    /// when it is treated. Otherwise the ten minutes reset every time and the strap could stay on forever.
    /// </summary>
    [Test]
    public async Task TourniquetClockSurvivesATreatedWoundTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var infection = entities.System<WolfmedInfectionSystem>();
            var necrosis = entities.System<WolfmedNecrosisSystem>();
            var tourniquet = entities.System<TourniquetSystem>();
            var wounds = entities.System<WoundSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            Damage(entities, body, TargetBodyPart.LeftArm, "Slash", 25);

            Assert.That(tourniquet.Apply(body, arm), Is.True);
            necrosis.Update(120f);
            var accumulated = entities.GetComponent<WolfmedNecrosisComponent>(arm).Progress;
            Assert.That(accumulated, Is.GreaterThan(0f));

            // A deep freeze on the same arm takes the slot, because its onset is the shorter of the two. M6: its
            // risk multiplier 1.5 divides the 300 s onset.
            Damage(entities, body, TargetBodyPart.LeftArm, "Cold", 90);
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<WolfmedNecrosisComponent>(arm).Source,
                    Is.EqualTo(WolfmedNecrosisSource.Wound));
                Assert.That(entities.GetComponent<WolfmedNecrosisComponent>(arm).Onset.TotalSeconds,
                    Is.EqualTo(200d).Within(0.01d));
            });

            wounds.RemoveWound(FindWound(entities, arm, "WFWolfmedFrostbiteWound"));

            var clock = entities.GetComponent<WolfmedNecrosisComponent>(arm);
            Assert.Multiple(() =>
            {
                Assert.That(clock.Source, Is.EqualTo(WolfmedNecrosisSource.Tourniquet),
                    "the strap is still on, so it takes its own clock back.");
                Assert.That(clock.Onset, Is.EqualTo(infection.Profile.TourniquetOnset));
                Assert.That(clock.Progress, Is.GreaterThanOrEqualTo(accumulated),
                    "and the minutes it already spent are not given back.");
            });
        });
    }

    /// <summary>
    /// <c>NecrosisRiskTest</c> (plan §12 M6, P30): a wound's necrosis risk multiplier divides its onset. Frostbite frozen
    /// through (risk 1.5, onset 300 s) kills the limb in 200 s; charring (risk 1, onset 480 s) takes its full 480 s. The
    /// multiplier used to be read only as "is there a risk at all", so both limbs took their raw onsets.
    /// </summary>
    [Test]
    public async Task NecrosisRiskTest()
    {
        await OverrideCVar(Content.IntegrationTests.Fixtures.Attributes.Side.Server, WolfmedCVars.NecrosisEnabled, true);
        await OverrideCVar(Content.IntegrationTests.Fixtures.Attributes.Side.Server, WolfmedCVars.NecrosisRate, 1f);
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var necrosis = entities.System<WolfmedNecrosisSystem>();
            var wounds = entities.System<WoundSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var frozen = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var charred = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Right);
            Assert.That(wounds.CreateOrMergeWound(frozen, "WFWolfmedFrostbiteWound", FixedPoint2.New(90)), Is.Not.Null);
            Assert.That(wounds.CreateOrMergeWound(charred, "WFWolfmedCharringWound", FixedPoint2.New(100)), Is.Not.Null);

            var frozenClock = entities.GetComponent<WolfmedNecrosisComponent>(frozen);
            var charredClock = entities.GetComponent<WolfmedNecrosisComponent>(charred);
            Assert.Multiple(() =>
            {
                Assert.That(frozenClock.Source, Is.EqualTo(WolfmedNecrosisSource.Wound));
                Assert.That(charredClock.Source, Is.EqualTo(WolfmedNecrosisSource.Wound));
                Assert.That(frozenClock.Onset.TotalSeconds, Is.EqualTo(200d).Within(0.01d), "risk 1.5 did not shorten 300 s.");
                Assert.That(charredClock.Onset.TotalSeconds, Is.EqualTo(480d).Within(0.01d), "risk 1 changed the onset.");
            });

            // 210 s: past the frozen arm's 200, well inside the charred arm's 480.
            necrosis.Update(210f);
            Assert.Multiple(() =>
            {
                Assert.That(necrosis.IsNecrotic(frozen), Is.True, "the frozen arm outlived its shortened onset.");
                Assert.That(necrosis.IsNecrotic(charred), Is.False);
                Assert.That(necrosis.IsAtRisk(charred), Is.True);
            });

            // And the charred arm goes at its own 480.
            necrosis.Update(280f);
            Assert.That(necrosis.IsNecrotic(charred), Is.True);
        });
    }

    /// <summary>A limb put back on long after it came off goes back on dead.</summary>
    [Test]
    public async Task LateReattachmentKillsTheLimbTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var necrosis = entities.System<WolfmedNecrosisSystem>();
            var timing = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);

            // Detach and reattach is the real path (WolfmedBodyPartLifecycleSystem drives both of these);
            // the stamp is back-dated because the grace period is wall-clock minutes.
            necrosis.OnDetached(arm);
            Assert.That(necrosis.OnAttached(arm), Is.False, "a limb put straight back is fine.");

            necrosis.OnDetached(arm);
            entities.GetComponent<WolfmedNecrosisComponent>(arm).DetachedAt =
                timing.CurTime - TimeSpan.FromMinutes(30);

            Assert.That(necrosis.OnAttached(arm), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(necrosis.IsNecrotic(arm), Is.True);
                Assert.That(Prototypes(entities, arm), Does.Contain("WFWolfmedNecrosisWound"));
            });

            // INFECTION: a hand put back dead reaches sepsis through the arm and then the torso: 20 minutes to the arm's
            // spreading, 13 more to septic, then 20 to the torso (playtest 5; derived 53 minutes); it used to be about a
            // minute, off the necrosis wound directly.
            var infection = entities.System<WolfmedInfectionSystem>();
            var other = entities.SpawnEntity("MobHuman", map.GridCoords);
            var hand = Part(entities, other, BodyPartType.Hand, BodyPartSymmetry.Left);
            necrosis.OnDetached(hand);
            entities.GetComponent<WolfmedNecrosisComponent>(hand).DetachedAt = timing.CurTime - TimeSpan.FromMinutes(30);
            Assert.That(necrosis.OnAttached(hand), Is.True);

            Run(infection, 52f);
            Assert.Multiple(() =>
            {
                Assert.That(infection.GetPartStage(Part(entities, other, BodyPartType.Arm, BodyPartSymmetry.Left)),
                    Is.GreaterThanOrEqualTo(WolfmedInfectionStage.Spreading), "the dead hand never infected the arm.");
                Assert.That(entities.HasComponent<WolfmedSepsisComponent>(other), Is.False,
                    "sepsis started before the infection reached the torso.");
            });

            Run(infection, 2.5f);
            Assert.That(entities.HasComponent<WolfmedSepsisComponent>(other), Is.True, "a dead hand never reached sepsis.");
        });
    }

    /// <summary>
    /// Disabling infection stops it dead without unwinding what is already there, so an admin can turn it
    /// off mid-round.
    /// </summary>
    [Test]
    public async Task CVarsDisableTheModelTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var infection = entities.System<WolfmedInfectionSystem>();
            var necrosis = entities.System<WolfmedNecrosisSystem>();
            var tourniquet = entities.System<TourniquetSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            Damage(entities, body, TargetBodyPart.LeftArm, "Slash", 25);
            var wound = FindWound(entities, arm, "SlashWound");

            try
            {
                config.SetCVar(WolfmedCVars.InfectionEnabled, false);
                config.SetCVar(WolfmedCVars.NecrosisEnabled, false);
                Assert.That(tourniquet.Apply(body, arm), Is.True);

                infection.Update(Minutes(60));
                necrosis.Update(3600f);
                Assert.Multiple(() =>
                {
                    Assert.That(Progress(entities, wound), Is.Zero);
                    Assert.That(necrosis.IsNecrotic(arm), Is.False);
                });

                // The rate multiplier is the other half: it scales the same timers rather than gating them.
                config.SetCVar(WolfmedCVars.InfectionEnabled, true);
                config.SetCVar(WolfmedCVars.InfectionRate, 10f);
                infection.Update(Minutes(2));
                Assert.That(infection.GetStage(wound), Is.Not.EqualTo(WolfmedInfectionStage.None));
            }
            finally
            {
                config.SetCVar(WolfmedCVars.InfectionEnabled, true);
                config.SetCVar(WolfmedCVars.NecrosisEnabled, true);
                config.SetCVar(WolfmedCVars.InfectionRate, 1f);
            }
        });
    }

    /// <summary>The analyzer reports the stage per part and sepsis for the body, and the words all exist.</summary>
    [Test]
    public async Task AnalyzerAndNamesExistTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var analyzer = entities.System<Content.Server.Medical.HealthAnalyzerSystem>();
            var infection = entities.System<WolfmedInfectionSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Damage(entities, body, TargetBodyPart.Torso, "Slash", 20);
            infection.Update(Minutes(35));

            var diagnostics = analyzer.BuildWoundDiagnostics(body);
            Assert.That(diagnostics, Is.Not.Null);
            Assert.That(diagnostics!.Parts.TryGetValue(TargetBodyPart.Torso, out var torso));

            Assert.Multiple(() =>
            {
                Assert.That(torso.Infection, Is.EqualTo(WolfmedInfectionStage.Septic));
                // INFECTION: the torso's own tissue, on its own line.
                Assert.That(torso.PartInfection, Is.EqualTo(WolfmedInfectionStage.Septic));
                Assert.That(diagnostics.Sepsis, Is.GreaterThanOrEqualTo(0f));

                Assert.That(prototypes.HasIndex<AlertPrototype>(WolfmedInfectionSystem.SepsisAlert));
                Assert.That(prototypes.HasIndex<ReagentPrototype>("Spaceacillin"));
                Assert.That(prototypes.HasIndex<ReactionPrototype>("Spaceacillin"));
                Assert.That(prototypes.HasIndex<WoundPrototype>("WFWolfmedNecrosisWound"));
                Assert.That(prototypes.HasIndex<WolfmedInfectionProfilePrototype>(WolfmedInfectionSystem.DefaultProfile));

                Assert.That(locale.HasString("wolfmed-wound-name-necrosis"));
                Assert.That(locale.HasString("wolfmed-wound-stage-necrotic"));
                Assert.That(locale.HasString("wolfmed-wound-cleaned"));
                Assert.That(locale.HasString("wolfmed-necrosis-warning"));
                Assert.That(locale.HasString("wolfmed-necrosis-dead"));
                Assert.That(locale.HasString("wolfmed-tourniquet-loosen-verb"));
                Assert.That(locale.HasString("wolfmed-tourniquet-loosened"));
                Assert.That(locale.HasString("health-analyzer-wound-infection-local"));
                Assert.That(locale.HasString("health-analyzer-wound-infection-spreading"));
                Assert.That(locale.HasString("health-analyzer-wound-infection-septic"));
                Assert.That(locale.HasString("health-analyzer-wound-necrotic-short"));
                Assert.That(locale.HasString("health-analyzer-wound-necrosis-risk-short"));
                Assert.That(locale.HasString("health-analyzer-wound-sepsis"));
                Assert.That(locale.HasString("alerts-wolfmed-sepsis-name"));
                Assert.That(locale.HasString("alerts-wolfmed-sepsis-desc"));
                Assert.That(locale.HasString("health-analyzer-part-infection-spreading"));
                Assert.That(locale.HasString("health-analyzer-wound-septic-shock"));
                Assert.That(locale.HasString("reagent-name-spaceacillin"));
            });
        });
    }

    /// <summary>Seconds for a number of minutes of model time.</summary>
    /// <summary>
    /// A tick adds and removes the components it walks (a wound closes, a septic body takes damage that
    /// opens new wounds). Live enumeration threw "Collection was modified" and took the server down.
    /// </summary>
    [Test]
    public async Task TickSurvivesWoundsChangingUnderItTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var infection = entities.System<WolfmedInfectionSystem>();
            var necrosis = entities.System<WolfmedNecrosisSystem>();
            var wounds = entities.System<WoundSystem>();

            var closing = new List<EntityUid>();
            for (var i = 0; i < 4; i++)
            {
                var body = entities.SpawnEntity("MobHuman", map.GridCoords);
                Damage(entities, body, TargetBodyPart.Torso, "Slash", 20);
                Damage(entities, body, TargetBodyPart.LeftArm, "Slash", 10);
                Damage(entities, body, TargetBodyPart.RightLeg, "Piercing", 10);
                Damage(entities, body, TargetBodyPart.Head, "Heat", 10);
                closing.Add(FindWound(entities, Part(entities, body, BodyPartType.Torso), "SlashWound"));

                // One point under the burn's top stage: the infection's severity creep pushes it over, and
                // that stage spawns a charring wound, which is itself infectable. That is a component added
                // to the set the tick is walking.
                wounds.CreateOrMergeWound(Part(entities, body, BodyPartType.Torso), "BurnWound", 79);
            }

            Assert.DoesNotThrow(() =>
            {
                for (var step = 0; step < 12; step++)
                {
                    // Close one wound per body part-way through so the tick meets healed wounds too.
                    if (step == 3)
                    {
                        foreach (var wound in closing.Skip(2))
                            wounds.SetWoundState(wound, WoundState.Healed);
                    }

                    // New wounds arrive between and because of ticks.
                    if (step % 4 == 1)
                    {
                        var extra = entities.SpawnEntity("MobHuman", map.GridCoords);
                        Damage(entities, extra, TargetBodyPart.Torso, "Slash", 15);
                    }

                    infection.Update(Minutes(5));
                    necrosis.Update(Minutes(5));
                }
            });
        });
    }

    /// <summary>
    /// The infection widening a wound is not a new injury. It used to reset the bleeding treatment every
    /// tick, so gauze on an infected wound held for five seconds and the limb never showed a dressing.
    /// </summary>
    [Test]
    public async Task InfectionCreepKeepsTheDressingOnTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var infection = entities.System<WolfmedInfectionSystem>();
            var bleeding = entities.System<Content.Shared._Onyx.Wounds.WoundBleedingSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(entities, body, BodyPartType.Torso);
            Damage(entities, body, TargetBodyPart.Torso, "Slash", 20);
            var wound = FindWound(entities, torso, "SlashWound");

            infection.Update(Minutes(7));
            Assert.That(infection.GetStage(wound), Is.EqualTo(WolfmedInfectionStage.Local));

            Assert.That(bleeding.SetTreatment(wound, BleedingTreatment.Bandaged));
            var severity = entities.GetComponent<WoundComponent>(wound).Severity;
            infection.Update(Minutes(2));

            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<WoundComponent>(wound).Severity, Is.GreaterThan(severity),
                    "the creep still happens.");
                Assert.That(entities.GetComponent<WoundBleedingComponent>(wound).Treatment,
                    Is.EqualTo(BleedingTreatment.Bandaged), "and the gauze stays on.");
            });
        });
    }

    private static float Minutes(float minutes) => minutes * 60f;

    /// <summary>
    /// INFECTION: model minutes in the real 5 s batches. A part infects its parent once per call, so a single long
    /// call would hold an infection at the first part it reached.
    /// </summary>
    private static void Run(WolfmedInfectionSystem infection, float minutes)
    {
        for (var t = 0f; t < minutes * 60f - 0.01f; t += 5f)
            infection.Update(5f);
    }

    private static float Progress(IEntityManager entities, EntityUid wound) =>
        entities.TryGetComponent(wound, out WolfmedInfectionComponent? infection) ? infection.Progress : 0f;

    private static void Damage(IEntityManager entities, EntityUid body, TargetBodyPart target,
        string type, int amount)
    {
        entities.System<DamageableSystem>().TryChangeDamage(body, Spec(type, amount),
            ignoreResistances: true, origin: null, targetPart: target);
    }

    private static FixedPoint2 Damage(IEntityManager entities, EntityUid uid, string type) =>
        entities.GetComponent<DamageableComponent>(uid).Damage.DamageDict
            .GetValueOrDefault(new ProtoId<DamageTypePrototype>(type));

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type,
        BodyPartSymmetry symmetry = BodyPartSymmetry.None)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }

    private static List<string> Prototypes(IEntityManager entities, EntityUid part)
    {
        return entities.System<WoundSystem>().GetWounds((part, entities.GetComponent<WoundableComponent>(part)))
            .Select(wound => wound.Comp.Prototype.Id)
            .ToList();
    }

    private static EntityUid FindWound(IEntityManager entities, EntityUid part, string prototype)
    {
        return entities.System<WoundSystem>().GetWounds((part, entities.GetComponent<WoundableComponent>(part)))
            .First(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>(prototype))
            .Owner;
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
