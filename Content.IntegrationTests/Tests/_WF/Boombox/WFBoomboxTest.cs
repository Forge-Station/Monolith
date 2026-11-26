#nullable enable
using Content.Server._WF.Audio.InternetSound;
using Content.Server._WF.Boombox;
using Content.Shared._WF.Audio.InternetSound;
using Content.Shared._WF.Boombox;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Boombox;

/// <summary>The boombox spawns as a sound source, refuses bad links and shows its playing sprite.</summary>
[TestFixture]
[TestOf(typeof(WFBoomboxSystem))]
public sealed class WFBoomboxTest
{
    [Test]
    public async Task BoomboxRefusesBadLinksAndToggles()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var sounds = entMan.System<InternetSoundSystem>();
        var appearance = entMan.System<SharedAppearanceSystem>();

        await server.WaitAssertion(() =>
        {
            var box = entMan.SpawnEntity("WFBoombox", map.GridCoords);
            var source = entMan.GetComponent<InternetSoundSourceComponent>(box);
            Assert.That(entMan.HasComponent<WFBoomboxComponent>(box), Is.True);

            // Plain http and other schemes are turned away before any fetch starts.
            Assert.That(sounds.PlayFromSource("tester", "http://example.com/song", (box, source), out var error), Is.False);
            Assert.That(error, Is.Not.Null);
            Assert.That(sounds.IsPlayingFrom(box), Is.False);
            Assert.That(sounds.StopForSource(box), Is.False);

            var on = new InternetSoundSourceChangedEvent(true);
            entMan.EventBus.RaiseLocalEvent(box, ref on);
            Assert.That(appearance.TryGetData<bool>(box, WFBoomboxVisuals.Playing, out var playing) && playing, Is.True);

            var off = new InternetSoundSourceChangedEvent(false);
            entMan.EventBus.RaiseLocalEvent(box, ref off);
            Assert.That(appearance.TryGetData<bool>(box, WFBoomboxVisuals.Playing, out playing) && playing, Is.False);

            entMan.DeleteEntity(box);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public void PlaylistLinksAndEntries()
    {
        Assert.Multiple(() =>
        {
            Assert.That(InternetSoundDownloader.IsPlaylistLink(new Uri("https://www.youtube.com/playlist?list=PL123")), Is.True);
            Assert.That(InternetSoundDownloader.IsPlaylistLink(new Uri("https://music.youtube.com/watch?list=PL123")), Is.True);
            Assert.That(InternetSoundDownloader.IsPlaylistLink(new Uri("https://www.youtube.com/watch?v=abc&list=PL123")), Is.False,
                "A video in a playlist plays just the video.");
            Assert.That(InternetSoundDownloader.IsPlaylistLink(new Uri("https://www.youtube.com/watch?v=abc")), Is.False);
            Assert.That(InternetSoundDownloader.IsPlaylistLink(new Uri("https://soundcloud.com/artist/sets/album")), Is.True);

            var good = InternetSoundDownloader.ParseEntry("https://www.youtube.com/watch?v=a\t215.0\tnot_live\tpublic\tA\tsong", 420);
            Assert.That(good, Is.Not.Null);
            Assert.That(good!.Title, Is.EqualTo("A\tsong"));
            Assert.That(good.Seconds, Is.EqualTo(215));

            Assert.That(InternetSoundDownloader.ParseEntry("https://www.youtube.com/watch?v=b\tNA\tNA\tNA\t[Private video]", 420), Is.Null);
            Assert.That(InternetSoundDownloader.ParseEntry("https://www.youtube.com/watch?v=c\t900\tnot_live\tpublic\tToo long", 420), Is.Null);
            Assert.That(InternetSoundDownloader.ParseEntry("https://www.youtube.com/watch?v=d\t100\tis_live\tpublic\tStream", 420), Is.Null);
            Assert.That(InternetSoundDownloader.ParseEntry("http://example.com/e\t100\tnot_live\tpublic\tPlain http", 420), Is.Null);
            Assert.That(InternetSoundDownloader.ParseEntry("https://www.youtube.com/watch?v=f\t100\tnot_live\tsubscriber_only\tMembers", 420), Is.Null);
        });
    }
}
