#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._WF.Audio.InternetSound;
using Content.Server.Power.Components;
using Content.Shared._WF.Audio.InternetSound;
using Moq;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Server.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Network.Transfer;

namespace Content.IntegrationTests.Tests._WF.ShipPa;

[TestOf(typeof(InternetSoundSystem))]
public sealed class ShipPaAudienceTest : InteractionTest
{
    private static int _nextId = 30000;
    private ITransferManager? _realTransfer;
    private int _startedTransfers;

    [TearDown]
    public async Task RestoreTransferProbe()
    {
        if (_realTransfer != null)
        {
            await Server.WaitPost(() => typeof(InternetSoundSystem).GetField("_transfer", Fields)!
                .SetValue(Server.System<InternetSoundSystem>(), _realTransfer));
        }
    }
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public async Task ApproachUsesRotatedHullAndRetainsAssetUntilCut()
    {
        await InitializeTransfer();
        var otherMap = await Pair.CreateTestMap();
        var id = Interlocked.Increment(ref _nextId);
        object track = default!;

        await Server.WaitAssertion(() =>
        {
            // A long ship turned sideways must use its hull, not its origin or an unrotated world box.
            MapSystem.SetTile(MapData.Grid, new Vector2i(200, 0), MapData.Tile.Tile);
            Transform.SetWorldPosition(MapData.Grid.Owner, new Vector2(500, 500));
            Transform.SetWorldRotation(MapData.Grid.Owner, Angle.FromDegrees(90));
            Transform.SetCoordinates(SPlayer, new EntityCoordinates(MapData.MapUid, 2000, 2000));
            AddSpeaker();
            track = PrepareTrack(id, MapData.Grid.Owner);
            Assert.That(Recipients(track), Is.Empty, "Distant players must not be sent PA assets.");
        });
        await RunTicks(40);
        await Client.WaitAssertion(() => Assert.That(Resources().Has(id), Is.False));

        await Server.WaitPost(() =>
            Transform.SetCoordinates(SPlayer, new EntityCoordinates(otherMap.MapUid, 500, 740)));
        await RunTicks(40);
        await Server.WaitAssertion(() => Assert.That(Recipients(track), Is.Empty,
            "Matching coordinates on another map are not a listener."));

        await Server.WaitPost(() =>
        {
            var nearStern = Vector2.Transform(new Vector2(240, 0), Transform.GetWorldMatrix(MapData.Grid.Owner));
            Transform.SetCoordinates(SPlayer, new EntityCoordinates(MapData.MapUid, nearStern));
        });
        await WaitForAsset(id);
        await Server.WaitAssertion(() => Assert.That(Recipients(track), Has.Count.EqualTo(1),
            "An approaching player must prefetch outside speaker audibility, even at a rotated ship's stern."));

        await Server.WaitPost(() => Transform.SetCoordinates(SPlayer, new EntityCoordinates(MapData.MapUid, 2000, 2000)));
        await RunTicks(40);
        await Client.WaitAssertion(() => Assert.That(Resources().Has(id), Is.True,
            "Leaving hearing range must retain the asset until the track ends."));
        await Server.WaitPost(() => Transform.SetCoordinates(SPlayer, MapData.GridCoords));
        await RunTicks(40);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Recipients(track), Has.Count.EqualTo(1));
            Assert.That(_startedTransfers, Is.EqualTo(1), "Leaving/re-entering must not open another transfer stream.");
        });

        await Server.WaitAssertion(() => Assert.That(Server.System<InternetSoundSystem>().StopForGrid(MapData.Grid.Owner), Is.True));
        await RunTicks(20);
        await Client.WaitAssertion(() =>
        {
            Client.System<Content.Client._WF.Audio.InternetSound.InternetSoundSystem>().FrameUpdate(0.3f);
            Assert.That(Resources().Has(id), Is.False, "Stop must release the cached asset.");
        });
    }

    [Test]
    public async Task RemoteViewAndEyeOffsetReceivePaWithoutMovingBody()
    {
        await InitializeTransfer();
        var otherMap = await Pair.CreateTestMap();
        var id = Interlocked.Increment(ref _nextId);
        EntityUid camera = default;
        object track = default!;
        await Server.WaitAssertion(() =>
        {
            Transform.SetCoordinates(SPlayer, new EntityCoordinates(otherMap.MapUid, 0, 0));
            camera = SEntMan.SpawnEntity(null, new EntityCoordinates(MapData.MapUid, 1000, 0));
            SEntMan.AddComponent<EyeComponent>(camera);
            Server.System<ViewSubscriberSystem>().AddViewSubscriber(camera, ServerSession);
            AddSpeaker();
            track = PrepareTrack(id, MapData.Grid.Owner);
            Assert.That(Recipients(track), Is.Empty);
        });
        await RunTicks(40);
        await Client.WaitAssertion(() => Assert.That(Resources().Has(id), Is.False));

        await Server.WaitPost(() =>
        {
            // The camera itself remains distant; only its eye moves near the ship.
            var shipPosition = Transform.GetWorldPosition(MapData.Grid.Owner);
            Server.System<SharedEyeSystem>().SetOffset(camera, shipPosition - new Vector2(1000, 0));
        });
        await WaitForAsset(id);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Recipients(track), Has.Count.EqualTo(1));
            Assert.That(SEntMan.GetComponent<TransformComponent>(SPlayer).MapID, Is.EqualTo(otherMap.MapId));
            Server.System<ViewSubscriberSystem>().RemoveViewSubscriber(camera, ServerSession);
            Server.System<InternetSoundSystem>().StopForGrid(MapData.Grid.Owner);
        });
        await RunTicks(20);
    }

    [Test]
    public async Task GlobalTrackBypassesAudienceAndStartsAfterReady()
    {
        await InitializeTransfer();
        var id = Interlocked.Increment(ref _nextId);
        object track = default!;
        await Server.WaitAssertion(() =>
        {
            Transform.SetCoordinates(SPlayer, new EntityCoordinates(MapData.MapUid, 2000, 2000));
            track = PrepareTrack(id, null);
            Assert.That(Recipients(track), Has.Count.EqualTo(1), "Global sounds must be sent regardless of location.");
            Assert.That(GetField(track, "State")!.ToString(), Is.EqualTo("Sending"));
            Assert.That(GetField(track, "Audio"), Is.Null, "Global playback must wait for readiness.");
        });
        await WaitForAsset(id);
        await RunTicks(10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(GetField(track, "State")!.ToString(), Is.EqualTo("Playing"));
            var audio = (EntityUid) GetField(track, "Audio")!;
            Assert.That(SEntMan.HasComponent<InternetSoundAudioComponent>(audio), Is.True,
                "The readiness acknowledgement must create the ordinary global audio entity.");
            Server.System<InternetSoundSystem>().Stop(null);
        });
        await RunTicks(20);
    }

    [Test]
    public async Task StealthTrackNamesNobody()
    {
        await InitializeTransfer();
        var id = Interlocked.Increment(ref _nextId);
        await Server.WaitPost(() => PrepareTrack(id, null, stealth: true));

        Content.Client._WF.Audio.InternetSound.InternetSoundPopup? popup = null;
        for (var i = 0; i < 240 && popup == null; i++)
        {
            await RunTicks(1);
            await Client.WaitPost(() => popup = (Content.Client._WF.Audio.InternetSound.InternetSoundPopup?) GetField(
                Client.System<Content.Client._WF.Audio.InternetSound.InternetSoundSystem>(), "_popup"));
        }
        Assert.That(popup, Is.Not.Null, "A global sound must open the radio.");

        var playing = false;
        for (var i = 0; i < 240 && !playing; i++)
        {
            await RunTicks(1);
            await Client.WaitPost(() => playing =
                popup!.FindControl<Label>("StatusLabel").Text == Client.ResolveDependency<ILocalizationManager>().GetString("wf-internet-sound-popup-played-stealth"));
        }
        Assert.That(playing, Is.True, "The radio must say it's playing without crediting anyone.");

        await Server.WaitAssertion(() =>
        {
            var system = Server.System<InternetSoundSystem>();
            var state = (InternetSoundStateEvent) typeof(InternetSoundSystem).GetMethod("BuildState", Fields)!.Invoke(system, null)!;
            Assert.That(state.Tracks.Single(t => t.Id == id).Requester, Is.Empty,
                "Other admins' windows must not name a stealth track's requester.");
            system.Stop(null);
        });
        await RunTicks(20);
    }

    [Test]
    public async Task ReconnectedClientReceivesActiveTrackAgain()
    {
        await InitializeTransfer();
        var id = Interlocked.Increment(ref _nextId);
        object track = default!;
        await Server.WaitPost(() =>
        {
            Transform.SetWorldPosition(MapData.Grid.Owner, new Vector2(5000, 5000));
            Transform.SetCoordinates(SPlayer, MapData.GridCoords);
            AddSpeaker();
            track = PrepareTrack(id, MapData.Grid.Owner);
        });
        await WaitForAsset(id);
        var oldChannel = ServerSession.Channel;
        await Pair.Disconnect();
        await Server.WaitAssertion(() => Assert.That(Recipients(track), Does.Not.Contain(oldChannel)));
        await Client.WaitAssertion(() => Assert.That(Resources().Has(id), Is.False));
        await Pair.Connect();
        await Server.WaitPost(() => ServerSession = Server.PlayerMan.GetSessionById(Client.Session!.UserId));
        await InitializeTransfer();
        await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(ServerSession, SPlayer));
        await WaitForAsset(id);
        await Server.WaitAssertion(() =>
        {
            Assert.That(ServerSession.Channel, Is.Not.SameAs(oldChannel));
            Assert.That(_startedTransfers, Is.EqualTo(2), "A new connection must not inherit the previous connection's sent marker.");
            Server.System<InternetSoundSystem>().StopForGrid(MapData.Grid.Owner);
        });
        await RunTicks(20);
    }

    private async Task InitializeTransfer()
    {
        // The in-memory integration connection skips the normal transfer handshake. Run that handshake
        // explicitly so these tests exercise real framing, queueing, receive callbacks and Ready events.
        Task handshake = default!;
        await Server.WaitPost(() =>
        {
            var manager = _realTransfer = Server.ResolveDependency<ITransferManager>();
            // Count actual stream openings while delegating to the real transport. A client ignoring
            // duplicate payloads would otherwise hide server-side retransmission bugs from this test.
            var probe = new Mock<ITransferManager>();
            probe.Setup(x => x.StartTransfer(It.IsAny<INetChannel>(), It.IsAny<TransferStartInfo>()))
                .Returns<INetChannel, TransferStartInfo>((channel, info) =>
                {
                    _startedTransfers++;
                    return manager.StartTransfer(channel, info);
                });
            typeof(InternetSoundSystem).GetField("_transfer", Fields)!.SetValue(Server.System<InternetSoundSystem>(), probe.Object);
            handshake = (Task) manager.GetType().GetMethod("ServerHandshake")!.Invoke(manager, new object[] { ServerSession.Channel })!;
        });
        for (var i = 0; i < 120 && !handshake.IsCompleted; i++)
            await RunTicks(1);
        Assert.That(handshake.IsCompleted, Is.True, "Transfer handshake timed out.");
        await handshake;
    }

    private void AddSpeaker()
    {
        var speaker = SEntMan.SpawnEntity("WFWallmountShipPaSpeaker", MapData.GridCoords);
        SEntMan.GetComponent<ApcPowerReceiverComponent>(speaker).NeedsPower = false;
    }

    private object PrepareTrack(int id, EntityUid? grid, bool stealth = false)
    {
        // Only replace external downloading. Everything from Distribute through the client Ready event
        // runs normally. Reflection is confined to setup because production has no test-only entry point.
        var system = Server.System<InternetSoundSystem>();
        var trackType = typeof(InternetSoundSystem).GetNestedType("Track", BindingFlags.NonPublic)!;
        var track = Activator.CreateInstance(trackType)!;
        using var fetch = new CancellationTokenSource();
        trackType.GetField("Id")!.SetValue(track, id);
        trackType.GetField("Requester")!.SetValue(track, "ShipPaAudienceTest");
        trackType.GetField("Stealth")!.SetValue(track, stealth);
        trackType.GetField("Grid")!.SetValue(track, grid);
        trackType.GetField("Fetch")!.SetValue(track, fetch);
        ((IDictionary) GetField(system, "_tracks")!).Add(id, track);
        if (grid is { } ship)
            ((IDictionary) GetField(system, "_byGrid")!)[ship] = id;
        else
            typeof(InternetSoundSystem).GetField("_global", Fields)!.SetValue(system, id);
        var result = new InternetSoundDownloader.Result("Audience test", TestAudio());
        typeof(InternetSoundSystem).GetMethod("Distribute", Fields)!.Invoke(system, new object[] { id, fetch, "local test audio", result });
        return track;
    }

    private byte[] TestAudio()
    {
        // Shipped mono Ogg, long enough for movement and transfer checks without external downloads.
        using var input = Server.ResolveDependency<IResourceManager>().ContentFileRead("/Audio/Ambience/ambigen12.ogg");
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    private async Task WaitForAsset(int id)
    {
        var arrived = false;
        for (var i = 0; i < 240 && !arrived; i++)
        {
            await RunTicks(1);
            await Client.WaitPost(() => arrived = Resources().Has(id));
        }
        Assert.That(arrived, Is.True, "The eligible client did not receive the audio asset.");
    }

    private InternetSoundResources Resources() => InternetSoundResources.For(Client.ResolveDependency<IResourceCache>());
    private static HashSet<INetChannel> Recipients(object track) => (HashSet<INetChannel>) GetField(track, "Recipients")!;
    private static object? GetField(object instance, string name) => instance.GetType().GetField(name, Fields)!.GetValue(instance);
}
