using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Content.Client.Lobby;
using Content.Server.Humanoid;
using Content.Server.Preferences.Managers;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Migration;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Robust.Client.State;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using static Content.IntegrationTests.Tests._WF.Genitals.GenitalTestHelpers;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>Anatomy survives the client-server round trip, EnsureValid is idempotent with the migration, and exports import.</summary>
[TestFixture]
[TestOf(typeof(HumanoidCharacterProfile))]
public sealed class GenitalProfileRoundTripTest
{
    /// <summary>(a) Client to server, modelled on CharacterCreationTest: NetSerializer and both EnsureValid runs keep anatomy.</summary>
    [Test]
    public async Task NetworkRoundTripTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var client = pair.Client;

        var clientNetManager = client.ResolveDependency<IClientNetManager>();
        var clientStateManager = client.ResolveDependency<IStateManager>();
        var clientPrefManager = client.ResolveDependency<IClientPreferencesManager>();
        var serverPrefManager = server.ResolveDependency<IServerPreferencesManager>();

        // Need to run them in sync to receive the messages.
        await pair.RunTicksSync(1);
        await PoolManager.WaitUntil(client, () => clientStateManager.CurrentState is LobbyState, 600);
        Assert.That(clientNetManager.ServerChannel, Is.Not.Null);
        var userId = clientNetManager.ServerChannel!.UserId;

        var expected = CustomProfile();
        var slot = -1;
        await client.WaitAssertion(() =>
        {
            var before = clientPrefManager.Preferences!.Characters.Keys.ToHashSet();
            var profile = HumanoidCharacterProfile.RandomWithSpecies("Human").WithAge(30).WithGenitals(CustomProfile());
            clientPrefManager.CreateCharacter(profile);
            slot = clientPrefManager.Preferences!.Characters.Keys.Single(k => !before.Contains(k));
        });

        await PoolManager.WaitUntil(server, () => serverPrefManager.GetPreferences(userId).Characters.ContainsKey(slot), maxTicks: 120);

        HumanoidCharacterProfile clientProfile = default!;
        await client.WaitAssertion(() => clientProfile = (HumanoidCharacterProfile) clientPrefManager.Preferences!.Characters[slot]);

        await server.WaitAssertion(() =>
        {
            var serverProfile = (HumanoidCharacterProfile) serverPrefManager.GetPreferences(userId).Characters[slot];
            Assert.Multiple(() =>
            {
                Assert.That(clientProfile.Genitals.MemberwiseEquals(expected), Is.True, "client-side EnsureValid changed anatomy");
                Assert.That(serverProfile.Genitals.MemberwiseEquals(expected), Is.True, "the server copy differs");
                Assert.That(serverProfile.MemberwiseEquals(clientProfile), Is.True);
            });
        });

        // Leave the pooled pair with the characters it had.
        await client.WaitPost(() => clientPrefManager.DeleteCharacter(slot));
        await PoolManager.WaitUntil(server, () => !serverPrefManager.GetPreferences(userId).Characters.ContainsKey(slot), maxTicks: 120);

