#nullable enable
using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Pair;
using Content.Server.Chat.Systems;
using Content.Shared._WF.RMCCore.Deafness;
using Content.Shared.Chat;
using Content.Shared.Mind;
using Content.Shared.Players;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.RMCCore;

/// <summary>A deaf player catches only a few letters of nearby speech and none of a whisper; hearing again, they get it all.</summary>
[TestFixture]
public sealed class DeafSpeechTest
{
    private const string Said = "the quick brown fox jumps over the lazy dog";
    private const string Whispered = "a secret nobody deaf should catch";
    private const string Heard = "every word of this arrives";

    // Chat capitalises speech and ends it with a full stop.
    private const string SaidSent = "The quick brown fox jumps over the lazy dog.";
    private const string HeardSent = "Every word of this arrives.";
    private const int MaxWaitTicks = 120;

    [Test]
    public async Task DeafListenersMissSpeech()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var minds = entMan.System<SharedMindSystem>();
        var chatSys = entMan.System<ChatSystem>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        var chat = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var listener = EntityUid.Invalid;
        var speaker = EntityUid.Invalid;
        // The pooled client keeps chat from earlier tests.
        var start = chat.History.Count;

        await server.WaitPost(() =>
        {
            minds.WipeMind(session.ContentData()?.Mind);
            listener = entMan.SpawnEntity("MobHuman", map.GridCoords);
            speaker = entMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(1f, 0f)));
            minds.TransferTo(minds.CreateMind(session.UserId).Owner, listener);
            entMan.EnsureComponent<DeafComponent>(listener);
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            chatSys.TrySendInGameICMessage(speaker, Whispered, InGameICChatType.Whisper, false);
            chatSys.TrySendInGameICMessage(speaker, Said, InGameICChatType.Speak, false);
        });
        var garbled = await WaitForLocal(pair, chat, start, 1, "a deaf listener to get anything of nearby speech");

        await server.WaitPost(() =>
        {
            entMan.RemoveComponent<DeafComponent>(listener);
            chatSys.TrySendInGameICMessage(speaker, Heard, InGameICChatType.Speak, false);
        });
        var clear = await WaitForLocal(pair, chat, start, 2, "a hearing listener to get nearby speech");

        Assert.Multiple(() =>
        {
            Assert.That(garbled, Is.Not.EqualTo(SaidSent), "a deaf listener heard nearby speech word for word.");
            Assert.That(garbled.Length, Is.EqualTo(SaidSent.Length), "the garbled speech is not the speaker's words with letters missing.");
            Assert.That(chat.History.Skip(start).Any(h => h.Msg.Channel == ChatChannel.Whisper), Is.False, "a deaf listener heard a whisper.");
            Assert.That(clear, Is.EqualTo(HeardSent), "a listener who can hear again still got garbled speech.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Waits for the <paramref name="count"/>th Local message and returns its text.</summary>
    private static async Task<string> WaitForLocal(TestPair pair, ChatUIController chat, int start, int count, string what)
    {
        for (var i = 0; i < MaxWaitTicks; i++)
        {
            var local = chat.History.Skip(start).Where(h => h.Msg.Channel == ChatChannel.Local).ToList();
            if (local.Count >= count)
                return local[count - 1].Msg.Message;

            await pair.RunTicksSync(1);
        }

        Assert.Fail($"Timed out waiting for {what}.");
        return string.Empty;
    }
}
