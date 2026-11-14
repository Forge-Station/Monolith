using Content.Shared._WF.RMCCore.Armor;
using Content.Shared._WF.RMCCore.Stun;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.RMCCore;

/// <summary>RMC's CMArmor cuts the damage a sized mob takes, through Wolfgate's DamageableSystem.</summary>
[TestFixture]
[TestOf(typeof(CMArmorSystem))]
public sealed class CMArmorTest
{
    private const string Plain = "WFRMCCoreTestMob";
    private const string Armored = "WFRMCCoreTestArmoredMob";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFRMCCoreTestMob
  name: rmccore test mob
  components:
  - type: Damageable
    damageContainer: Biological
  - type: RMCSize
    size: Humanoid

- type: entity
  id: WFRMCCoreTestArmoredMob
  parent: WFRMCCoreTestMob
  name: rmccore armored test mob
  components:
  - type: CMArmor
    bio: 10
";

    /// <summary>Bio armor resists burn damage; the same hit on an unarmored mob lands in full.</summary>
    [Test]
    public async Task BioArmorReducesBurnDamage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var damageable = entities.System<DamageableSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var plain = entities.SpawnEntity(Plain, map.GridCoords);
            var armored = entities.SpawnEntity(Armored, map.GridCoords);
            Assert.That(entities.GetComponent<RMCSizeComponent>(armored).Size, Is.EqualTo(RMCSizes.Humanoid));

            var heat = new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Heat"), FixedPoint2.New(20));
            damageable.TryChangeDamage(plain, heat);
            damageable.TryChangeDamage(armored, heat);

            var plainTotal = entities.GetComponent<DamageableComponent>(plain).TotalDamage;
            var armoredTotal = entities.GetComponent<DamageableComponent>(armored).TotalDamage;
            Assert.Multiple(() =>
            {
                Assert.That(plainTotal, Is.GreaterThan(FixedPoint2.Zero), "Test setup: the unarmored mob should take the hit.");
                Assert.That(armoredTotal, Is.GreaterThan(FixedPoint2.Zero), "10 bio armor shouldn't stop 20 heat outright.");
                Assert.That(armoredTotal, Is.LessThan(plainTotal), "CMArmor bio should reduce burn damage.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