        await pair.CleanReturnAsync();
    }

    /// <summary>(b) Validated(Validated(p)) equals Validated(p), with the migration on.</summary>
    [Test]
    public async Task EnsureValidIdempotenceTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var session = pair.Player!;
            var legacyOnce = Validated(LegacyProfile(), session);
            var legacyTwice = Validated(legacyOnce, session);
            var customOnce = Validated(MakeProfile().WithGenitals(CustomProfile()), session);
            var customTwice = Validated(customOnce, session);

            Assert.Multiple(() =>
            {
                Assert.That(legacyOnce.Genitals.Version, Is.EqualTo(GenitalProfile.CurrentVersion));
                Assert.That(legacyOnce.Genitals.Penis, Is.Not.Null);
                Assert.That(legacyOnce.Appearance.Markings.Any(m => LegacyGenitalMarkings.IsLegacyId(m.MarkingId)), Is.False);
                Assert.That(legacyTwice.MemberwiseEquals(legacyOnce), Is.True);
                Assert.That(customOnce.Genitals.MemberwiseEquals(CustomProfile()), Is.True);
                Assert.That(customTwice.MemberwiseEquals(customOnce), Is.True);
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>(c) A pre-feature export (no genitals block, legacy ids) converts on import; a new export keeps its anatomy.</summary>
    [Test]
    public async Task ImportExportTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var humanoid = server.System<HumanoidAppearanceSystem>();

        await server.WaitAssertion(() =>
        {
            var session = pair.Player!;

            // Exports made before this feature have no genitals node.
            var oldExport = (MappingDataNode) humanoid.ToDataNode(LegacyProfile());
            var removed = ((MappingDataNode) oldExport["profile"]).Remove("genitals");
            var imported = Import(humanoid, oldExport, session);

            var custom = MakeProfile().WithGenitals(CustomProfile());
            var roundTrip = Import(humanoid, humanoid.ToDataNode(custom), session);

            // LoadFailed is load-time state: an export never carries it, so an imported profile is saved like any edit.
            var failedExport = (MappingDataNode) humanoid.ToDataNode(MakeProfile().WithGenitals(GenitalProfile.Failed(CustomProfile())));
            var failedGenitals = (MappingDataNode) ((MappingDataNode) failedExport["profile"])["genitals"];
            var failedImport = Import(humanoid, failedExport, session);

            Assert.Multiple(() =>
            {
                Assert.That(removed, Is.True, "exports should always write the genitals block");
                Assert.That(imported.Genitals.Version, Is.EqualTo(GenitalProfile.CurrentVersion));
                Assert.That(imported.Genitals.Penis?.Shape, Is.EqualTo(PenisKnotted));
                Assert.That(imported.Genitals.Penis?.Sheath, Is.EqualTo(SheathType.Sheath));
                Assert.That(imported.Genitals.Testicles?.Size, Is.EqualTo(2));
                Assert.That(imported.Genitals.Breasts?.Shape, Is.EqualTo(BreastsPair));
                Assert.That(imported.Genitals.Breasts?.Cup, Is.EqualTo(3));
                Assert.That(imported.Appearance.Markings.Any(m => LegacyGenitalMarkings.IsLegacyId(m.MarkingId)), Is.False);
                Assert.That(roundTrip.Genitals.MemberwiseEquals(custom.Genitals), Is.True);
                Assert.That(failedGenitals.Has("loadFailed"), Is.False, "exports must not write LoadFailed");
                Assert.That(failedImport.Genitals.LoadFailed, Is.False);
                Assert.That(failedImport.Genitals.Penis?.Shape, Is.EqualTo(PenisKnotted));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The server keeps an unreadable column only for the anatomy it loaded from it: a LoadFailed flag that arrives with
    /// other anatomy is cleared, so the save writes what was sent.
    /// </summary>
    [Test]
    public async Task LoadFailedFlagTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var userId = pair.Client.Session!.UserId;

        var crafted = MakeProfile().WithGenitals(GenitalProfile.Failed(CustomProfile()));
        await server.WaitPost(() => prefs.SetProfile(userId, 0, crafted).Wait());

        await server.WaitAssertion(() =>
        {
            var saved = (HumanoidCharacterProfile) prefs.GetPreferences(userId).Characters[0];
            Assert.Multiple(() =>
            {
                Assert.That(saved.Genitals.LoadFailed, Is.False, "A LoadFailed flag alone must not keep the column.");
                Assert.That(saved.Genitals.Penis?.Shape, Is.EqualTo(PenisKnotted), "The anatomy sent is kept.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Equals is value equality (MemberwiseEquals) and equal profiles hash alike; Equals used to call itself.</summary>
    [Test]
    public async Task ProfileEqualityTest()
    {
        await using var pair = await PoolManager.GetServerClient();

        await pair.Server.WaitAssertion(() =>
        {
            var first = MakeProfile().WithGenitals(CustomProfile());
            var second = MakeProfile().WithGenitals(CustomProfile());
            var otherAnatomy = MakeProfile().WithGenitals(CustomProfile().WithWomb(true));
            var otherName = second.WithName("Another Name");

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.SameAs(second));
                Assert.That(first.Equals(second), Is.True, "Independently built identical profiles must be equal.");
                Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()), "Equal profiles must hash alike.");
                Assert.That(first.Equals(otherAnatomy), Is.False, "Different anatomy must not be equal.");
                Assert.That(first.Equals(otherName), Is.False, "A different name must not be equal.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every field away from its default, all valid for an adult human.</summary>
    private static GenitalProfile CustomProfile()
    {
        return GenitalProfile.Empty
            .WithRevealMode(GenitalRevealMode.ClothingRemoval)
            .WithPenis(new PenisProfile(PenisKnotted, 27, SheathType.Sheath, false, Color.FromHex("#AA5566"), true, null,
                GenitalVisibility.ShowThroughClothing))
            .WithTesticles(new TesticlesProfile(TesticleType.External, 4, false, Color.FromHex("#886655")))
            .WithVagina(new VaginaProfile(VaginaHuman, true, null, GenitalVisibility.AlwaysHidden))
            .WithWomb(false)
            .WithBreasts(new BreastsProfile(BreastsPair, 7, true));
    }

    /// <summary>A profile as stored before the feature: no anatomy yet, genital markings instead.</summary>
    private static HumanoidCharacterProfile LegacyProfile()
    {
        var profile = MakeProfile().WithGenitals(GenitalProfile.Unmigrated);
        var markings = new List<Marking>
        {
            new("Genital-Penis-Knotted-3-0", new List<Color> { Color.White }),
            new("Genital-Balls-Sheath2", new List<Color> { Color.White }),
            new("GenitalBreastsPairC", new List<Color> { Color.White }),
        };

        return profile.WithCharacterAppearance(profile.Appearance.WithMarkings(markings));
    }

    /// <summary>Writes the export as YAML text and imports it through FromStream, which runs the legacy migration.</summary>
    private static HumanoidCharacterProfile Import(HumanoidAppearanceSystem humanoid, DataNode export, ICommonSession session)
    {
        using var writer = new StringWriter();
        export.Write(writer);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(writer.ToString()));
        return humanoid.FromStream(stream, session);
    }

    /// <summary>profile.Validated, which runs the legacy migration. Server thread only.</summary>
    private static HumanoidCharacterProfile Validated(HumanoidCharacterProfile profile, ICommonSession session)
    {
        return (HumanoidCharacterProfile) profile.Validated(session, IoCManager.Instance!);
    }
}
