#nullable enable
using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._WF.Audio.InternetSound;
using Content.Server._WF.ShipPa;
using Content.Server.Power.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.ShipPa;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.ShipPa;

/// <summary>
/// A ship's PA playlist, driven through the shuttle console. Fetching needs yt-dlp, so a missing executable
/// stands in: a link starts and stays fetching until a later tick, which these tests do not run while a
/// track is meant to be playing. Tracks "end" by raising the event the PA raises when one plays out.
/// </summary>
[TestFixture]
[TestOf(typeof(ShipPaPlaylistSystem))]
public sealed class ShipPaPlaylistTest : InteractionTest
{
    private const string MissingDownloader = "wolfgate-test-missing-yt-dlp";

    /// <summary>Real time allowed for a fetch to fail and its popup to reach the client.</summary>
    private static readonly TimeSpan FailureTimeout = TimeSpan.FromSeconds(30);

    private const string First = "https://example.invalid/first";
    private const string Second = "https://example.invalid/second";
    private const string Third = "https://example.invalid/third";
    private const string Fourth = "https://example.invalid/fourth";

    private sealed class Restore : IDisposable
    {
        private readonly Action _undo;

        public Restore(Action undo)
        {
            _undo = undo;
        }

        public void Dispose()
        {
            _undo();
        }
    }

    [Test]
    public async Task FirstLinkStartsAtOnceAndTheRestQueueInOrder()
    {
        var (console, grid, _) = await OpenConsole();

        await Server.WaitAssertion(() =>
        {
            using var downloader = Override(InternetSoundCVars.YtDlpPath, MissingDownloader);
            using var cooldown = Override(InternetSoundCVars.RequestCooldown, 0);
            var internet = SEntMan.System<InternetSoundSystem>();

            Add(console, First);
            Assert.That(internet.IsPlayingOn(grid), Is.True, "Nothing was playing, so the first link should start at once.");
            Assert.That(Playlist(grid).Links, Is.Empty);
            Assert.That(Playlist(grid).Current, Is.Not.Null);
            Assert.That(Playlist(grid).Current!.Value.Label, Is.EqualTo("example.invalid/first"),
                "Until the title is known the panel shows the shortened link.");

            Add(console, Second);
            Add(console, Third);
            Assert.That(Playlist(grid).Links.Select(link => link.Url), Is.EqualTo(new[] { Second, Third }));
            Assert.That(Playlist(grid).Queue.Select(entry => entry.Label),
                Is.EqualTo(new[] { "example.invalid/second", "example.invalid/third" }),
                "Clients get the queue in play order, as short labels.");
            Assert.That(Playlist(grid).Queue.Select(entry => entry.Id), Is.Unique);

            Stop(console);
        });
    }

    [Test]
    public async Task TheNextLinkStartsWhenATrackEnds()
    {
        var (console, grid, _) = await OpenConsole();

        await Server.WaitAssertion(() =>
        {
            using var downloader = Override(InternetSoundCVars.YtDlpPath, MissingDownloader);
            using var cooldown = Override(InternetSoundCVars.RequestCooldown, 0);
            var internet = SEntMan.System<InternetSoundSystem>();

            Add(console, First);
            Add(console, Second);
            Add(console, Third);

            TrackEnds(grid);
            Assert.That(internet.IsPlayingOn(grid), Is.True, "The queue should carry straight on.");
            Assert.That(Playlist(grid).Playing!.Url, Is.EqualTo(Second));
            Assert.That(Playlist(grid).Links.Select(link => link.Url), Is.EqualTo(new[] { Third }));
            Assert.That(Playlist(grid).Current!.Value.Label, Is.EqualTo("example.invalid/second"));

            TrackEnds(grid);
            Assert.That(Playlist(grid).Playing!.Url, Is.EqualTo(Third));
            Assert.That(Playlist(grid).Links, Is.Empty);

            TrackEnds(grid);
            Assert.That(internet.IsPlayingOn(grid), Is.False, "Nothing is left to play.");
            Assert.That(SEntMan.HasComponent<ShipPaPlaylistComponent>(grid), Is.False,
                "An empty playlist should leave the ship.");
        });
    }

