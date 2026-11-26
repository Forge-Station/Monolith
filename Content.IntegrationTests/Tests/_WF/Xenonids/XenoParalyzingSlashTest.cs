#nullable enable
using System.Linq;
using Content.Shared._WF.RMCCore.Actions;
using Content.Shared._WF.RMCCore.Slow;
using Content.Shared._WF.Xenonids.Paralyzing;
using Content.Shared._WF.Xenonids.Plasma;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Projectiles;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>The sentinel's Paralyzing Slash and Slowing Spit paralyze humans for RMC's durations.</summary>
[TestFixture]
public sealed class XenoParalyzingSlashTest
{
    [Test]
    public async Task SlashParalyzesHuman()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid sentinel = default, human = default;
        await server.WaitPost(() =>
        {
            entities.System<SharedMapSystem>().SetTile(map.Grid, new Vector2i(1, 0), map.Tile.Tile);
            human = entities.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 1.5f, 0.5f));
            sentinel = entities.SpawnEntity("CMXenoSentinel", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var plasma = entities.GetComponent<XenoPlasmaComponent>(sentinel);
            entities.System<XenoPlasmaSystem>().SetPlasma((sentinel, plasma), plasma.MaxPlasma);

            var action = entities.System<SharedRMCActionsSystem>().GetActionsWithEvent<XenoParalyzingSlashActionEvent>(sentinel).First();
            var ev = new XenoParalyzingSlashActionEvent { Performer = sentinel, Action = action };
            entities.EventBus.RaiseLocalEvent(sentinel, ev);
            Assert.That(ev.Handled, "Paralyzing Slash should activate.");
            Assert.That(entities.HasComponent<XenoActiveParalyzingSlashComponent>(sentinel));

            entities.System<SharedCombatModeSystem>().SetInCombatMode(sentinel, true);
            var weapon = entities.GetComponent<MeleeWeaponComponent>(sentinel);
            weapon.NextAttack = server.ResolveDependency<IGameTiming>().CurTime;
            Assert.That(entities.System<SharedMeleeWeaponSystem>().AttemptLightAttack(sentinel, sentinel, weapon, human));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<VictimBeingParalyzedComponent>(human), "The slash marks the human for paralysis.");
            Assert.That(entities.HasComponent<KnockedDownComponent>(human), Is.False, "Paralysis waits for the delay.");
        });

        // RMC's StunDelay is 4 s and StunDuration 4 s
        await pair.RunSeconds(4.5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<VictimBeingParalyzedComponent>(human), Is.False);
            Assert.That(entities.HasComponent<KnockedDownComponent>(human), "The human is knocked down once the delay ends.");
            Assert.That(entities.HasComponent<StunnedComponent>(human), "The human is stunned once the delay ends.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A slowing spit hit paralyzes an unarmored human for 3.5 s and super-slows them.</summary>
    [Test]
    public async Task SlowingSpitParalyzesHuman()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid human = default;
        await server.WaitPost(() => human = entities.SpawnEntity("MobHuman", map.GridCoords));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var spit = entities.SpawnEntity("XenoSlowingSpitProjectile", map.GridCoords);
            var ev = new ProjectileHitEvent(new DamageSpecifier(), human);
            entities.EventBus.RaiseLocalEvent(spit, ref ev);

            Assert.That(entities.HasComponent<KnockedDownComponent>(human), "The spit knocks the human down.");
            Assert.That(entities.HasComponent<StunnedComponent>(human), "The spit stuns the human.");
            Assert.That(entities.HasComponent<RMCSuperSlowdownComponent>(human), "The spit super-slows the human.");
        });

        await pair.RunSeconds(4f);

        await server.WaitAssertion(() =>
            Assert.That(entities.HasComponent<StunnedComponent>(human), Is.False, "The stun wears off after 3.5 s."));

        await pair.CleanReturnAsync();
    }
}
