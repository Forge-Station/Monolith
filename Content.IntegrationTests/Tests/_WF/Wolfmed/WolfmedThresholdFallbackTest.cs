#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// HOOK 11 swapped every mob's thresholds onto <c>CheckVitalDamage</c>. Anything that is not a wound host
/// has to fall back to its plain damage total and cross its own prototype thresholds at exactly the numbers
/// the prototype names.
/// </summary>
[TestFixture]
[TestOf(typeof(MobThresholdSystem))]
public sealed class WolfmedThresholdFallbackTest : GameTest
{
    [Test]
    public async Task NonWoundHostCritsAndDiesAtItsThresholdsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var protos = server.ResolveDependency<IPrototypeManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var damageable = entities.System<DamageableSystem>();
            var mobState = entities.System<MobStateSystem>();
            var thresholds = entities.System<MobThresholdSystem>();
            var mob = entities.SpawnEntity("MobMouse", map.GridCoords);

            Assert.That(entities.HasComponent<WoundHostComponent>(mob), Is.False,
                "MobMouse must stay a non-wound-host for this guard to mean anything.");
            Assert.That(thresholds.TryGetThresholdForState(mob, MobState.Critical, out var crit), Is.True);
            Assert.That(thresholds.TryGetThresholdForState(mob, MobState.Dead, out var dead), Is.True);

            var blunt = protos.Index<DamageTypePrototype>("Blunt");
            var one = FixedPoint2.New(1);

            damageable.TryChangeDamage(mob, new DamageSpecifier(blunt, crit!.Value - one), true);
            Assert.That(mobState.IsAlive(mob), Is.True, "a mob crit a point short of its crit threshold.");

            damageable.TryChangeDamage(mob, new DamageSpecifier(blunt, one), true);
            Assert.That(mobState.IsCritical(mob), Is.True, "a mob did not crit at its crit threshold.");

            damageable.TryChangeDamage(mob, new DamageSpecifier(blunt, dead!.Value - crit.Value - one), true);
            Assert.That(mobState.IsDead(mob), Is.False, "a mob died a point short of its death threshold.");

            damageable.TryChangeDamage(mob, new DamageSpecifier(blunt, one), true);
            Assert.That(mobState.IsDead(mob), Is.True, "a mob did not die at its death threshold.");
        });
    }
}
