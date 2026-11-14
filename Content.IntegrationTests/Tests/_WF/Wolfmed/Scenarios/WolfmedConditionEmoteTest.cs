#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// Playtest 4, SEPSIS: a sick body shows it. Failed lungs cough, deep sepsis retches and vomits once a vomit interval,
/// and a healthy body, a dead one and a chassis never emote. Real game time, chance pinned at 1.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedConditionEmoteSystem))]
public sealed class WolfmedConditionEmoteTest : GameTest
{
    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.ConditionEmoteInterval, 8f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConditionEmoteChance, 1f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConditionVomitInterval, 60f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConditionCoughSepsis, 40f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConditionRetchSepsis, 60f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConditionVomitSepsis, 90f);
        await OverrideCVar(Side.Server, WolfmedCVars.SedationWarnHeavy, 0.8f);
    }

    /// <summary>
    /// Runs the given seconds of game time, one at a time, and returns each body's emotes in the order played. Sepsis
    /// is pinned on the listed bodies every second.
    /// </summary>
    private async Task<Dictionary<EntityUid, List<string>>> Watch(int seconds, EntityUid[] bodies,
        Dictionary<EntityUid, float>? sepsis = null)
    {
        var seen = bodies.ToDictionary(b => b, _ => new List<string>());
        var counts = bodies.ToDictionary(b => b, _ => 0);
        for (var second = 0; second < seconds; second++)
        {
            await Server.WaitPost(() =>
            {
                foreach (var (body, progress) in sepsis ?? new())
                    SEntMan.EnsureComponent<WolfmedSepsisComponent>(body).Progress = progress;
            });
            await RunSeconds(1);
            await Server.WaitAssertion(() =>
            {
                foreach (var body in bodies)
                {
                    if (!SEntMan.TryGetComponent(body, out WolfmedConditionEmoteComponent? comp) || comp.EmoteCount == counts[body])
                        continue;

                    counts[body] = comp.EmoteCount;
                    seen[body].Add(comp.LastEmote ?? "?");
                }
            });
        }

        return seen;
    }

    private int Vomits(EntityUid body) =>
        SEntMan.TryGetComponent(body, out WolfmedConditionEmoteComponent? comp) ? comp.VomitCount : 0;

    /// <summary><c>FailedLungsCoughTest</c>: a body whose lungs have failed coughs up blood within three intervals.</summary>
    [Test]
    public async Task FailedLungsCoughTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid body = default;
        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitPost(() =>
        {
            var lungs = SEntMan.System<SharedBodySystem>().GetBodyOrganEntityComps<LungComponent>(body).Single();
            SEntMan.System<OrganHealthSystem>().SetHealth((lungs.Owner, SEntMan.GetComponent<WolfmedOrganComponent>(lungs.Owner)),
                FixedPoint2.Zero);
        });

        var seen = (await Watch(24, new[] { body }))[body];
        TestContext.Out.WriteLine($"FailedLungsCoughTest: {string.Join(", ", seen)}");
        Assert.That(seen, Is.Not.Empty, "failed lungs played no emote in three intervals.");
        Assert.That(seen[0], Is.EqualTo(WolfmedConditionEmoteSystem.CoughBlood.Id),
            "the first emote of a body with failed lungs is not coughing up blood.");
    }

    /// <summary>
    /// <c>SepsisRetchesAndVomitsOnceTest</c>: at sepsis 95 the body retches, and a retch brings something up once a
    /// vomit interval, not twice.
    /// </summary>
    [Test]
    public async Task SepsisRetchesAndVomitsOnceTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid body = default;
        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        var seen = (await Watch(64, new[] { body }, new() { [body] = 95f }))[body];
        TestContext.Out.WriteLine($"SepsisRetchesAndVomitsOnceTest: {string.Join(", ", seen)}; vomits {Vomits(body)}");
        Assert.Multiple(() =>
        {
            Assert.That(seen, Does.Contain(WolfmedConditionEmoteSystem.Retch.Id), "sepsis 95 never retched.");
            Assert.That(seen, Does.Contain(WolfmedConditionEmoteSystem.Cough.Id), "sepsis 95 never coughed.");
            Assert.That(Vomits(body), Is.EqualTo(1), "sepsis 95 did not vomit exactly once within the vomit interval.");
        });
    }

    /// <summary>
    /// <c>NoEmoteWithoutACauseTest</c>: over a minute at chance 1, a healthy body never emotes; neither does a dead body
    /// with sepsis at 95 and failed lungs, nor a chassis with sepsis at 95 (organics only).
    /// </summary>
    [Test]
    public async Task NoEmoteWithoutACauseTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid healthy = default, dead = default, ipc = default;
        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            healthy = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            dead = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            ipc = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitPost(() =>
        {
            var organs = SEntMan.System<OrganHealthSystem>();
            var brain = s.Life.GetBrainOrgan(dead)!.Value;
            organs.SetHealth(brain, FixedPoint2.Zero);
            var lungs = SEntMan.System<SharedBodySystem>().GetBodyOrganEntityComps<LungComponent>(dead).Single();
            organs.SetHealth((lungs.Owner, SEntMan.GetComponent<WolfmedOrganComponent>(lungs.Owner)), FixedPoint2.Zero);
        });
        await RunSeconds(1);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(dead), Is.True, "the brain at zero did not kill."));

        var seen = await Watch(60, new[] { healthy, dead, ipc }, new() { [dead] = 95f, [ipc] = 95f });
        TestContext.Out.WriteLine($"NoEmoteWithoutACauseTest: healthy [{string.Join(", ", seen[healthy])}], " +
            $"dead [{string.Join(", ", seen[dead])}], IPC [{string.Join(", ", seen[ipc])}]");
        Assert.Multiple(() =>
        {
            Assert.That(seen[healthy], Is.Empty, "a healthy body emoted.");
            Assert.That(seen[dead], Is.Empty, "a dead body emoted.");
            Assert.That(seen[ipc], Is.Empty, "a chassis emoted.");
            Assert.That(Vomits(dead) + Vomits(ipc), Is.Zero, "a dead body or a chassis vomited.");
        });
    }

    /// <summary>
    /// <c>InfectedChestShiversTest</c> (INFECTION, playtest 5): a fever counts the parts' own infections, but only
    /// from the chest or the head: a spreading arm is a local matter and runs none, a spreading torso, with no wound
    /// and no sepsis, shivers.
    /// </summary>
    [Test]
    public async Task InfectedChestShiversTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid body = default;
        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            // 70 falls 4 a minute with nothing feeding it: spreading for the whole watch.
            var infectionSystem = SEntMan.System<Content.Server._WF.Wolfmed.Wounds.WolfmedInfectionSystem>();
            var arm = s.Part(body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var armInfection = SEntMan.EnsureComponent<WolfmedPartInfectionComponent>(arm);
            armInfection.Progress = 70f;
            armInfection.Stage = WolfmedInfectionStage.Spreading;
            Assert.That(infectionSystem.HasFever(body), Is.False, "a spreading arm runs a fever (playtest 5: not until the chest).");

            var torso = s.Part(body, BodyPartType.Torso);
            var infection = SEntMan.EnsureComponent<WolfmedPartInfectionComponent>(torso);
            infection.Progress = 70f;
            infection.Stage = WolfmedInfectionStage.Spreading;
            Assert.That(infectionSystem.HasFever(body), Is.True, "a spreading torso runs no fever.");
        });

        var seen = (await Watch(24, new[] { body }))[body];
        TestContext.Out.WriteLine($"InfectedChestShiversTest: {string.Join(", ", seen)}");
        Assert.That(seen, Does.Contain(WolfmedConditionEmoteSystem.Shiver.Id), "a spreading torso never shivered.");
    }
}