    [Test]
    public async Task SkipStartsTheNextLinkAndIsPaced()
    {
        var (console, grid, _) = await OpenConsole();

        await Server.WaitAssertion(() =>
        {
            using var downloader = Override(InternetSoundCVars.YtDlpPath, MissingDownloader);
            using var cooldown = Override(InternetSoundCVars.RequestCooldown, 0);
            var internet = SEntMan.System<InternetSoundSystem>();

            Add(console, First);
            Add(console, Second);
            Add(console, Third);

            Send(console, new ShipPaPlaylistRequestMessage(ShipPaPlaylistAction.Skip));
            Assert.That(Playlist(grid).Playing!.Url, Is.EqualTo(Second));
            Assert.That(Playlist(grid).Links.Select(link => link.Url), Is.EqualTo(new[] { Third }));
            Assert.That(internet.IsPlayingOn(grid), Is.True);

            Send(console, new ShipPaPlaylistRequestMessage(ShipPaPlaylistAction.Skip));
            Assert.That(Playlist(grid).Playing!.Url, Is.EqualTo(Second),
                "Every skip starts a fetch, so a second one straight away should be refused.");

            Playlist(grid).NextSkip = TimeSpan.Zero;
            Send(console, new ShipPaPlaylistRequestMessage(ShipPaPlaylistAction.Skip));
            Assert.That(Playlist(grid).Playing!.Url, Is.EqualTo(Third));
            Assert.That(Playlist(grid).Links, Is.Empty);

            Playlist(grid).NextSkip = TimeSpan.Zero;
            Send(console, new ShipPaPlaylistRequestMessage(ShipPaPlaylistAction.Skip));
            Assert.That(internet.IsPlayingOn(grid), Is.False, "Skipping the last track just ends it.");
            Assert.That(SEntMan.HasComponent<ShipPaPlaylistComponent>(grid), Is.False);

            Send(console, new ShipPaPlaylistRequestMessage(ShipPaPlaylistAction.Skip));
            Assert.That(internet.IsPlayingOn(grid), Is.False, "Skipping a quiet ship does nothing.");
        });
    }

    [Test]
    public async Task RemoveNamesAnEntryByIdAndClearKeepsThePlayingTrack()
    {
        var (console, grid, _) = await OpenConsole();

        await Server.WaitAssertion(() =>
        {
            using var downloader = Override(InternetSoundCVars.YtDlpPath, MissingDownloader);
            using var cooldown = Override(InternetSoundCVars.RequestCooldown, 0);
            var internet = SEntMan.System<InternetSoundSystem>();

            Add(console, First);
            Add(console, Second);
            Add(console, Third);
            Add(console, Fourth);

            var ids = Playlist(grid).Queue.Select(entry => entry.Id).ToArray();
            Send(console, new ShipPaPlaylistRequestMessage(ShipPaPlaylistAction.Remove, ids[1]));
            Assert.That(Playlist(grid).Links.Select(link => link.Url), Is.EqualTo(new[] { Second, Fourth }));

            // A client looking at an older queue can name an entry that has since gone.
            Send(console, new ShipPaPlaylistRequestMessage(ShipPaPlaylistAction.Remove, ids[1]));
            Send(console, new ShipPaPlaylistRequestMessage(ShipPaPlaylistAction.Remove, int.MaxValue));
            Send(console, new ShipPaPlaylistRequestMessage(ShipPaPlaylistAction.Remove, -1));
            Assert.That(Playlist(grid).Links.Select(link => link.Url), Is.EqualTo(new[] { Second, Fourth }),
                "Ids that are not in the queue must change nothing.");

            Send(console, new ShipPaPlaylistRequestMessage(ShipPaPlaylistAction.Clear));
            Assert.That(Playlist(grid).Links, Is.Empty);
            Assert.That(Playlist(grid).Queue, Is.Empty);
            Assert.That(Playlist(grid).Playing!.Url, Is.EqualTo(First));
            Assert.That(internet.IsPlayingOn(grid), Is.True, "Clearing the queue leaves the playing track alone.");

            Stop(console);
        });
    }

