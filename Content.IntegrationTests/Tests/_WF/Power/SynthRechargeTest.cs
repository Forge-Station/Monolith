#nullable enable
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._EinsteinEngines.Power;
using Content.Server._EinsteinEngines.Power.Components;
using Content.Server._HL.Silicons.Synths.Battery;
using Content.Server.Power.EntitySystems;
using Content.Server.PowerCell;
using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._HL.Silicons.Synths.Battery;
using Content.Shared.Power.Components;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Power;

/// <summary>
/// Synths and IPCs recharge by draining a charged power cell or a wall APC. A synth's cell sits in an organ slot,
/// which the stock drinker could not find, so this covers both the verb and the transfer for each source.
/// </summary>
[TestFixture]
[TestOf(typeof(BatteryDrinkerSystem))]
public sealed class SynthRechargeTest
{
    private const string Synth = "MobSynth";
    private const string Ipc = "MobIPC";
    private const string Cell = "PowerCellSmall";
    private const string Apc = "APCBasic";
    private const string DrinkVerb = "battery-drinker-verb-drink";

    /// <summary>Seconds of drinking, kept short so nothing else can interrupt the DoAfter.</summary>
    private const float DrinkSpeed = 0.1f;

    [Test]
    [TestCase(Synth)]
    [TestCase(Ipc)]
    public async Task DrainCellTest(string drinkerProto)
    {
        await using var pair = await PoolManager.GetServerClient();
        var (drinker, coords) = await SpawnDrinker(pair, drinkerProto);

        var server = pair.Server;
        var sEntMan = server.EntMan;
        var battery = server.System<BatterySystem>();

        var own = await OwnBattery(pair, drinker);
        var cell = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            cell = sEntMan.SpawnEntity(Cell, coords);
            battery.SetCharge(own, 0f);
        });

        var cellCharge = await Charge(pair, cell);
        Assert.That(cellCharge, Is.GreaterThan(0f), "Precondition: the spawned cell is charged.");

        await Drink(pair, drinker, cell);

        Assert.That(await Charge(pair, cell), Is.EqualTo(0f), "The cell was not drained.");
        Assert.That(await Charge(pair, own), Is.EqualTo(cellCharge), "The drinker did not take the cell's charge.");

        await pair.CleanReturnAsync();
    }

    [Test]
    [TestCase(Synth)]
    [TestCase(Ipc)]
    public async Task DrainApcTest(string drinkerProto)
    {
        await using var pair = await PoolManager.GetServerClient();
        var (drinker, coords) = await SpawnDrinker(pair, drinkerProto);

        var server = pair.Server;
        var sEntMan = server.EntMan;
        var battery = server.System<BatterySystem>();

        var own = await OwnBattery(pair, drinker);
        var apc = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            apc = sEntMan.SpawnEntity(Apc, coords);
            battery.SetCharge(own, 0f);
        });

        var before = await Charge(pair, apc);
        Assert.That(before, Is.GreaterThan(0f), "Precondition: the APC holds a charge.");

        await Drink(pair, drinker, apc);

        var gained = await Charge(pair, own);
        Assert.That(gained, Is.GreaterThan(0f), "The drinker took nothing from the APC.");
        Assert.That(await Charge(pair, apc), Is.EqualTo(before - gained), "The APC did not lose what the drinker gained.");

        await pair.CleanReturnAsync();
    }

    /// <summary>An empty APC offers the verb but hands over nothing.</summary>
    [Test]
    public async Task EmptyApcTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var (drinker, coords) = await SpawnDrinker(pair, Synth);

        var server = pair.Server;
        var sEntMan = server.EntMan;
        var battery = server.System<BatterySystem>();

        var own = await OwnBattery(pair, drinker);
        var apc = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            apc = sEntMan.SpawnEntity(Apc, coords);
            battery.SetCharge(apc, 0f);
            battery.SetCharge(own, 0f);
        });

        await Drink(pair, drinker, apc);

        Assert.That(await Charge(pair, own), Is.EqualTo(0f), "Charge came from an empty APC.");

        await pair.CleanReturnAsync();
    }

    /// <summary>Spawns a drinker, waits for its cell and stops its idle draw so the charge numbers stay exact.</summary>
    private static async Task<(EntityUid Drinker, EntityCoordinates Coords)> SpawnDrinker(TestPair pair, string proto)
    {
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var sEntMan = server.EntMan;

        var drinker = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            drinker = sEntMan.SpawnEntity(proto, map.GridCoords);

            // The stock speed would outlast the test's tick budget.
            sEntMan.GetComponent<BatteryDrinkerComponent>(drinker).DrinkSpeed = DrinkSpeed;
        });

        // SynthBatteryPowerSystem inserts a synth's starting cell on its once-a-second update.
        await server.WaitRunTicks(90);

        await server.WaitPost(() =>
        {
            if (sEntMan.TryGetComponent(drinker, out SynthBatteryComponent? synth))
                synth.DrawRate = 0f;

            if (sEntMan.TryGetComponent(drinker, out SiliconComponent? silicon))
                silicon.DrainPerSecond = 0f;
        });

        return (drinker, map.GridCoords);
    }

    /// <summary>The battery the drinker stores its own charge in, wherever it keeps it.</summary>
    private static async Task<EntityUid> OwnBattery(TestPair pair, EntityUid drinker)
    {
        var server = pair.Server;
        var synthBattery = server.System<SynthBatterySystem>();
        var powerCell = server.System<PowerCellSystem>();
        var own = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            if (synthBattery.TryGetBattery(drinker, out var organ))
                own = organ.Value.Owner;
            else if (powerCell.TryGetBatteryFromSlot(drinker, out var slotted, out _))
                own = slotted.Value;

            Assert.That(own, Is.Not.EqualTo(EntityUid.Invalid), "The drinker has no cell of its own.");
        });

        return own;
    }

    /// <summary>Runs the drink verb to completion.</summary>
    private static async Task Drink(TestPair pair, EntityUid drinker, EntityUid source)
    {
        var server = pair.Server;
        var verbs = server.System<SharedVerbSystem>();
        var loc = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            var text = loc.GetString(DrinkVerb);
            var verb = verbs.GetLocalVerbs(source, drinker, typeof(AlternativeVerb))
                .FirstOrDefault(v => v.Text == text);

            Assert.That(verb, Is.Not.Null, "The drinker was not offered the drink verb.");
            verbs.ExecuteVerb(verb, drinker, source);
        });

        await server.WaitRunTicks(30);
    }

    private static async Task<float> Charge(TestPair pair, EntityUid uid)
    {
        var charge = 0f;
        await pair.Server.WaitPost(() =>
            charge = pair.Server.EntMan.GetComponent<BatteryComponent>(uid).CurrentCharge);

        return charge;
    }
}
