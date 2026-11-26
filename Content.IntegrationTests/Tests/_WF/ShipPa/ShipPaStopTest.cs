#nullable enable
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Power.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.ShipPa;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.ShipPa;

/// <summary>
/// The console Stop must release the request cooldown only when it actually removes a ship track.
/// </summary>
public sealed class ShipPaStopTest : InteractionTest
{
    [Test]
    public async Task StopReleasesCooldownOnlyWhenTrackExists()
    {
        await SpawnTarget("ComputerShuttle");
        var console = SEntMan.GetEntity(Target!.Value);

        await Server.WaitPost(() =>
        {
            var speaker = SEntMan.SpawnEntity("WFWallmountShipPaSpeaker",
                MapData.GridCoords);
            SEntMan.GetComponent<ApcPowerReceiverComponent>(speaker).NeedsPower = false;
        });
        await RunTicks(20);

        await Activate();
        Assert.That(IsUiOpen(ShuttleConsoleUiKey.Key), Is.True);

        await Server.WaitAssertion(() =>
        {
            var grid = SEntMan.GetComponent<TransformComponent>(console).GridUid!.Value;
            var alert = SEntMan.EnsureComponent<ShipAlertComponent>(grid);
            var ui = SEntMan.System<SharedUserInterfaceSystem>();
            var internet = SEntMan.System<Content.Server._WF.Audio.InternetSound.InternetSoundSystem>();
            var timing = Server.ResolveDependency<IGameTiming>();

            // Begin() records the track and cooldown synchronously. A missing executable prevents
            // network access; any fetch completion is queued to a later server tick.
            var downloader = Server.CfgMan.GetCVar(InternetSoundCVars.YtDlpPath);
            Server.CfgMan.SetCVar(InternetSoundCVars.YtDlpPath, "wolfgate-test-missing-yt-dlp");
            try
            {
                const string url = "https://example.invalid/";
                var request = new ShipPaInternetSoundRequestMessage(url) { Actor = SPlayer };
                ui.RaiseUiMessage(console, ShuttleConsoleUiKey.Key, request);

                Assert.That(internet.IsPlayingOn(grid), Is.True,
                    "The request handler should record a ship track before download completion.");
                Assert.That(alert.NextInternetSound, Is.GreaterThan(timing.CurTime),
                    "A successful request should start the request cooldown.");

                var stop = new ShipPaInternetSoundStopMessage { Actor = SPlayer };
                ui.RaiseUiMessage(console, ShuttleConsoleUiKey.Key, stop);

                Assert.That(internet.IsPlayingOn(grid), Is.False,
                    "A successful stop should remove the pending ship track.");
                Assert.That(alert.NextInternetSound, Is.EqualTo(TimeSpan.Zero),
                    "A successful stop should release the request cooldown.");

                var replacement = new ShipPaInternetSoundRequestMessage(url) { Actor = SPlayer };
                ui.RaiseUiMessage(console, ShuttleConsoleUiKey.Key, replacement);

                Assert.That(internet.IsPlayingOn(grid), Is.True,
                    "A replacement request should be accepted immediately after a successful stop.");
                Assert.That(alert.NextInternetSound, Is.GreaterThan(timing.CurTime));

                // Remove the replacement, then create cooldown state without a track. A second stop is a
                // no-op and must not clear that cooldown.
                ui.RaiseUiMessage(console, ShuttleConsoleUiKey.Key, stop);
                var noOpCooldown = timing.CurTime + TimeSpan.FromSeconds(60);
                alert.NextInternetSound = noOpCooldown;
                ui.RaiseUiMessage(console, ShuttleConsoleUiKey.Key, stop);

                Assert.That(alert.NextInternetSound, Is.EqualTo(noOpCooldown),
                    "A no-op stop must leave the existing cooldown untouched.");
            }
            finally
            {
                Server.CfgMan.SetCVar(InternetSoundCVars.YtDlpPath, downloader);
            }
        });
    }
}
