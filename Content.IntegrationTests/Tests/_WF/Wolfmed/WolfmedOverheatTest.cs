#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// GAMEPLAY: overheating a wound host burns it through the wound model. Upstream set MobState.Dead
/// outright, which skipped arrest, the alarms and every other BRAIN-aware system. M4: the pulse itself still never
/// kills; a machine now dies of heat only through the core-heat route (plan §3.11), which
/// <c>WolfmedIpcDeathTest</c> covers.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedOverheatSystem))]
public sealed class WolfmedOverheatTest : GameTest
{
    [Test]
    public async Task OverheatBurnsInsteadOfKillingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var overheat = entities.System<WolfmedOverheatSystem>();
            var mobState = entities.System<MobStateSystem>();
            var ipc = entities.SpawnEntity("MobIPC", map.GridCoords);

            Assert.That(overheat.TryOverheat(ipc, "ipc-overheat-popup"), Is.True,
                "a wound host's overheating was left to the upstream kill.");
            Assert.That(mobState.IsDead(ipc), Is.False,
                "overheating killed a wound host outright instead of burning it.");

            var burned = entities.GetComponent<DamageableComponent>(ipc).TotalDamage;
            Assert.That(burned, Is.GreaterThan(FixedPoint2.Zero),
                "an overheating chassis took no heat at all.");

            // The burn is on a pulse: a second call before the next one is due must not stack another dose.
            Assert.That(overheat.TryOverheat(ipc, "ipc-overheat-popup"), Is.True);
            Assert.That(entities.GetComponent<DamageableComponent>(ipc).TotalDamage, Is.EqualTo(burned),
                "the overheat burn ran more than once a pulse.");
        });
    }

    [Test]
    public async Task OverheatLeavesOrdinaryMobsAloneTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mouse = entities.SpawnEntity("MobMouse", map.GridCoords);
            Assert.That(entities.System<WolfmedOverheatSystem>().TryOverheat(mouse, "ipc-overheat-popup"),
                Is.False, "Wolfmed took over the overheat kill for a mob whose death it does not own.");
        });
    }
}
