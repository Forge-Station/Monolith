#nullable enable
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._Mono.Company;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Client.Players.PlayTimeTracking;
using Content.IntegrationTests.Pair;
using Content.Server._WF.TypingBubble;
using Content.Server.Database;
using Content.Server.Station.Systems;
using Content.Shared._WF.Biotype;
using Content.Shared._WF.TypingBubble;
using Content.Shared.Chat.TypingIndicator;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WF.TypingBubble;

/// <summary>
/// The profile keeps and saves a typing bubble and a spawned character gets it; the creator's Body card offers every
/// bubble, a two-way biotype switch, a chat font sample in the picked font and any skin colour with Mismatched parts.
/// </summary>
[TestFixture]
[TestOf(typeof(WFTypingBubbleSystem))]
public sealed class TypingBubbleTest
{
    private const string Robot = "robot";
    private const string Lizard = "lizard";

    /// <summary>A green no human skin tone reaches; only Mismatched parts may keep it.</summary>
    private static readonly Color Green = Color.FromHex("#20d040");

    [Test]
    public async Task ProfileTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var db = GetDb(server);
        var user = new NetUserId(Guid.NewGuid());
        HumanoidCharacterProfile robot = default!;

        await server.WaitAssertion(() =>
        {
            var human = HumanoidCharacterProfile.RandomWithSpecies("Human");
            robot = Validated(pair, human.WithTypingIndicator(Robot));

            Assert.Multiple(() =>
            {
                Assert.That(human.TypingIndicator, Is.Null, "A new profile should start on the species' own bubble.");
                Assert.That(robot.TypingIndicator?.Id, Is.EqualTo(Robot), "Validation dropped a real bubble.");
                Assert.That(Validated(pair, human.WithTypingIndicator("WFNoSuchBubble")).TypingIndicator, Is.Null,
                    "An unknown bubble should reset to the species' own.");
                Assert.That(Validated(pair, human.WithTypingIndicator("empty")).TypingIndicator, Is.Null,
                    "The empty bubble is not offered.");
                Assert.That(robot.Clone().TypingIndicator?.Id, Is.EqualTo(Robot), "Copies drop the bubble.");
                Assert.That(robot.MemberwiseEquals(robot.WithTypingIndicator(Lizard)), Is.False);
                Assert.That(WFTypingBubbleRules.FromStored(""), Is.Null);
                Assert.That(WFTypingBubbleRules.FromStored(WFTypingBubbleRules.ToStored(robot.TypingIndicator))?.Id,
                    Is.EqualTo(Robot));
            });
        });

        await db.InitPrefsAsync(user, robot);
        var prefs = await db.GetPlayerPreferencesAsync(user);
        var loaded = (HumanoidCharacterProfile) prefs!.Characters.Single().Value;
        Assert.That(loaded.TypingIndicator?.Id, Is.EqualTo(Robot), "The bubble was not saved.");

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

