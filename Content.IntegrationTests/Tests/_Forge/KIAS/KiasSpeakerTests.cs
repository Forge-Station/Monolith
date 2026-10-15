using System.Collections.Generic;
using System.Linq;
using Content.Client.UserInterface.Systems.Chat;
using Content.Server._Forge.KIAS;
using Content.Server.Power.Components;
using Content.Server.DeviceLinking.Systems;
using Content.Shared._Forge.KIAS;
using Content.Shared.Chat;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Mind;
using Robust.Client.UserInterface;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasSpeakerTests
{
    [Test]
    public async Task WarningAudioAndLocalSpeechComeFromAddressedSpeaker()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var received = new List<ChatMessage>();
        EntityUid speaker = default;
        EntityUid other = default;
        EntityUid scanner = default;
        EntityUid rotary = default;
        EntityUid core = default;
        NetEntity speakerNet = default;
        NetEntity otherNet = default;
        await pair.Client.WaitAssertion(() =>
            pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>().MessageAdded += received.Add);
        await pair.Server.WaitAssertion(() =>
        {
            var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
            var body = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 1.5f, 0.5f));
            var mind = em.System<SharedMindSystem>().CreateMind(session.UserId);
            em.System<SharedMindSystem>().TransferTo(mind, body);
            for (var x = 0; x < 5; x++)
            {
                em.System<SharedMapSystem>().SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
                em.SpawnEntity("KiasDataCable", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
            }
            EntityUid Spawn(string prototype, int x)
            {
                var uid = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid);
                return uid;
            }
            core = Spawn("KiasCore", 0);
            speaker = Spawn("KiasSpeaker", 2);
            other = Spawn("KiasSpeaker", 3);
            scanner = Spawn("KiasRoomScanner", 4);
            rotary = Spawn("KiasRotarySwitch", 4);
            em.GetComponent<KiasSpeakerComponent>(speaker).Group = "MEDICAL";
            em.GetComponent<KiasSpeakerComponent>(other).Group = "ENGINEERING";
            speakerNet = em.GetNetEntity(speaker);
            otherNet = em.GetNetEntity(other);
            em.System<KiasSystem>().Rebuild(map.Grid);
        });
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            var warning = new KiasAnnouncementEvent(map.Grid, "KIAS regression warning", true, Group: "MEDICAL");
            Assert.DoesNotThrow(() => em.EventBus.RaiseLocalEvent(map.Grid, ref warning, true));
        });
        await pair.RunTicksSync(5);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(received.Any(msg => msg.SenderEntity == speakerNet && msg.Channel == ChatChannel.Local && !msg.HideChat), Is.True,
                "Local speech with the speaker entity must reach the native speech-bubble route.");
            Assert.That(received.Any(msg => msg.SenderEntity == otherNet), Is.False);
            Assert.That(received.Any(msg => msg.Channel == ChatChannel.Radio && msg.Message.Contains("KIAS regression")), Is.False);
            received.Clear();
        });
        await pair.Server.WaitAssertion(() =>
        {
            em.GetComponent<KiasSpeakerComponent>(speaker).Links.Add(new KiasSpeakerLink { Source = scanner, SourcePort = "KiasMotion", Message = "KIAS custom linked message" });
            var signal = new SignalReceivedEvent("KiasAnnounce", scanner, SourcePort: "KiasMotion");
            em.EventBus.RaiseLocalEvent(speaker, ref signal);
        });
        await pair.RunTicksSync(5);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(received.Any(msg => msg.SenderEntity == speakerNet && msg.Channel == ChatChannel.Local), Is.True);
            Assert.That(received.Any(msg => msg.SenderEntity == otherNet), Is.False, "A direct link must speak only from its sink speaker.");
        });
        await pair.Server.WaitAssertion(() =>
        {
            var links = em.System<DeviceLinkSystem>();
            links.LinkDefaults(rotary, rotary, speaker);
            Assert.That(links.GetLinks(rotary, speaker).Any(link => link.source.ToString() == "KiasPosition3" && link.sink.ToString() == "KiasAnnounce"), Is.True);
            var component = em.GetComponent<KiasSpeakerComponent>(speaker);
            component.Message = "KIAS configured switch message";
            component.Links.Add(new KiasSpeakerLink { Source = rotary, SourcePort = "KiasPosition3", Message = "" });
            links.SendSignal(rotary, "KiasPosition3", true);
        });
        await pair.RunTicksSync(5);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(received.Any(msg => msg.SenderEntity == speakerNet && msg.Message.Contains("KIAS configured switch message")), Is.True);
        });
        await pair.Server.WaitAssertion(() =>
        {
            em.System<KiasSystem>().SetEnabled(core, false);
            Assert.That(em.GetComponent<KiasSpeakerComponent>(speaker).Tone, Is.Null);
            Assert.That(em.GetComponent<KiasSpeakerComponent>(other).Tone, Is.Null);
        });
        await pair.RunTicksSync(5);
        await pair.Client.WaitAssertion(received.Clear);
        await pair.Server.WaitAssertion(() =>
        {
            var warning = new KiasAnnouncementEvent(map.Grid, "KIAS offline warning", true);
            em.EventBus.RaiseLocalEvent(map.Grid, ref warning, true);
        });
        await pair.RunTicksSync(5);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(received.Any(msg => msg.SenderEntity == speakerNet || msg.SenderEntity == otherNet), Is.False);
            pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>().MessageAdded -= received.Add;
        });
        await pair.CleanReturnAsync();
    }
}
