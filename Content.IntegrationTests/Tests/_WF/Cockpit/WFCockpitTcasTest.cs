#nullable enable annotations

using System;
using Content.Client._WF.Cockpit;
using Content.Client._WF.Shuttles.UI;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Shuttles;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Cockpit;

/// <summary>Checks the cockpit reader against replicated grid warnings and both collision-alert disable controls.</summary>
public sealed class WFCockpitTcasTest : InteractionTest
{
    [Test]
    public async Task GridWarningsAndDisableSettingsReachTheCockpitReader()
    {
        var serverConfig = Server.ResolveDependency<IConfigurationManager>();
        var clientConfig = Client.ResolveDependency<IConfigurationManager>();
        var originalEnabled = true;
        NetEntity grid = default;
        CollisionWarningBanner? banner = null;
        await Server.WaitAssertion(() =>
        {
            originalEnabled = serverConfig.GetCVar(CollisionWarningCVars.Enabled);
            grid = SEntMan.GetNetEntity(MapData.Grid.Owner);
        });
        try
        {
            await Server.WaitPost(() => serverConfig.SetCVar(CollisionWarningCVars.Enabled, true));
            await RunTicks(5);
            await Client.WaitAssertion(() =>
            {
                Assert.That(clientConfig.GetCVar(CollisionWarningCVars.Enabled), Is.True);
                banner = new CollisionWarningBanner();
                Assert.That(banner.WfCockpitTcasReading().State, Is.EqualTo(WFCockpitTcasState.NoSignal));
                var clientGrid = CEntMan.GetEntity(grid);
                Assert.That(CEntMan.EntityExists(clientGrid), Is.True, "The real player must receive the grid through PVS.");
                banner.SetShuttle(clientGrid);
                Assert.That(banner.WfCockpitTcasReading().State, Is.EqualTo(WFCockpitTcasState.Clear));
            });

            foreach (var level in new[] { CollisionWarningLevel.Advisory, CollisionWarningLevel.Imminent })
            {
                TimeSpan impactTime = default;
                await Server.WaitPost(() =>
                {
                    var warning = SEntMan.EnsureComponent<CollisionWarningComponent>(MapData.Grid.Owner);
                    warning.Level = level;
                    warning.ImpactTime = impactTime = STiming.CurTime + TimeSpan.FromSeconds(8);
                    warning.ThreatName = "Approaching vessel";
                    warning.Bearing = 315;
                    warning.ClosingSpeed = 24;
                    // Keep the synthetic warning alive while isolating replication from collision prediction.
                    warning.ClearTime = STiming.CurTime + TimeSpan.FromMinutes(1);
                    SEntMan.Dirty(MapData.Grid.Owner, warning);
                });
                await RunTicks(5);
                await Client.WaitAssertion(() =>
                {
                    var reading = banner!.WfCockpitTcasReading();
                    Assert.That(reading.State, Is.EqualTo(level == CollisionWarningLevel.Advisory
                        ? WFCockpitTcasState.Caution : WFCockpitTcasState.Warning));
                    Assert.That(reading.ImpactTime, Is.EqualTo(impactTime));
                    Assert.That(reading.ThreatName, Is.EqualTo("Approaching vessel"));
                    Assert.That(reading.Bearing, Is.EqualTo(315));
                    Assert.That(reading.ClosingSpeed, Is.EqualTo(24));
                });
            }

            await Server.WaitPost(() => SEntMan.EnsureComponent<CollisionWarningDisabledComponent>(MapData.Grid.Owner));
            await RunTicks(5);
            await Client.WaitAssertion(() =>
                Assert.That(banner!.WfCockpitTcasReading().State, Is.EqualTo(WFCockpitTcasState.Off)));
            await Server.WaitPost(() =>
            {
                SEntMan.RemoveComponent<CollisionWarningDisabledComponent>(MapData.Grid.Owner);
                SEntMan.RemoveComponent<CollisionWarningComponent>(MapData.Grid.Owner);
            });
            await RunTicks(5);
            await Client.WaitAssertion(() =>
                Assert.That(banner!.WfCockpitTcasReading().State, Is.EqualTo(WFCockpitTcasState.Clear)));

            await Server.WaitPost(() => serverConfig.SetCVar(CollisionWarningCVars.Enabled, false));
            await RunTicks(5);
            await Client.WaitAssertion(() =>
            {
                Assert.That(clientConfig.GetCVar(CollisionWarningCVars.Enabled), Is.False,
                    "A global server disable must replicate instead of leaving a false TCAS OK indication.");
                Assert.That(banner!.WfCockpitTcasReading().State, Is.EqualTo(WFCockpitTcasState.Off));
            });
            await Server.WaitPost(() => serverConfig.SetCVar(CollisionWarningCVars.Enabled, true));
            await RunTicks(5);
            await Client.WaitAssertion(() =>
            {
                Assert.That(clientConfig.GetCVar(CollisionWarningCVars.Enabled), Is.True);
                Assert.That(banner!.WfCockpitTcasReading().State, Is.EqualTo(WFCockpitTcasState.Clear));
                banner.SetShuttle(null);
                Assert.That(banner.WfCockpitTcasReading().State, Is.EqualTo(WFCockpitTcasState.NoSignal));
            });
        }
        finally
        {
            await Client.WaitPost(() => banner?.Dispose());
            await Server.WaitPost(() =>
            {
                SEntMan.RemoveComponent<CollisionWarningDisabledComponent>(MapData.Grid.Owner);
                SEntMan.RemoveComponent<CollisionWarningComponent>(MapData.Grid.Owner);
                serverConfig.SetCVar(CollisionWarningCVars.Enabled, originalEnabled);
            });
            await RunTicks(5);
            await Client.WaitAssertion(() =>
                Assert.That(clientConfig.GetCVar(CollisionWarningCVars.Enabled), Is.EqualTo(originalEnabled)));
        }
    }
}
