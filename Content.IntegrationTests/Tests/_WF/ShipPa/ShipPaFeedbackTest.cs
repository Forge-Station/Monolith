#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Power.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.ShipPa;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._WF.ShipPa;

/// <summary>Captures cursor popups delivered to the integration-test client.</summary>
public sealed partial class ShipPaFeedbackObserver : EntitySystem
{
    [Dependency] private INetManager _net = default!;

    public readonly ConcurrentQueue<PopupCursorEvent> CursorPopups = new();

    public override void Initialize()
    {
        base.Initialize();

        if (_net.IsClient)
            SubscribeNetworkEvent<PopupCursorEvent>(OnPopup);
    }

    private void OnPopup(PopupCursorEvent ev)
    {
        CursorPopups.Enqueue(ev);
    }
}

/// <summary>An accepted PA request reports a later downloader failure to the player who made it.</summary>
[TestFixture]
[TestOf(typeof(Content.Server._WF.ShipPa.ShipAlertSystem))]
public sealed class ShipPaFeedbackTest : InteractionTest
{
    /// <summary>Real time allowed for the downloader to fail and the popup to reach the client.</summary>
    private static readonly TimeSpan FailureTimeout = TimeSpan.FromSeconds(30);

    [Test]
    public async Task DelayedInternetSoundFailureReachesRequestingPlayer()
    {
        await SpawnTarget("ComputerShuttle");
        var console = SEntMan.GetEntity(Target!.Value);

        await Server.WaitPost(() =>
        {
            var speaker = SEntMan.SpawnEntity("WFWallmountShipPaSpeaker", MapData.GridCoords);
            SEntMan.GetComponent<ApcPowerReceiverComponent>(speaker).NeedsPower = false;
        });
        await RunTicks(20);

        await Activate();
        Assert.That(IsUiOpen(ShuttleConsoleUiKey.Key), Is.True);

        var observer = Client.System<ShipPaFeedbackObserver>();
        observer.CursorPopups.Clear();
        var oldYtDlpPath = Server.CfgMan.GetCVar(InternetSoundCVars.YtDlpPath);

        try
        {
            await Server.WaitAssertion(() =>
            {
                var grid = SEntMan.GetComponent<TransformComponent>(console).GridUid!.Value;
                var ui = SEntMan.System<SharedUserInterfaceSystem>();
                var internet = SEntMan.System<Content.Server._WF.Audio.InternetSound.InternetSoundSystem>();

                // A missing executable fails before any network request, while still completing asynchronously.
                Server.CfgMan.SetCVar(InternetSoundCVars.YtDlpPath, "wolfgate-test-missing-yt-dlp");
                ui.RaiseUiMessage(console, ShuttleConsoleUiKey.Key,
                    new ShipPaInternetSoundRequestMessage("https://example.invalid/") { Actor = SPlayer });

                Assert.That(internet.IsPlayingOn(grid), Is.True,
                    "The accepted request should remain tracked until its fetch fails.");
            });

            // The fetch fails on a worker thread in real time, and test ticks run as fast as they can,
            // so a tick budget alone can run out before a loaded machine has even tried the executable.
            var timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < FailureTimeout && !observer.CursorPopups.Any(p => p.Type == PopupType.MediumCaution))
            {
                await RunTicks(1);
                await Task.Delay(10);
            }

            Assert.That(observer.CursorPopups, Has.Some.Matches<PopupCursorEvent>(popup =>
                    popup.Type == PopupType.MediumCaution && popup.Message.Contains("yt-dlp wasn't found")),
                "The requesting player should receive the delayed yt-dlp failure popup.");

            await Server.WaitAssertion(() =>
            {
                var grid = SEntMan.GetComponent<TransformComponent>(console).GridUid!.Value;
                var internet = SEntMan.System<Content.Server._WF.Audio.InternetSound.InternetSoundSystem>();
                Assert.That(internet.IsPlayingOn(grid), Is.False,
                    "The failed asynchronous request must remove its active track.");
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.CfgMan.SetCVar(InternetSoundCVars.YtDlpPath, oldYtDlpPath));
        }
    }
}