    [Test]
    public async Task StopEndsTheTrackAndEmptiesTheQueue()
    {
        var (console, grid, _) = await OpenConsole();

        await Server.WaitAssertion(() =>
        {
            using var downloader = Override(InternetSoundCVars.YtDlpPath, MissingDownloader);
            using var cooldown = Override(InternetSoundCVars.RequestCooldown, 0);
            var internet = SEntMan.System<InternetSoundSystem>();
            var timing = Server.ResolveDependency<IGameTiming>();

            Add(console, First);
            Add(console, Second);
            Add(console, Third);

            var alert = SEntMan.GetComponent<ShipAlertComponent>(grid);
            alert.NextInternetSound = timing.CurTime + TimeSpan.FromSeconds(60);

            Stop(console);
            Assert.That(internet.IsPlayingOn(grid), Is.False);
            Assert.That(SEntMan.HasComponent<ShipPaPlaylistComponent>(grid), Is.False,
                "Stop should leave nothing queued behind it.");
            Assert.That(alert.NextInternetSound, Is.EqualTo(TimeSpan.Zero), "Stopping releases the request cooldown.");
        });
    }

    [Test]
    public async Task TheQueueHoldsAtMostItsCap()
    {
        var (console, grid, _) = await OpenConsole();

        await Server.WaitAssertion(() =>
        {
            using var downloader = Override(InternetSoundCVars.YtDlpPath, MissingDownloader);
            using var cooldown = Override(InternetSoundCVars.RequestCooldown, 0);

            Add(console, First);
            for (var i = 0; i < ShipPaPlaylistComponent.MaxQueued; i++)
            {
                Add(console, $"https://example.invalid/queued/{i}");
            }

            Assert.That(Playlist(grid).Links, Has.Count.EqualTo(ShipPaPlaylistComponent.MaxQueued));

            Add(console, "https://example.invalid/one-too-many");
            Assert.That(Playlist(grid).Links, Has.Count.EqualTo(ShipPaPlaylistComponent.MaxQueued),
                "A full queue must turn further links away.");
            Assert.That(Playlist(grid).Links.Select(link => link.Url), Has.None.EndsWith("one-too-many"));
            Assert.That(Playlist(grid).Queue, Has.Count.EqualTo(ShipPaPlaylistComponent.MaxQueued));

            Stop(console);
        });
    }

    [Test]
    public async Task TheRequestCooldownPacesNewTracksButNotTheQueue()
    {
        var (console, grid, _) = await OpenConsole();

        await Server.WaitAssertion(() =>
        {
            using var downloader = Override(InternetSoundCVars.YtDlpPath, MissingDownloader);

            var alert = SEntMan.GetComponent<ShipAlertComponent>(grid);
            var timing = Server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();
            Add(console, First);
            Assert.That(alert.NextInternetSound, Is.GreaterThan(timing.CurTime), "Starting a track starts the cooldown.");
            var held = alert.NextInternetSound;
            Add(console, Second);
            Add(console, Third);
            Assert.That(Playlist(grid).Links.Select(link => link.Url), Is.EqualTo(new[] { Second, Third }),
                "Links added while a track plays join the queue inside the cooldown.");
            Assert.That(alert.NextInternetSound, Is.EqualTo(held), "Queueing must not extend the cooldown.");

            Stop(console);
            alert.NextInternetSound = timing.CurTime + TimeSpan.FromSeconds(60);
            Add(console, First);
            Assert.That(SEntMan.System<InternetSoundSystem>().IsPlayingOn(grid), Is.False,
                "On a quiet ship a new track is still held to the cooldown.");
            Assert.That(SEntMan.HasComponent<ShipPaPlaylistComponent>(grid) && Playlist(grid).Links.Count > 0, Is.False);
            alert.NextInternetSound = TimeSpan.Zero;
        });
    }

