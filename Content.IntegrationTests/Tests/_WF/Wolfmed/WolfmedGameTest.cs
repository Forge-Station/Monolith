#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// The base of every Wolfmed fixture: its test map has station air.
/// </summary>
/// <remarks>
/// The pair's own test map is hard vacuum. A body on it takes one low-pressure hit, Blunt 2 and Heat 0.4, each time
/// the barotrauma system's global one-second timer rolls over, which on a wound host is two dry wounds on some part;
/// it also suffocates and freezes. A test that waited a few ticks and then counted a part's wounds or its exact
/// damage failed only when the rollover landed inside its window, and the timer's phase depends on how many ticks
/// the pooled pair has already run, so it moved whenever a test was added anywhere. Older fixtures still give their
/// map air themselves or take the body's BarotraumaComponent off; that is redundant here and harmless.
/// </remarks>
public abstract class WolfmedGameTest : GameTest
{
    private bool _vacuum;
    private TestMapData? _mapBefore;

    /// <summary>The pair's map as the test found it: a pooled pair can still carry the last test's.</summary>
    [SetUp]
    public void RememberTestMap() => _mapBefore = Pair.TestMap;

    /// <summary>A test map with station air on it. Use this in place of <c>Pair.CreateTestMap()</c>.</summary>
    protected Task<TestMapData> CreateTestMap(bool initialized = true) => CreateTestMap(Pair, initialized);

    /// <summary><see cref="CreateTestMap(bool)"/> for a fixture that runs its own pair.</summary>
    public static async Task<TestMapData> CreateTestMap(TestPair pair, bool initialized = true)
    {
        var map = await pair.CreateTestMap(initialized);
        await pair.Server.WaitPost(() =>
            pair.Server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, WolfmedScenario.Air()));
        return map;
    }

    /// <summary>
    /// The pair's bare test map, hard vacuum at 2.7 K, for a test that is about pressure, cold or having no air.
    /// </summary>
    protected Task<TestMapData> CreateVacuumTestMap(bool initialized = true)
    {
        _vacuum = true;
        return Pair.CreateTestMap(initialized);
    }

    /// <summary>
    /// Fails a passing test that made its map with the pair directly and left it bare: that test is a matter of
    /// timing, whatever it showed this run.
    /// </summary>
    [TearDown]
    public async Task TestMapIsNotBareByAccident()
    {
        if (_vacuum || TestContext.CurrentContext.Result.Outcome.Status != TestStatus.Passed ||
            Pair.TestMap is not { } map || ReferenceEquals(map, _mapBefore))
            return;

        var bare = false;
        await Server.WaitPost(() =>
            bare = SEntMan.EntityExists(map.MapUid) && !SEntMan.HasComponent<MapAtmosphereComponent>(map.MapUid));

        Assert.That(bare, Is.False,
            "This test made its map with Pair.CreateTestMap(), which is hard vacuum: pressure damage lands on a body " +
            "there on a timer the test does not control. Use CreateTestMap(), or CreateVacuumTestMap() if the vacuum " +
            "is the point.");
    }
}
