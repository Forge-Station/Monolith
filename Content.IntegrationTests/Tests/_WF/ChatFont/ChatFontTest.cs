#nullable enable
using System.Linq;
using System.Reflection;
using Content.Client._Mono.Company;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Client.Players.PlayTimeTracking;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Pair;
using Content.Server._WF.ChatFont;
using Content.Server.Chat.Systems;
using Content.Server.Database;
using Content.Server.Station.Systems;
using Content.Shared._EinsteinEngines.Language;
using Content.Shared._WF.ChatFont;
using Content.Shared.Chat;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Preferences;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WF.ChatFont;

/// <summary>
/// The profile keeps and saves a chat font, a spawned character gets it, its speech is wrapped in it (a language's font
/// still wins) and the client can draw it; the creator lists every font and picking one changes the profile.
/// </summary>
[TestFixture]
[TestOf(typeof(WFChatFontSystem))]
public sealed class ChatFontTest
{
    private const string Iceberg = "WFChatFontIceberg";
    private const string Minecraftia = "WFChatFontMinecraftia";
    private const string IcebergFont = "WFIceberg";
    private const int MaxWaitTicks = 120;
    private static readonly ProtoId<LanguagePrototype> Thaveyan = "Thaveyan";

    private static readonly string[] Offered =
    {
        "WFChatFontDefault", "WFChatFontTerminal", "WFChatFontBoxfont", Iceberg, "WFChatFontFondamento", Minecraftia,
    };

    [Test]
    public async Task ProfileTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var db = GetDb(server);
        var user = new NetUserId(Guid.NewGuid());
        HumanoidCharacterProfile iceberg = default!;

        await server.WaitAssertion(() =>
        {
            var human = HumanoidCharacterProfile.RandomWithSpecies("Human");
            iceberg = (HumanoidCharacterProfile) human.WithChatFont(Iceberg).Validated(pair.Player!, IoCManager.Instance!);
            var unknown = (HumanoidCharacterProfile) human.WithChatFont("WFChatFontNonsense")
                .Validated(pair.Player!, IoCManager.Instance!);

            Assert.Multiple(() =>
            {
                Assert.That(human.ChatFont, Is.EqualTo(WFChatFontRules.Default), "A new profile should start on the default.");
                Assert.That(iceberg.ChatFont.Id, Is.EqualTo(Iceberg), "Validation dropped a real font.");
                Assert.That(unknown.ChatFont, Is.EqualTo(WFChatFontRules.Default), "An unknown font should reset to the default.");
                Assert.That(iceberg.Clone().ChatFont.Id, Is.EqualTo(Iceberg), "Copies drop the font.");
                Assert.That(iceberg.MemberwiseEquals(iceberg.WithChatFont(Minecraftia)), Is.False);
                Assert.That(WFChatFontRules.FromStored(""), Is.EqualTo(WFChatFontRules.Default));
                Assert.That(WFChatFontRules.FromStored(WFChatFontRules.ToStored(Iceberg)).Id, Is.EqualTo(Iceberg));
            });
        });

        await db.InitPrefsAsync(user, iceberg);
        var prefs = await db.GetPlayerPreferencesAsync(user);
        var loaded = (HumanoidCharacterProfile) prefs!.Characters.Single().Value;
        Assert.That(loaded.ChatFont.Id, Is.EqualTo(Iceberg), "The font was not saved.");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OfferedFontsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var serverProto = pair.Server.ResolveDependency<IPrototypeManager>();
        var clientProto = pair.Client.ResolveDependency<IPrototypeManager>();
        var cache = pair.Client.ResolveDependency<IResourceCache>();

        Assert.That(WFChatFontRules.Ordered(serverProto).Select(p => p.ID), Is.EqualTo(Offered),
            "The creator should offer exactly these fonts, in this order.");