            var robot = spawning.SpawnPlayerMob(map.GridCoords, job: null, profile.WithTypingIndicator(Robot), station: null);
            var plain = spawning.SpawnPlayerMob(map.GridCoords, job: null, profile, station: null);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<WFTypingBubbleComponent>(robot).Indicator.Id, Is.EqualTo(Robot));
                Assert.That(entMan.HasComponent<WFTypingBubbleComponent>(plain), Is.False,
                    "The species' own bubble needs no override.");
            });

            entMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(robot, profile);
            Assert.That(entMan.HasComponent<WFTypingBubbleComponent>(robot), Is.False,
                "Loading a profile without a pick should take the bubble off.");

            entMan.DeleteEntity(robot);
            entMan.DeleteEntity(plain);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SkinColourTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var human = HumanoidCharacterProfile.RandomWithSpecies("Human");
            human = human.WithCharacterAppearance(human.Appearance.WithSkinColor(Green));

            var mismatched = HumanoidCharacterAppearance.EnsureValid(human.Appearance, "Human", human.Sex, true);
            var matched = HumanoidCharacterAppearance.EnsureValid(human.Appearance, "Human", human.Sex, false);

            var body = entMan.System<StationSpawningSystem>()
                .SpawnPlayerMob(map.GridCoords, job: null, human.WithMismatchedParts(true), station: null);

            Assert.Multiple(() =>
            {
                Assert.That(mismatched.SkinColor, Is.EqualTo(Green), "Mismatched parts should keep any skin colour.");
                Assert.That(matched.SkinColor, Is.Not.EqualTo(Green), "Without it a human keeps to skin tones.");
                Assert.That(entMan.GetComponent<HumanoidAppearanceComponent>(body).SkinColor, Is.EqualTo(Green),
                    "The body lost the colour.");
            });

            entMan.DeleteEntity(body);
        });

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
            var editor = NewEditor(client);
            var human = HumanoidCharacterProfile.RandomWithSpecies("Human");
            editor.SetProfile(human.WithChatFont("WFChatFontIceberg"), null);

            // Typing bubble: the species' own first, then every offered bubble.
            var bubbles = editor.FindControl<GridContainer>("TypingBubbleGrid").Children.Cast<ContainerButton>().ToList();
            var offered = WFTypingBubbleRules.Offered(proto);
            Assert.Multiple(() =>
            {
                Assert.That(bubbles, Has.Count.EqualTo(offered.Count + 1));
                Assert.That(bubbles[0].Pressed, "A profile without a pick should light the species' own box.");
                Assert.That(offered.Select(p => p.ID), Does.Not.Contain("empty"));
            });

            var robot = bubbles.Single(b => b.Name == $"TypingBubble{Robot}");
            Press(robot);
            Assert.Multiple(() =>
            {
                Assert.That(editor.Profile!.TypingIndicator?.Id, Is.EqualTo(Robot), "Picking a bubble didn't change the profile.");
                Assert.That(editor.IsDirty, "Picking a bubble should mark the profile unsaved.");
            });
            Press(bubbles[0]);
            Assert.That(editor.Profile!.TypingIndicator, Is.Null, "The species box should clear the pick.");

            // Biotype: one box per biotype, lit for the species' own.
            var biotypes = editor.FindControl<BoxContainer>("BiotypeSwitch").Children.Cast<ContainerButton>().ToList();
            var synthetic = biotypes.Single(b => b.Name == $"Biotype{WFBiotype.Synthetic}");
            Assert.Multiple(() =>
            {
                Assert.That(biotypes, Has.Count.EqualTo(2), "The biotype switch should have two boxes.");
                Assert.That(biotypes.Single(b => b.Name == $"Biotype{WFBiotype.Organic}").Pressed, "A human starts organic.");
            });
            Press(synthetic);
            Assert.That(editor.Profile!.Biotype, Is.EqualTo(WFBiotype.Synthetic));

            // Chat font: the sample is drawn in the pick, so a different font measures differently.
            var preview = editor.FindControl<RichTextLabel>("ChatFontPreview");
            var iceberg = Measure(preview);
            var fonts = editor.FindControl<OptionButton>("ChatFontButton");
            var picked = (Action<OptionButton.ItemSelectedEventArgs>?) typeof(OptionButton)
                .GetField(nameof(OptionButton.OnItemSelected), BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(fonts);
            picked!.Invoke(new OptionButton.ItemSelectedEventArgs(
                Content.Shared._WF.ChatFont.WFChatFontRules.Ordered(proto).FindIndex(f => f.ID == "WFChatFontMinecraftia"), fonts));
            Assert.That(Measure(preview), Is.Not.EqualTo(iceberg), "The chat font sample isn't drawn in the picked font.");

            // Skin: a human's skin-tone slider becomes a colour picker with Mismatched parts.
            var slider = editor.FindControl<Slider>("Skin");
            var rgb = editor.FindControl<BoxContainer>("RgbSkinColorContainer");
            Assert.That(slider.Visible && !rgb.Visible, "A human should start on the skin-tone slider.");
            editor.FindControl<CheckBox>("MismatchedPartsCheckBox").Pressed = true;
            var toggled = (Action<BaseButton.ButtonToggledEventArgs>?) typeof(BaseButton)
                .GetField(nameof(BaseButton.OnToggled), BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(editor.FindControl<CheckBox>("MismatchedPartsCheckBox"));
            toggled!.Invoke(new BaseButton.ButtonToggledEventArgs(true, editor.FindControl<CheckBox>("MismatchedPartsCheckBox"), ClickArgs()));
            Assert.Multiple(() =>
            {
                Assert.That(editor.Profile!.MismatchedParts, "The checkbox didn't turn the option on.");
                Assert.That(!slider.Visible && rgb.Visible, "Mismatched parts should open the full colour picker.");
                Assert.That(editor.IsDirty, "Turning the option on should mark the profile unsaved.");
            });
        });

        await pair.CleanReturnAsync();
    }

    private static HumanoidCharacterProfile Validated(TestPair pair, HumanoidCharacterProfile profile)
    {
        return (HumanoidCharacterProfile) profile.Validated(pair.Player!, IoCManager.Instance!);
    }

    private static Vector2 Measure(Control control)
    {
        control.InvalidateMeasure();
        control.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
        return control.DesiredSize;
    }

    private static GUIBoundKeyEventArgs ClickArgs()
    {
        return new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, default, true, default, default);
    }

    /// <summary>Lights a box and raises its press the way a click does; the editor isn't laid out, so a real click misses.</summary>
    private static void Press(ContainerButton button)
    {
        button.Pressed = true;
        var pressed = (Action<BaseButton.ButtonEventArgs>?) typeof(BaseButton)
            .GetField(nameof(BaseButton.OnPressed), BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(button);
        pressed!.Invoke(new BaseButton.ButtonEventArgs(button, ClickArgs()));
    }

    private static HumanoidProfileEditor NewEditor(RobustIntegrationTest.ClientIntegrationInstance client)
    {
        return new HumanoidProfileEditor(
            client.ResolveDependency<IClientPreferencesManager>(),
            client.ResolveDependency<IConfigurationManager>(),
            client.ResolveDependency<IEntityManager>(),
            client.ResolveDependency<IFileDialogManager>(),
            client.ResolveDependency<ILogManager>(),
            client.ResolveDependency<IPlayerManager>(),
            client.ResolveDependency<IPrototypeManager>(),
            client.ResolveDependency<IResourceCache>(),
            client.ResolveDependency<JobRequirementsManager>(),
            client.ResolveDependency<MarkingManager>(),
            client.ResolveDependency<CompanyManager>());
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
