using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.RoundSetup;
using Content.Server.Shuttles.Events;
using Content.Server.Atmos.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.Damage;
using Content.Shared.Explosion.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.RoundSetup;

/// <summary>Countdown bursts are sparse, shared across fragments, temporary and cosmetic.</summary>
[TestFixture]
public sealed class CrashEffectsTest
{
    [Test]
    public async Task CountdownBurstsAreCappedAndHarmless()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var (layers, _, _) = await BuildOwnedStack(pair);
        var system = server.System<WFCrashScenarioSystem>();
        var fragment = await BuildHull(pair, em.GetComponent<MapComponent>(layers[^1]).MapId, new Vector2(2000, 0));
        await MapInitHull(pair, fragment);
        const int lobby = 19003;
        var hull = EntityUid.Invalid;
        var station = EntityUid.Invalid;
        var actor = EntityUid.Invalid;
        var effects = new HashSet<EntityUid>();
        await server.WaitAssertion(() =>
        {
            Assert.That(system.TryPrepare(lobby, out station, out var reason), Is.True, reason);
            Assert.That(system.TryGetSpawn(lobby, out var at), Is.True);
            hull = at.EntityId;
            actor = em.SpawnEntity("MobHuman", at);
            // The hull is airless; vacuum damage would mask what the bursts do.
            em.RemoveComponent<BarotraumaComponent>(actor);
            var split = new GridSplitEvent(new[] { fragment }, hull);
            em.EventBus.RaiseEvent(EventSource.Local, ref split);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1));
        await server.WaitAssertion(() =>
        {
            Assert.That(Flames(), Is.Empty, "An unarmed ship must not burst.");
            system.NotifyArrival(lobby, pair.Player!);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1));
        await server.WaitAssertion(() => Assert.That(Flames(), Is.Empty, "Bursts must not start immediately."));

        foreach (var remaining in new[] { 120, 60, 2 })
        {
            await server.WaitPost(() =>
            {
                foreach (var grid in new[] { hull, fragment })
                    em.GetComponent<WFCrashScenarioComponent>(grid).DescendAt =
                        server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromSeconds(remaining);
            });
            await server.WaitRunTicks(pair.SecondsToTicks(0.75f));
            await server.WaitAssertion(() =>
            {
                var fresh = Flames().Where(uid => !effects.Contains(uid)).ToArray();
                Assert.That(fresh, Has.Length.EqualTo(1), "A split must not multiply the crew's bursts.");
                effects.UnionWith(fresh);
                foreach (var fire in fresh)
                {
                    Assert.That(em.HasComponent<TimedDespawnComponent>(fire), Is.True);
                    Assert.That(em.HasComponent<FlammableComponent>(fire), Is.False);
                    Assert.That(em.HasComponent<ExplosiveComponent>(fire), Is.False);
                    Assert.That(em.HasComponent<PhysicsComponent>(fire), Is.False);
                    Assert.That(em.GetComponent<TransformComponent>(fire).GridUid, Is.AnyOf(hull, fragment));
                }
                Assert.That(em.GetComponent<DamageableComponent>(actor).TotalDamage.Float(), Is.Zero,
                    "Cosmetic bursts must not hurt the crew. " + string.Join(",", em.GetComponent<DamageableComponent>(actor).Damage.DamageDict.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key}={kv.Value}")));
            });
        }
        await server.WaitRunTicks(pair.SecondsToTicks(6));
        await server.WaitAssertion(() =>
        {
            Assert.That(effects, Has.Count.EqualTo(3));
            Assert.That(Flames(), Is.Empty, "Flames must expire, with no further bursts after the countdown.");
            Assert.That(effects.All(em.Deleted), Is.True);
            system.RemoveScenario(lobby);
            em.DeleteEntity(station);
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();

        EntityUid[] Flames()
        {
            var found = new List<EntityUid>();
            var query = em.EntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out var uid, out var meta))
                if (meta.EntityPrototype?.ID == "WFCrashCountdownFlame")
                    found.Add(uid);
            return found.ToArray();
        }
    }
}