    [Test]
    public async Task ARefusedLinkIsDroppedAndTheQueueMovesOn()
    {
        var (console, grid, _) = await OpenConsole();

        await Server.WaitAssertion(() =>
        {
            using var downloader = Override(InternetSoundCVars.YtDlpPath, MissingDownloader);
            using var cooldown = Override(InternetSoundCVars.RequestCooldown, 0);
            var internet = SEntMan.System<InternetSoundSystem>();

            Add(console, First);
            // Links are checked when their turn comes, as a lone request is.
            Add(console, "http://example.invalid/not-encrypted");
            Add(console, Third);

            TrackEnds(grid);
            Assert.That(Playlist(grid).Playing!.Url, Is.EqualTo(Third), "The queue should step over the link the sound system refused.");
            Assert.That(Playlist(grid).Links, Is.Empty);
            Assert.That(internet.IsPlayingOn(grid), Is.True);

            Stop(console);
        });
    }

    [Test]
    public async Task TheQueueWaitsWhileNoSpeakerWorks()
    {
        var (console, grid, speaker) = await OpenConsole();
        IDisposable downloader = default!;
        IDisposable cooldown = default!;

        // The link starts during later ticks here, so the stand-in downloader has to outlast every block.
        await Server.WaitPost(() =>
        {
            downloader = Override(InternetSoundCVars.YtDlpPath, MissingDownloader);
            cooldown = Override(InternetSoundCVars.RequestCooldown, 0);
        });

        try
        {
            await Server.WaitAssertion(() =>
            {
                Add(console, First);
                Add(console, Second);
                SEntMan.GetComponent<ShipPaSpeakerComponent>(speaker).Broken = true;

                TrackEnds(grid);
                Assert.That(SEntMan.System<InternetSoundSystem>().IsPlayingOn(grid), Is.False);
                Assert.That(Playlist(grid).Links.Select(link => link.Url), Is.EqualTo(new[] { Second }),
                    "Nothing is wrong with the link, so it keeps its place until a speaker works again.");
            });

            // The playlist keeps checking while it waits.
            await RunTicks(40);
            await Server.WaitAssertion(() =>
                Assert.That(Playlist(grid).Links.Select(link => link.Url), Is.EqualTo(new[] { Second })));

            await Server.WaitPost(() => SEntMan.GetComponent<ShipPaSpeakerComponent>(speaker).Broken = false);

            // The link starts on the next check; whether it has since failed to fetch does not matter here.
            await RunTicks(40);
            await Server.WaitAssertion(() =>
            {
                var waiting = SEntMan.TryGetComponent(grid, out ShipPaPlaylistComponent? playlist) ? playlist.Links.Count : 0;
                Assert.That(waiting, Is.Zero, "With a speaker back the waiting link should have been started.");
                Stop(console);
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                cooldown.Dispose();
                downloader.Dispose();
            });
        }
    }

    [Test]
    public async Task BadRequestsChangeNothing()
    {
        var (console, grid, _) = await OpenConsole();

        await Server.WaitAssertion(() =>
        {
            using var downloader = Override(InternetSoundCVars.YtDlpPath, MissingDownloader);
            using var cooldown = Override(InternetSoundCVars.RequestCooldown, 0);
            var internet = SEntMan.System<InternetSoundSystem>();
            var alert = SEntMan.EnsureComponent<ShipAlertComponent>(grid);

            Add(console, string.Empty);
            Add(console, "   ");
            Add(console, "https://example.invalid/" + new string('a', alert.MaxUrlLength));
            Assert.That(internet.IsPlayingOn(grid), Is.False, "Empty and oversized links never reach the sound system.");
            Assert.That(SEntMan.HasComponent<ShipPaPlaylistComponent>(grid), Is.False);

            Add(console, First);
            Add(console, Second);
            Add(console, new string('b', alert.MaxUrlLength + 1));
            Assert.That(Playlist(grid).Links.Select(link => link.Url), Is.EqualTo(new[] { Second }),
                "An oversized link must not be queued.");

            Send(console, new ShipPaPlaylistRequestMessage((ShipPaPlaylistAction) 200, 5));
            Send(console, new ShipPaPlaylistRequestMessage((ShipPaPlaylistAction) 255));
            Assert.That(Playlist(grid).Playing!.Url, Is.EqualTo(First));
            Assert.That(Playlist(grid).Links.Select(link => link.Url), Is.EqualTo(new[] { Second }),
                "An action the server does not know must be ignored.");

            Stop(console);

            SEntMan.System<ShipPaPlaylistSystem>().Remove(grid, 1);
            Assert.That(SEntMan.System<ShipPaPlaylistSystem>().ClearQueued(grid), Is.Zero);
        });
    }