        await pair.Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var font in WFChatFontRules.Ordered(clientProto))
                {
                    Assert.That(clientProto.TryIndex<FontPrototype>(font.Font, out var fontProto),
                        $"{font.ID} names the client font {font.Font}, which doesn't exist; chat would fall back to Default.");
#pragma warning disable CS0618 // FontTag still resolves fonts through the prototype's path.
                    if (fontProto != null)
                        Assert.That(cache.TryGetResource<FontResource>(fontProto.Path, out _), $"{fontProto.Path} doesn't load.");
#pragma warning restore CS0618
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpawnTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var spawning = entMan.System<StationSpawningSystem>();
            var profile = HumanoidCharacterProfile.RandomWithSpecies("Human");

            var iceberg = spawning.SpawnPlayerMob(map.GridCoords, job: null, profile.WithChatFont(Iceberg), station: null);
            var plain = spawning.SpawnPlayerMob(map.GridCoords, job: null, profile, station: null);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<WFChatFontComponent>(iceberg).Font, Is.EqualTo(IcebergFont));
                Assert.That(entMan.HasComponent<WFChatFontComponent>(plain), Is.False,
                    "The default font should leave the speech verb's own.");
            });

            entMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(iceberg, profile);
            Assert.That(entMan.HasComponent<WFChatFontComponent>(iceberg), Is.False,
                "Loading a default profile should take the picked font off.");

            entMan.DeleteEntity(iceberg);
            entMan.DeleteEntity(plain);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ChatTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        var chatSys = entMan.System<ChatSystem>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        var chat = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var thaveyan = server.ResolveDependency<IPrototypeManager>().Index(Thaveyan);
        var body = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            mindSys.WipeMind(session.ContentData()?.Mind);
            body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.System<SharedHumanoidAppearanceSystem>()
                .LoadProfile(body, HumanoidCharacterProfile.RandomWithSpecies("Human").WithChatFont(Iceberg));
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, body);
        });

        await server.WaitPost(() =>
            chatSys.TrySendInGameICMessage(body, "icebergs ahead", InGameICChatType.Speak, ChatTransmitRange.Normal));
        var said = await WaitForMessage(pair, chat, "icebergs ahead");
        Assert.That(said.WrappedMessage, Does.Contain($"font=\"{IcebergFont}\""), "Speech is not wrapped in the picked font.");

        // The bubble draws the BubbleContent part of the same markup, so the font has to be inside it.
        await pair.Client.WaitAssertion(() =>
        {
            var content = FormattedMessage.FromMarkupOrThrow(SharedChatSystem.GetStringInsideTag(said, "BubbleContent"));
            Assert.That(content.Nodes.Any(n => n.Name == "font" && n.Value.StringValue == IcebergFont),
                "The speech bubble's content lost the font.");
        });

        await server.WaitPost(() =>
            chatSys.TrySendInGameICMessage(body, "quietly now", InGameICChatType.Whisper, ChatTransmitRange.Normal));
        var whispered = await WaitForMessage(pair, chat, "quietly now");
        Assert.That(whispered.WrappedMessage, Does.Contain($"font=\"{IcebergFont}\""), "A whisper is not in the picked font.");

        var before = chat.History.Count;
        await server.WaitPost(() =>
            chatSys.TrySendInGameICMessage(body, "in my own tongue", InGameICChatType.Speak, ChatTransmitRange.Normal,
                languageOverride: thaveyan));
        await WaitFor(pair, () => chat.History.Count > before && chat.History.Skip(before).Any(IsLocalSpeech),
            "the Thaveyan line");
        var native = chat.History.Skip(before).First(IsLocalSpeech).Msg;
        Assert.Multiple(() =>
        {
            Assert.That(native.WrappedMessage, Does.Contain("font=\"Thaveyan\""), "The language's font should win.");
            Assert.That(native.WrappedMessage, Does.Not.Contain(IcebergFont));
        });

        await server.WaitPost(() => entMan.DeleteEntity(body));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CreatorTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var proto = client.ResolveDependency<IPrototypeManager>();
            var editor = new HumanoidProfileEditor(
                client.ResolveDependency<IClientPreferencesManager>(),
                client.ResolveDependency<IConfigurationManager>(),
                client.ResolveDependency<IEntityManager>(),
                client.ResolveDependency<IFileDialogManager>(),
                client.ResolveDependency<ILogManager>(),
                client.ResolveDependency<IPlayerManager>(),
                proto,
                client.ResolveDependency<IResourceCache>(),
                client.ResolveDependency<JobRequirementsManager>(),
                client.ResolveDependency<MarkingManager>(),
                client.ResolveDependency<CompanyManager>());

            editor.SetProfile(HumanoidCharacterProfile.RandomWithSpecies("Human").WithChatFont(Iceberg), null);
            var button = editor.FindControl<OptionButton>("ChatFontButton");
            var fonts = WFChatFontRules.Ordered(proto);

            Assert.Multiple(() =>
            {
                Assert.That(button.ItemCount, Is.EqualTo(Offered.Length), "The dropdown should list every font.");
                Assert.That(fonts[button.SelectedId].ID, Is.EqualTo(Iceberg), "The dropdown doesn't show the profile's font.");
            });

            // Raise the dropdown's pick the way a click on its popup does.
            var minecraftia = fonts.FindIndex(f => f.ID == Minecraftia);
            var picked = (Action<OptionButton.ItemSelectedEventArgs>?) typeof(OptionButton)
                .GetField(nameof(OptionButton.OnItemSelected), BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(button);
            picked!.Invoke(new OptionButton.ItemSelectedEventArgs(minecraftia, button));

            Assert.Multiple(() =>
            {
                Assert.That(editor.Profile!.ChatFont.Id, Is.EqualTo(Minecraftia), "Picking a font didn't change the profile.");
                Assert.That(button.SelectedId, Is.EqualTo(minecraftia));
                Assert.That(editor.IsDirty, "Picking a font should mark the profile unsaved.");
            });
        });

        await pair.CleanReturnAsync();
    }

    private static bool IsLocalSpeech((Robust.Shared.Timing.GameTick Tick, ChatMessage Msg) entry) =>
        entry.Msg.Channel == ChatChannel.Local;

    private static async Task<ChatMessage> WaitForMessage(TestPair pair, ChatUIController chat, string text)
    {
        bool Match((Robust.Shared.Timing.GameTick Tick, ChatMessage Msg) h) =>
            h.Msg.Message.Contains(text, StringComparison.OrdinalIgnoreCase);

        await WaitFor(pair, () => chat.History.Any(Match), $"\"{text}\"");
        return chat.History.Last(Match).Msg;
    }

    private static async Task WaitFor(TestPair pair, Func<bool> condition, string what)
    {
        for (var i = 0; i < MaxWaitTicks; i++)
        {
            if (condition())
                return;

            await pair.RunTicksSync(1);
        }

        Assert.That(condition(), Is.True, $"Timed out waiting for {what}.");
    }

    private static ServerDbSqlite GetDb(RobustIntegrationTest.ServerIntegrationInstance server)
    {
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var opsLog = server.ResolveDependency<ILogManager>().GetSawmill("db.ops");
        var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        builder.UseSqlite(conn);
        return new ServerDbSqlite(() => builder.Options, true, cfg, true, opsLog);
    }
}
