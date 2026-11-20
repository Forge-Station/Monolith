#nullable enable
using System.Linq;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.Server._WF.Synthetics;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Radiation.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Biotype;
using Content.Shared._WF.Synthetics;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WF.Synthetics;

/// <summary>
/// Radiation on a synthetic fills its collector instead of the body; a full collector strains and leaks into the core,
/// a burnt-out or missing one leaves the core to take it all, and a pulled collector vents.
/// </summary>
[TestFixture]
[TestOf(typeof(WFRadCollectorSystem))]
public sealed class WFRadCollectorTest : WolfmedGameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [Test]
    public async Task CollectorTakesTheRadiationTest()
    {
        var map = await CreateTestMap();
        EntityUid body = default, collector = default;

        await Server.WaitAssertion(() =>
        {
            body = SpawnSynthetic(map);
            var found = SEntMan.System<WFRadCollectorSystem>().GetCollector(body);
            Assert.That(found, Is.Not.Null, "a synthetic body has no radiation collector.");
            collector = found!.Value.Owner;

            Irradiate(body, 100);
            Assert.Multiple(() =>
            {
                Assert.That(Radiation(body), Is.Zero, "radiation still builds on the body.");
                Assert.That(Charge(collector), Is.EqualTo(100f).Within(0.01f));
                Assert.That(CoreHealth(body), Is.EqualTo(CoreMax(body)), "the core was touched while the collector had room.");
            });

            // The chassis panel shows the collector's charge and the built-in meter's reading.
            var panel = SEntMan.System<WFChassisPanelSystem>().BuildState(body);
            Assert.Multiple(() =>
            {
                Assert.That(panel.RadCollector, Is.EqualTo(100f / 300f).Within(0.001f), "the panel does not show the collector's charge.");
                Assert.That(panel.Radiation, Is.EqualTo(100f).Within(0.01f), "the panel's meter does not read the exposure.");
            });

            // 200 fills it, 200 overflows: strain on the collector, half-rate corruption in the core.
            Irradiate(body, 400);
            Assert.Multiple(() =>
            {
                Assert.That(Charge(collector), Is.EqualTo(300f).Within(0.01f));
                Assert.That(Health(collector), Is.EqualTo(30f - 200f * 0.03f).Within(0.01f), "the overflow did not strain the collector.");
                Assert.That(CoreHealth(body), Is.EqualTo(CoreMax(body) - 200f * 0.01f).Within(0.01f), "a full collector does not leak into the core.");
                Assert.That(Radiation(body), Is.Zero);
            });

            Assert.That(SEntMan.System<SharedBodySystem>().RemoveOrgan(collector), Is.True);
        });

        await RunSeconds(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Charge(collector), Is.LessThanOrEqualTo(300f - 5f * 9f), "a pulled collector does not vent.");

            // With none fitted, all of it corrupts the core.
            var before = CoreHealth(body);
            Irradiate(body, 100);
            Assert.That(CoreHealth(body), Is.EqualTo(before - 100f * WFRadCollectorSystem.BareCorruptionPerRad).Within(0.01f));
            Assert.That(Radiation(body), Is.Zero);
        });
    }

    [Test]
    public async Task CollectorBurnsOutTest()
    {
        var map = await CreateTestMap();
        EntityUid body = default, collector = default;

        await Server.WaitAssertion(() =>
        {
            body = SpawnSynthetic(map);
            collector = SEntMan.System<WFRadCollectorSystem>().GetCollector(body)!.Value.Owner;
            Irradiate(body, 300 + 1100);
            Assert.That(Health(collector), Is.LessThanOrEqualTo(0f), "1100 rads of overflow did not burn the collector out.");
            Assert.That(SEntMan.System<WFRadCollectorSystem>().GetCollector(body), Is.Null, "a burnt-out collector still works.");
        });

        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(collector) || SEntMan.GetComponent<OrganComponent>(collector).Body == null, Is.True,
                "a burnt-out collector stays in the chassis.");
        });
    }

    [Test]
    public async Task EveryChassisHasACollectorTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var collectors = SEntMan.System<WFRadCollectorSystem>();
            Assert.Multiple(() =>
            {
                foreach (var proto in new[] { "MobIPC", "MobSynth" })
                {
                    var mob = SEntMan.SpawnEntity(proto, map.GridCoords);
                    Assert.That(collectors.GetCollector(mob), Is.Not.Null, $"{proto} has no radiation collector.");
                }
            });
        });
    }

    private EntityUid SpawnSynthetic(TestMapData map)
    {
        var profile = HumanoidCharacterProfile.RandomWithSpecies("Human").WithBiotype(WFBiotype.Synthetic);
        var body = SEntMan.System<StationSpawningSystem>().SpawnPlayerMob(map.GridCoords, job: null, profile, station: null);
        Assert.That(SEntMan.HasComponent<WFChassisComponent>(body), "a synthetic body has no chassis.");
        return body;
    }

    private void Irradiate(EntityUid body, float rads) =>
        SEntMan.System<RadiationSystem>().IrradiateEntity(body, rads, 1f);

    private float Radiation(EntityUid body) => SEntMan.System<WolfmedRadiationSystem>().GetRadiation(body);

    private float Charge(EntityUid collector) => SEntMan.GetComponent<WFRadCollectorComponent>(collector).Charge;

    private float Health(EntityUid organ) => SEntMan.GetComponent<WolfmedOrganComponent>(organ).Health.Float();

    private float CoreHealth(EntityUid body) => SEntMan.System<WolfmedLifeSystem>().GetBrainOrgan(body)!.Value.Comp.Health.Float();

    private float CoreMax(EntityUid body) => SEntMan.System<WolfmedLifeSystem>().GetBrainOrgan(body)!.Value.Comp.MaxHealth.Float();
}
