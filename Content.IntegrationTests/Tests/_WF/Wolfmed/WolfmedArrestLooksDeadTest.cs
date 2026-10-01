#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Body.Components;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Examine;
using Content.Shared.Mobs.Systems;
using Content.Shared.StatusIcon;
using NUnit.Framework;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// ARREST: a stopped heart is still <see cref="Content.Shared.Mobs.MobState.Critical"/> - metabolism, rot
/// and every rule that reads a living body keep working - but nothing anybody can see says so. No gasping,
/// an examine that reads as a corpse, and a flatline on the medical HUD instead of the crit icon.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedLifeSystem))]
public sealed class WolfmedArrestLooksDeadTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
# Starts under its own suffocation threshold, so the first respirator update either gasps or does not.
# Neither body in the gasp test can breathe its way out of it: both are incapacitated.
- type: entity
  id: WolfmedTestGasper
  parent: MobHuman
  suffix: gasping
  components:
  - type: Respirator
    saturation: 0
    suffocationThreshold: 1
    updateInterval: 0.5
    gaspEmoteCooldown: 0
";

    /// <summary>The heart stops and the body stays Critical: arrest is a living body that looks dead.</summary>
    [Test]
    public async Task ArrestStaysCriticalTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            var mobState = entities.System<MobStateSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);

            Assert.That(life.StartArrest(body, "test"), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(life.InArrest(body), Is.True);
                Assert.That(mobState.IsCritical(body), Is.True, "arrest is not Critical any more.");
                Assert.That(mobState.IsDead(body), Is.False, "arrest killed the body outright.");
            });
        });
    }

    /// <summary>
    /// Two suffocating bodies, both unconscious so neither can breathe its way out of it: the one with a
    /// pulse gasps and the one in arrest makes no sound at all.
    /// </summary>
    [Test]
    public async Task ArrestedBodyDoesNotGaspTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid breathing = default;
        EntityUid arrested = default;

        await server.WaitAssertion(() =>
        {
            var consciousness = entities.System<WolfmedConsciousnessSystem>();
            var life = entities.System<WolfmedLifeSystem>();

            breathing = entities.SpawnEntity("WolfmedTestGasper", map.GridCoords);
            arrested = entities.SpawnEntity("WolfmedTestGasper", map.GridCoords);

            // Both unconscious on the airless test map. An unconscious wound host breathes since M1a, but
            // there is nothing here to breathe, so both stay under the threshold and the only difference
            // left is the stopped heart.
            consciousness.SetExternalPressure(breathing, "test", 1f);
            Assert.That(life.StartArrest(arrested, "test"), Is.True);
        });

        await Pair.RunTicksSync(180);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<RespiratorComponent>(breathing).LastGaspEmoteTime,
                    Is.Not.EqualTo(System.TimeSpan.Zero), "the fixture never reached the gasp at all.");
                Assert.That(entities.GetComponent<RespiratorComponent>(arrested).LastGaspEmoteTime,
                    Is.EqualTo(System.TimeSpan.Zero), "a body in cardiac arrest gasped.");
            });
        });
    }

    /// <summary>
    /// The look report on an arrested body reads as a corpse from across the room, and a hand on the neck
    /// adds the pulse and the chest. None of it is the truth: the analyzer is what has that.
    /// </summary>
    [Test]
    public async Task ArrestedBodyExaminesAsDeadTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var look = entities.System<WolfmedVisualInspectionSystem>();
            var life = entities.System<WolfmedLifeSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var examiner = entities.SpawnEntity("MobHuman", map.GridCoords);

            var before = Notes(look, body, examiner, detailed: true);
            Assert.That(before.Any(note => note.Contains("appears to be dead")), Is.False,
                "a healthy body already read as a corpse.");

            Assert.That(life.StartArrest(body, "test"), Is.True);

            var distant = Notes(look, body, examiner, detailed: false);
            var close = Notes(look, body, examiner, detailed: true);

            Assert.Multiple(() =>
            {
                Assert.That(distant.Any(note => note.Contains("appears to be dead")), Is.True,
                    "an arrested body does not read as dead at a distance.");
                Assert.That(distant.Any(note => note.Contains("is not breathing")), Is.True,
                    "a chest that is not moving is visible from where the corpse line is.");
                Assert.That(close.Any(note => note.Contains("has no pulse")), Is.True,
                    "a hand on the neck found a pulse.");
                Assert.That(locale.TryGetString("wolfmed-look-appears-dead-self", out _), Is.True,
                    "the self line is missing.");
            });
        });
    }

    /// <summary>A medical HUD reads a stopped heart as a flatline, which is its own icon with its own art.</summary>
    [Test]
    public async Task ArrestHudIconResolvesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var protos = server.ResolveDependency<IPrototypeManager>();
        var resources = server.ResolveDependency<IResourceManager>();

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<HealthIconPrototype>("WFHealthIconWolfmedArrest", out var icon), Is.True,
                "the arrest HUD icon prototype is missing.");

            Assert.That(icon!.Icon, Is.InstanceOf<SpriteSpecifier.Rsi>());
            var rsi = (SpriteSpecifier.Rsi) icon.Icon;

            Assert.Multiple(() =>
            {
                Assert.That(rsi.RsiState, Is.EqualTo("Flatline"));
                Assert.That(resources.ContentFileExists(rsi.RsiPath / "meta.json"), Is.True,
                    $"no RSI at {rsi.RsiPath}.");
            });
        });
    }

    private static string[] Notes(
        WolfmedVisualInspectionSystem look,
        EntityUid examined,
        EntityUid examiner,
        bool detailed)
    {
        var report = look.GetLook(examined, examiner, detailed);
        Assert.That(report, Is.Not.Null);
        return report!.Notes.Select(FormattedMessage.RemoveMarkupPermissive).ToArray();
    }
}