    [Test]
    public async Task PlayerRequestsOffRefusesNewLinks()
    {
        var (console, grid, _) = await OpenConsole();

        await Server.WaitAssertion(() =>
        {
            using var downloader = Override(InternetSoundCVars.YtDlpPath, MissingDownloader);
            using var cooldown = Override(InternetSoundCVars.RequestCooldown, 0);

            Add(console, First);
            Add(console, Second);

            using (Override(InternetSoundCVars.PlayerRequests, false))
            {
                Add(console, Third);
                Assert.That(Playlist(grid).Links.Select(link => link.Url), Is.EqualTo(new[] { Second }),
                    "Switching player requests off stops new links.");

                // Links queued earlier were requests too, so they are dropped when their turn comes.
                TrackEnds(grid);
                Assert.That(SEntMan.System<InternetSoundSystem>().IsPlayingOn(grid), Is.False);
                Assert.That(SEntMan.HasComponent<ShipPaPlaylistComponent>(grid), Is.False);
            }
        });
    }

    [Test]
    public async Task StoppingEveryInternetSoundEmptiesEveryShipsQueue()
    {
        var (console, grid, _) = await OpenConsole();

        await Server.WaitAssertion(() =>
        {
            using var downloader = Override(InternetSoundCVars.YtDlpPath, MissingDownloader);
            using var cooldown = Override(InternetSoundCVars.RequestCooldown, 0);
            var internet = SEntMan.System<InternetSoundSystem>();

            Add(console, First);
            Add(console, Second);
            Assert.That(Playlist(grid).Links, Has.Count.EqualTo(1));

            internet.Stop(null);
            Assert.That(internet.IsPlayingOn(grid), Is.False);
            Assert.That(SEntMan.HasComponent<ShipPaPlaylistComponent>(grid), Is.False,
                "Stopping every internet sound must empty the queues too, or they would start again.");
        });
    }

    [Test]
    public async Task TheQueueWaitsWhileTheServerIsAtItsTrackCap()
    {
        var (console, grid, _) = await OpenConsole();
        IDisposable downloader = default!;
        IDisposable cooldown = default!;
        IDisposable cap = default!;

        await Server.WaitPost(() =>
        {
            downloader = Override(InternetSoundCVars.YtDlpPath, MissingDownloader);
            cooldown = Override(InternetSoundCVars.RequestCooldown, 0);
        });

        try
        {
            await Server.WaitAssertion(() =>
            {
                Add(console, First);
                Add(console, Second);
                Add(console, Third);

                // No track has room, whichever link is tried.
                cap = Override(InternetSoundCVars.MaxConcurrent, 0);
                TrackEnds(grid);
                Assert.That(SEntMan.System<InternetSoundSystem>().IsPlayingOn(grid), Is.False);
                Assert.That(Playlist(grid).Links.Select(link => link.Url), Is.EqualTo(new[] { Second, Third }),
                    "A full server says nothing about the links, so none may be dropped for it.");
            });

            await RunTicks(40);
            await Server.WaitAssertion(() =>
                Assert.That(Playlist(grid).Links.Select(link => link.Url), Is.EqualTo(new[] { Second, Third }),
                    "The playlist keeps checking while it waits, and must not use the queue up."));

            await Server.WaitPost(() => cap.Dispose());

            // The next link starts on the next check; whether it has since failed to fetch does not matter here.
            await RunTicks(40);
            await Server.WaitAssertion(() =>
            {
                var waiting = SEntMan.TryGetComponent(grid, out ShipPaPlaylistComponent? playlist) ? playlist.Links.Count : 0;
                Assert.That(waiting, Is.LessThan(2), "With room again the waiting links should start in turn.");
                Stop(console);
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                cap?.Dispose();
                cooldown.Dispose();
                downloader.Dispose();
            });
        }
    }

