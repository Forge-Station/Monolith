#nullable enable
using System.Threading.Tasks;
using Content.Client._WF.Wolfmed.Damage;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// P6: the client never writes predicted damage onto a wound host, so the mob's damage is server state only
/// and cannot flicker between the predicted whole-body figure and the server's projected part total.
/// </summary>
/// <remarks>
/// Asserted at the seam rather than through a predicted melee swing: the swing would need a connected,
/// in-combat player on both sides and still only reaches the same DamageDealtEvent block. Driving
/// DamageableSystem directly on the client is the same code path with none of the setup, and it lets the
/// control (a damageable that is not a wound host) run in the same tick for comparison.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedPredictedDamageSystem))]
public sealed class WolfmedPredictionTest : GameTest
{
    /// <summary>A damageable with no body and no WoundHostComponent: the client must still predict on it.</summary>
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedPredictionControl
  components:
  - type: Damageable
    damageContainer: Biological
";

    [Test]
    public async Task ClientDoesNotWritePredictedWoundHostDamageTest()
    {
        var map = await Pair.CreateTestMap();

        EntityUid host = default;
        EntityUid control = default;
        await Server.WaitPost(() =>
        {
            host = Server.EntMan.Spawn("MobHuman", map.MapCoords);
            control = Server.EntMan.Spawn("WolfmedPredictionControl", map.MapCoords);
        });
        await Pair.RunTicksSync(10);

        var clientHost = ToClientUid(host);
        var clientControl = ToClientUid(control);
        Assert.That(Client.EntMan.EntityExists(clientHost), "The wound host never reached the client.");
        Assert.That(Client.EntMan.EntityExists(clientControl), "The control never reached the client.");

        var isWoundHost = false;
        FixedPoint2 hostBefore = default;
        FixedPoint2 hostAfter = default;
        FixedPoint2 controlBefore = default;
        FixedPoint2 controlAfter = default;
        DamageSpecifier? hostResult = null;

        await Client.WaitPost(() =>
        {
            var entMan = Client.EntMan;
            var damageable = entMan.System<DamageableSystem>();
            var protos = Client.ResolveDependency<IPrototypeManager>();
            var blunt = protos.Index<DamageTypePrototype>("Blunt");

            isWoundHost = entMan.HasComponent<WoundHostComponent>(clientHost);

            hostBefore = entMan.GetComponent<DamageableComponent>(clientHost).TotalDamage;
            hostResult = damageable.TryChangeDamage(clientHost, new DamageSpecifier(blunt, 20), ignoreResistances: true);
            hostAfter = entMan.GetComponent<DamageableComponent>(clientHost).TotalDamage;

            controlBefore = entMan.GetComponent<DamageableComponent>(clientControl).TotalDamage;
            damageable.TryChangeDamage(clientControl, new DamageSpecifier(blunt, 20), ignoreResistances: true);
            controlAfter = entMan.GetComponent<DamageableComponent>(clientControl).TotalDamage;
        });

        Assert.Multiple(() =>
        {
            Assert.That(isWoundHost, "MobHuman is not a wound host on the client, so this test proves nothing.");

            // The whole point: the client's copy of the mob is untouched, so nothing it draws from
            // DamageableComponent (health bar, damage overlay, predicted crit threshold) moves before the
            // server's projected total arrives.
            Assert.That(hostAfter, Is.EqualTo(hostBefore), "The client wrote predicted damage onto a wound host.");

            // Still reported to the caller, so the attacker keeps their predicted hit flash and the blunt
            // stamina prediction; only the write is suppressed.
            Assert.That(hostResult, Is.Not.Null, "The seam must not turn a predicted hit into a miss.");
            Assert.That(hostResult!.Empty, Is.False, "The predicted damage must still reach the caller.");

            // Control: the suppression is scoped to the wound-host seam, not to client damage in general.
            Assert.That(controlAfter - controlBefore, Is.EqualTo(FixedPoint2.New(20)),
                "A damageable that is not a wound host must still be predicted normally.");
        });
    }
}
