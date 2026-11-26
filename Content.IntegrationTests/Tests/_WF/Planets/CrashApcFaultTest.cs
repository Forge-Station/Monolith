using System.Numerics;
using System.Collections.Generic;
using Content.Server._WF.Planets.Flight;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
public sealed class CrashApcFaultTest
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task FaultPersistsUntilMultitoolRepairAndRespectsTheBreaker(bool breaker)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var hull = await BuildHull(pair, map.MapId);
        await MapInitHull(pair, hull);
        var apc = EntityUid.Invalid;
        var user = EntityUid.Invalid;
        var tool = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            apc = em.SpawnEntity("APCBasic", new EntityCoordinates(hull, new Vector2(5.5f, 5.5f)));
            em.GetComponent<ApcComponent>(apc).MainBreakerEnabled = breaker;
            em.AddComponent<WFCrashApcFaultComponent>(apc);
            var batteries = server.System<BatterySystem>();
            batteries.SetCharge(apc, 50000f);
        });
        var off = 0;
        var on = 0;
        var deadlines = new HashSet<TimeSpan>();
        for (var i = 0; i < 20; i++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(0.25f));
            await server.WaitAssertion(() =>
            {
                Assert.That(em.HasComponent<WFCrashApcFaultComponent>(apc), Is.True);
                deadlines.Add(em.GetComponent<WFCrashApcFaultComponent>(apc).NextFlicker);
                if (em.GetComponent<PowerNetworkBatteryComponent>(apc).CanDischarge) on++;
                else off++;
            });
        }
        await server.WaitAssertion(() =>
        {
            Assert.That(off, Is.GreaterThan(0));
            Assert.That(deadlines.Count, Is.GreaterThan(1));
            Assert.That(on, breaker ? Is.GreaterThan(0) : Is.EqualTo(0));
            user = em.SpawnEntity(ViewerProto, new EntityCoordinates(hull, new Vector2(5.5f, 6.5f)));
            tool = em.SpawnEntity("Multitool", em.GetComponent<TransformComponent>(user).Coordinates);
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(user, tool), Is.True);
            server.System<SharedInteractionSystem>().InteractUsing(user, tool, apc, em.GetComponent<TransformComponent>(apc).Coordinates);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        await server.WaitAssertion(() => Assert.That(em.HasComponent<WFCrashApcFaultComponent>(apc), Is.True, "Repair must take time."));
        await server.WaitRunTicks(pair.SecondsToTicks(3f));
        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WFCrashApcFaultComponent>(apc), Is.False);
            Assert.That(em.GetComponent<PowerNetworkBatteryComponent>(apc).CanDischarge, Is.EqualTo(breaker));
            Assert.That(em.GetComponent<ApcComponent>(apc).MainBreakerEnabled, Is.EqualTo(breaker));
        });
        await pair.CleanReturnAsync();
    }
}