    [Test]
    public async Task LinksThatFailToFetchLaterAreDroppedAndTheQueueMovesOn()
    {
        var (console, grid, _) = await OpenConsole();
        var observer = Client.System<ShipPaFeedbackObserver>();
        observer.CursorPopups.Clear();
        IDisposable downloader = default!;
        IDisposable cooldown = default!;

        await Server.WaitPost(() =>
        {
            downloader = Override(InternetSoundCVars.YtDlpPath, MissingDownloader);
            cooldown = Override(InternetSoundCVars.RequestCooldown, 0);
        });

        try
        {
            await Server.WaitAssertion(() =>
            {
                Add(console, First);
                Add(console, Second);
                Assert.That(Playlist(grid).Links.Select(link => link.Url), Is.EqualTo(new[] { Second }));
            });

            // Each fetch fails on a worker thread in real time, so the loop is paced by the clock and not the tick count.
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (timeout.Elapsed < FailureTimeout && observer.CursorPopups.Count(popup => popup.Type == PopupType.MediumCaution) < 2)
            {
                await RunTicks(1);
                await Task.Delay(10);
            }

            Assert.That(observer.CursorPopups.Count(popup => popup.Type == PopupType.MediumCaution), Is.EqualTo(2),
                "Both links were tried and each told its requester why it was dropped.");

            await RunTicks(40);
            await Server.WaitAssertion(() =>
            {
                Assert.That(SEntMan.System<InternetSoundSystem>().IsPlayingOn(grid), Is.False);
                Assert.That(SEntMan.HasComponent<ShipPaPlaylistComponent>(grid), Is.False,
                    "With every link failed and dropped nothing is left on the ship.");
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                cooldown.Dispose();
                downloader.Dispose();
            });
        }
    }

    /// <summary>Opens the helm of a ship with one working speaker.</summary>
    private async Task<(EntityUid Console, EntityUid Grid, EntityUid Speaker)> OpenConsole()
    {
        await SpawnTarget("ComputerShuttle");
        var console = SEntMan.GetEntity(Target!.Value);
        var speaker = EntityUid.Invalid;
        var grid = EntityUid.Invalid;

        await Server.WaitPost(() =>
        {
            speaker = SEntMan.SpawnEntity("WFWallmountShipPaSpeaker", MapData.GridCoords);
            SEntMan.GetComponent<ApcPowerReceiverComponent>(speaker).NeedsPower = false;
            grid = SEntMan.GetComponent<TransformComponent>(console).GridUid!.Value;
        });
        await RunTicks(20);

        await Activate();
        Assert.That(IsUiOpen(ShuttleConsoleUiKey.Key), Is.True);
        return (console, grid, speaker);
    }

    private ShipPaPlaylistComponent Playlist(EntityUid grid)
    {
        return SEntMan.GetComponent<ShipPaPlaylistComponent>(grid);
    }

    private void Send(EntityUid console, BoundUserInterfaceMessage message)
    {
        message.Actor = SPlayer;
        SEntMan.System<SharedUserInterfaceSystem>().RaiseUiMessage(console, ShuttleConsoleUiKey.Key, message);
    }

    private void Add(EntityUid console, string url)
    {
        Send(console, new ShipPaInternetSoundRequestMessage(url));
    }

    private void Stop(EntityUid console)
    {
        Send(console, new ShipPaInternetSoundStopMessage());
    }

    /// <summary>Raises what the PA raises when a track has played through.</summary>
    private void TrackEnds(EntityUid grid)
    {
        var ev = new ShipPaTrackFinishedEvent(grid, InternetSoundSystem.TrackKey);
        SEntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
    }

    private IDisposable Override<T>(CVarDef<T> cvar, T value) where T : notnull
    {
        var old = Server.CfgMan.GetCVar(cvar);
        Server.CfgMan.SetCVar(cvar, value);
        return new Restore(() => Server.CfgMan.SetCVar(cvar, old));
    }
}
