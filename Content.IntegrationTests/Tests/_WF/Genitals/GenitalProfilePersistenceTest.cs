using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Genitals;
using Content.Server.Database;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Migration;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.UnitTesting;
using static Content.IntegrationTests.Tests._WF.Genitals.GenitalTestHelpers;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>The anatomy column through the DB layer, the sanitised (Validated) path, damaged columns and unsanitised write-backs.</summary>
/// <remarks>ConvertProfiles does not run EnsureValid, so the DB layer and the Validated path are asserted separately.</remarks>
[TestFixture]
[TestOf(typeof(GenitalProfileJson))]
[TestOf(typeof(ServerDbBase))]
public sealed class GenitalProfilePersistenceTest
{
    private const string LegacyPenis = "Genital-Penis-Knotted-3-0";
    private const string LegacyTesticles = "Genital-Balls-Sheath2";
    private const string LegacyBreasts = "GenitalBreastsPairC";

    /// <summary>Truncated JSON, as a damaged row would hold.</summary>
    private const string CorruptJson = "{\"v\":1,\"penis\":{\"shape\":\"GenitalShapePenisKno";

    [Test]
    public async Task JsonTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var log = QuietLog(server);

        await server.WaitAssertion(() =>
        {
            var full = FullProfile().AsMigrated(new List<string> { "Genital-Penis-Knotted-3-0@#FFFFFFFF" });
            var fullJson = GenitalProfileJson.Serialize(full);
            var fullBack = GenitalProfileJson.Deserialize(fullJson, log, 1);

            var custom = FullProfile()
                .WithRevealMode(GenitalRevealMode.ClothingRemoval)
                .WithPenis(new PenisProfile(PenisHemi, 33, SheathType.Slit, false, Color.FromHex("#3366CC"), false,
                    Color.FromHex("#112233"), GenitalVisibility.ShowThroughClothing))
                .WithTesticles(new TesticlesProfile(TesticleType.Internal, 2, false, Color.FromHex("#445566")))
                .WithWomb(false);
            var customBack = GenitalProfileJson.Deserialize(GenitalProfileJson.Serialize(custom), log, 2);
            var emptyBack = GenitalProfileJson.Deserialize(GenitalProfileJson.Serialize(GenitalProfile.Empty), log, 3);

            var notJson = GenitalProfileJson.Deserialize("{not json", log, 4);
            var notObject = GenitalProfileJson.Deserialize("[1, 2]", log, 5);
            var badOrgan = GenitalProfileJson.Deserialize(
                "{\"v\":1,\"penis\":{\"shape\":\"GenitalShapePenisKnotted\",\"lengthCm\":\"long\"},\"vagina\":{\"shape\":\"GenitalShapeVaginaHuman\"},\"womb\":true}",
                log, 6);
            var badEnum = GenitalProfileJson.Deserialize(
                "{\"v\":1,\"breasts\":{\"shape\":\"GenitalShapeBreastsPair\",\"visibility\":\"Sideways\"},\"testicles\":{\"type\":\"External\"}}",
                log, 7);
            var extraProperty = GenitalProfileJson.Deserialize("{\"v\":1,\"reveal\":\"ClothingRemoval\",\"future\":{\"x\":1},\"womb\":false}", log, 8);
            var noVersion = GenitalProfileJson.Deserialize("{\"reveal\":\"UndergarmentRemoval\"}", log, 9);
            var newer = GenitalProfileJson.Deserialize(
                "{\"v\":2,\"reveal\":\"ClothingRemoval\",\"breasts\":{\"shape\":\"GenitalShapeBreastsPair\",\"cup\":4}}", log, 11);

            // An edited unmigrated profile is written as the current schema, so the edit is not lost.
            var editedUnmigrated = GenitalProfile.Unmigrated.WithPenis(new PenisProfile(PenisKnotted));
            var editedJson = GenitalProfileJson.Serialize(editedUnmigrated);
            var editedBack = GenitalProfileJson.Deserialize(editedJson, log, 10);
            var revealOnlyJson = GenitalProfileJson.Serialize(GenitalProfile.Unmigrated.WithRevealMode(GenitalRevealMode.ClothingRemoval));

            Assert.Multiple(() =>
            {
                // Unmigrated profiles keep the column empty, so the bank write-back never marks them migrated.
                Assert.That(GenitalProfileJson.Serialize(GenitalProfile.Unmigrated), Is.Empty);
                Assert.That(GenitalProfileJson.Serialize(GenitalProfile.Failed(GenitalProfile.Unmigrated)), Is.Empty);
                Assert.That(GenitalProfileJson.Deserialize("", log, 0), Is.Null);
                Assert.That(GenitalProfileJson.Deserialize("   ", log, 0), Is.Null);
                Assert.That(GenitalProfileJson.Deserialize(null, log, 0), Is.Null);

                // Round trips, with enum names and the legacy strings.
                Assert.That(fullJson, Does.Contain("\"v\":1"));
                Assert.That(fullJson, Does.Contain("\"sheath\":\"Sheath\""));
                Assert.That(fullBack?.MemberwiseEquals(full), Is.True);
                Assert.That(fullBack?.LegacyMarkings, Is.EqualTo(full.LegacyMarkings));
                Assert.That(fullBack?.LoadFailed, Is.False);
                Assert.That(customBack?.MemberwiseEquals(custom), Is.True);
                Assert.That(emptyBack?.MemberwiseEquals(GenitalProfile.Empty), Is.True);

                // Not JSON, or not an object: nothing readable.
                Assert.That(notJson?.LoadFailed, Is.True);
                Assert.That(notJson?.IsEmpty, Is.True);
                Assert.That(notObject?.LoadFailed, Is.True);

                // One unreadable organ never discards the others.
                Assert.That(badOrgan?.LoadFailed, Is.True);
                Assert.That(badOrgan?.Penis, Is.Null);
                Assert.That(badOrgan?.Vagina?.Shape, Is.EqualTo(VaginaHuman));
                Assert.That(badOrgan?.Womb, Is.True);
                Assert.That(badEnum?.LoadFailed, Is.True);
                Assert.That(badEnum?.Breasts, Is.Null);
                Assert.That(badEnum?.Testicles?.Type, Is.EqualTo(TesticleType.External));

                // Unknown properties are ignored; a missing version is schema 1, whatever CurrentVersion is.
                Assert.That(extraProperty?.LoadFailed, Is.False);
                Assert.That(extraProperty?.RevealMode, Is.EqualTo(GenitalRevealMode.ClothingRemoval));
                Assert.That(noVersion?.Version, Is.EqualTo(1));
                Assert.That(noVersion?.LoadFailed, Is.False);

                // A newer schema is read, but flagged, so saves keep the fields this build does not know.
                Assert.That(newer?.LoadFailed, Is.True);
                Assert.That(newer?.RevealMode, Is.EqualTo(GenitalRevealMode.ClothingRemoval));
                Assert.That(newer?.Breasts?.Cup, Is.EqualTo(4));

                // Only an untouched unmigrated profile stays "".
                Assert.That(editedJson, Does.Contain($"\"v\":{GenitalProfile.CurrentVersion}"));
                Assert.That(editedBack?.Version, Is.EqualTo(GenitalProfile.CurrentVersion));
                Assert.That(editedBack?.Penis?.Shape, Is.EqualTo(PenisKnotted));
                Assert.That(revealOnlyJson, Does.Contain("\"reveal\":\"ClothingRemoval\""));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RoundTripTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var (db, _) = GetDb(server);
        var user = new NetUserId(Guid.NewGuid());

        // Colours between 8-bit steps: the validator quantises them, so the reload compares exactly.
        var odd = new Color(0.1234f, 0.5555f, 0.9876f);
        HumanoidCharacterProfile validated = default!;
        await server.WaitAssertion(() =>
        {
            var genitals = FullProfile()
                .WithPenis(new PenisProfile(PenisKnotted, 22, SheathType.Sheath, false, odd, false, odd))
                .WithTesticles(new TesticlesProfile(TesticleType.External, 3, false, odd));
            validated = (HumanoidCharacterProfile) MakeProfile().WithGenitals(genitals).Validated(pair.Player!, IoCManager.Instance!);
        });

        await db.InitPrefsAsync(user, validated);
        var loaded = await Load(db, user);

        Assert.Multiple(() =>
        {
            Assert.That(validated.Genitals.Penis?.Color, Is.Not.EqualTo(odd));
            Assert.That(loaded.Genitals.MemberwiseEquals(validated.Genitals), Is.True);
            Assert.That(loaded.MemberwiseEquals(validated), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LegacyRowTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var (db, options) = GetDb(server);
        var user = new NetUserId(Guid.NewGuid());

        await db.InitPrefsAsync(user, LegacyProfile());

        // DB layer: an empty column loads as Unmigrated, with the raw markings.
        var column = await ReadColumn(options, user);
        var loaded = await Load(db, user);
        Assert.Multiple(() =>
        {
            Assert.That(column, Is.Empty);
            Assert.That(loaded.Genitals.Version, Is.EqualTo(0));
            Assert.That(loaded.Appearance.Markings.Select(m => m.MarkingId), Does.Contain(LegacyPenis));
        });

        // Unsanitised write-back (the offline bank path): the column stays unmigrated.
        await db.SaveCharacterSlotAsync(user, loaded.WithBankBalance(1234), 0);
        var afterBank = await ReadColumn(options, user);
        var reloaded = await Load(db, user);
        Assert.Multiple(() =>
        {
            Assert.That(afterBank, Is.Empty);
            Assert.That(reloaded.Genitals.Version, Is.EqualTo(0));
            Assert.That(reloaded.BankBalance, Is.EqualTo(1234));
        });

        // Sanitised path (ServerPreferencesManager.SanitizePreferences): converted.
        HumanoidCharacterProfile sanitised = default!;
        await server.WaitAssertion(() => sanitised = Validated(reloaded, pair.Player!));
        Assert.Multiple(() =>
        {
            Assert.That(sanitised.Genitals.Version, Is.EqualTo(GenitalProfile.CurrentVersion));
            Assert.That(sanitised.Genitals.Penis?.Shape, Is.EqualTo(PenisKnotted));
            Assert.That(sanitised.Genitals.Penis?.LengthCm, Is.EqualTo(28));
            Assert.That(sanitised.Genitals.Penis?.Sheath, Is.EqualTo(SheathType.Sheath));
            Assert.That(sanitised.Genitals.Testicles?.Size, Is.EqualTo(2));
            Assert.That(sanitised.Genitals.Breasts?.Cup, Is.EqualTo(3));
            Assert.That(sanitised.Appearance.Markings.Any(m => LegacyGenitalMarkings.IsLegacyId(m.MarkingId)), Is.False);
            Assert.That(sanitised.Genitals.LegacyMarkings, Has.Count.EqualTo(3));
        });

        // Saving the converted profile writes version-1 JSON, which loads back equal.
        await db.SaveCharacterSlotAsync(user, sanitised, 0);
        var converted = await ReadColumn(options, user);
        var final = await Load(db, user);
        Assert.Multiple(() =>
        {
            Assert.That(converted, Does.StartWith("{"));
            Assert.That(final.Genitals.MemberwiseEquals(sanitised.Genitals), Is.True);
            Assert.That(final.Genitals.LegacyMarkings, Is.EqualTo(sanitised.Genitals.LegacyMarkings));
            Assert.That(final.Appearance.Markings.Any(m => LegacyGenitalMarkings.IsLegacyId(m.MarkingId)), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CorruptColumnTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var (db, options) = GetDb(server);
        var user = new NetUserId(Guid.NewGuid());

        await db.InitPrefsAsync(user, LegacyProfile());
        await WriteColumn(options, user, CorruptJson);

        var loaded = await Load(db, user);
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Genitals.LoadFailed, Is.True);
            Assert.That(loaded.Genitals.IsEmpty, Is.True);
        });

        // Saves that do not edit anatomy keep the column byte for byte, including the bank write-back.
        await db.SaveCharacterSlotAsync(user, loaded.WithName("Renamed Person").WithBankBalance(50), 0);
        Assert.That(await ReadColumn(options, user), Is.EqualTo(CorruptJson));

        // The sanitised path never migrates a damaged column. The old ids are no longer marking prototypes, so marking
        // validation drops them instead of converting them.
        HumanoidCharacterProfile sanitised = default!;
        await server.WaitAssertion(() => sanitised = Validated(loaded, pair.Player!));
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Appearance.Markings.Select(m => m.MarkingId), Does.Contain(LegacyPenis), "Precondition: the DB layer keeps raw markings.");
            Assert.That(sanitised.Genitals.LoadFailed, Is.True);
            Assert.That(sanitised.Genitals.Penis, Is.Null);
            Assert.That(sanitised.Genitals.LegacyMarkings, Is.Null);
            Assert.That(sanitised.Appearance.Markings.Select(m => m.MarkingId), Does.Not.Contain(LegacyPenis));
        });

        await db.SaveCharacterSlotAsync(user, sanitised, 0);
        Assert.That(await ReadColumn(options, user), Is.EqualTo(CorruptJson));

        // An anatomy edit clears LoadFailed, so the next save replaces the column.
        var edited = sanitised.WithGenitals(sanitised.Genitals.WithPenis(new PenisProfile(PenisKnotted)));
        await db.SaveCharacterSlotAsync(user, edited, 0);
        var replaced = await ReadColumn(options, user);
        var final = await Load(db, user);
        Assert.Multiple(() =>
        {
            Assert.That(replaced, Is.Not.EqualTo(CorruptJson));
            Assert.That(final.Genitals.LoadFailed, Is.False);
            Assert.That(final.Genitals.Penis?.Shape, Is.EqualTo(PenisKnotted));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A column this build cannot fully honour, from a newer schema or naming a shape that no longer exists, survives the
    /// sanitised path flagged LoadFailed, so saves that do not edit anatomy, such as the bank write-back, keep it.
    /// </summary>
    [Test]
    public async Task KeptColumnTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var (db, options) = GetDb(server);

        var columns = new[]
        {
            "{\"v\":2,\"breasts\":{\"shape\":\"GenitalShapeBreastsPair\",\"cup\":4},\"future\":true}",
            "{\"v\":1,\"penis\":{\"shape\":\"GenitalShapePenisRenamed\"},\"breasts\":{\"shape\":\"GenitalShapeBreastsPair\",\"cup\":4}}",
        };

        foreach (var column in columns)
        {
            var user = new NetUserId(Guid.NewGuid());
            await db.InitPrefsAsync(user, MakeProfile().WithGenitals(GenitalProfile.Empty));
            await WriteColumn(options, user, column);

            var loaded = await Load(db, user);
            HumanoidCharacterProfile sanitised = default!;
            await server.WaitAssertion(() => sanitised = Validated(loaded, pair.Player!));
            Assert.Multiple(() =>
            {
                Assert.That(sanitised.Genitals.LoadFailed, Is.True, column);
                Assert.That(sanitised.Genitals.Penis, Is.Null, column);
                Assert.That(sanitised.Genitals.Breasts?.Cup, Is.EqualTo(4), column);
            });

            await db.SaveCharacterSlotAsync(user, sanitised.WithBankBalance(77), 0);
            Assert.That(await ReadColumn(options, user), Is.EqualTo(column), $"A save that edits no anatomy must keep {column}.");
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A character saved before the feature without genital markings: once validated it counts as migrated, so an anatomy
    /// edit survives a save and reload. An edit on the raw (version 0) copy survives too.
    /// </summary>
    [Test]
    public async Task EditUnmigratedTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var (db, options) = GetDb(server);
        var user = new NetUserId(Guid.NewGuid());

        await db.InitPrefsAsync(user, MakeProfile().WithGenitals(GenitalProfile.Unmigrated));
        var column = await ReadColumn(options, user);
        var loaded = await Load(db, user);
        Assert.Multiple(() =>
        {
            Assert.That(column, Is.Empty);
            Assert.That(loaded.Genitals.Version, Is.EqualTo(0));
            Assert.That(loaded.Appearance.Markings.Any(m => LegacyGenitalMarkings.IsLegacyId(m.MarkingId)), Is.False);
        });

        // Sanitised path: nothing to convert, so the profile counts as migrated.
        HumanoidCharacterProfile sanitised = default!;
        await server.WaitAssertion(() => sanitised = Validated(loaded, pair.Player!));
        Assert.That(sanitised.Genitals.Version, Is.EqualTo(GenitalProfile.CurrentVersion));

        // The creator edits the sanitised copy; the reload keeps the penis.
        var edited = sanitised.WithGenitals(sanitised.Genitals.WithPenis(new PenisProfile(PenisKnotted, 20)));
        await db.SaveCharacterSlotAsync(user, edited, 0);
        var afterEdit = await Load(db, user);
        Assert.Multiple(() =>
        {
            Assert.That(afterEdit.Genitals.Version, Is.EqualTo(GenitalProfile.CurrentVersion));
            Assert.That(afterEdit.Genitals.Penis?.Shape, Is.EqualTo(PenisKnotted));
            Assert.That(afterEdit.Genitals.Penis?.LengthCm, Is.EqualTo(20));
        });

        // An edit on the unvalidated copy keeps version 0 in memory, and is still saved.
        var rawEdited = loaded.WithGenitals(loaded.Genitals.WithVagina(new VaginaProfile(VaginaHuman)));
        await db.SaveCharacterSlotAsync(user, rawEdited, 0);
        var afterRawEdit = await Load(db, user);
        Assert.Multiple(() =>
        {
            Assert.That(rawEdited.Genitals.Version, Is.EqualTo(0), "Precondition: With* keeps version 0.");
            Assert.That(afterRawEdit.Genitals.Version, Is.EqualTo(GenitalProfile.CurrentVersion));
            Assert.That(afterRawEdit.Genitals.Vagina?.Shape, Is.EqualTo(VaginaHuman));
            Assert.That(afterRawEdit.Genitals.Womb, Is.True);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>An in-memory Sqlite database, as in ServerDbSqliteTests, plus its options for direct row access.</summary>
    private static (ServerDbSqlite Db, DbContextOptions<SqliteServerDbContext> Options) GetDb(
        RobustIntegrationTest.ServerIntegrationInstance server)
    {
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        builder.UseSqlite(conn);
        var options = builder.Options;
        return (new ServerDbSqlite(() => options, true, cfg, true, QuietLog(server)), options);
    }

    /// <summary>Drops everything below Fatal: damaged columns log an Error, which would fail a pooled test.</summary>
    private static ISawmill QuietLog(RobustIntegrationTest.ServerIntegrationInstance server)
    {
        var log = server.ResolveDependency<ILogManager>().GetSawmill("db.ops.wf-genitals-test");
        log.Level = LogLevel.Fatal;
        return log;
    }

    private static async Task<HumanoidCharacterProfile> Load(ServerDbSqlite db, NetUserId user)
    {
        var prefs = await db.GetPlayerPreferencesAsync(user);
        Assert.That(prefs, Is.Not.Null);
        return (HumanoidCharacterProfile) prefs!.Characters[0];
    }

    private static async Task<string> ReadColumn(DbContextOptions<SqliteServerDbContext> options, NetUserId user)
    {
        await using var context = new SqliteServerDbContext(options);
        var row = await context.Profile.AsNoTracking().SingleAsync(p => p.Preference.UserId == user.UserId && p.Slot == 0);
        return row.Genitals;
    }

    private static async Task WriteColumn(DbContextOptions<SqliteServerDbContext> options, NetUserId user, string value)
    {
        await using var context = new SqliteServerDbContext(options);
        var row = await context.Profile.SingleAsync(p => p.Preference.UserId == user.UserId && p.Slot == 0);
        row.Genitals = value;
        await context.SaveChangesAsync();
    }

    /// <summary>A profile as stored before the feature: no anatomy yet, genital markings instead.</summary>
    private static HumanoidCharacterProfile LegacyProfile()
    {
        var profile = MakeProfile().WithGenitals(GenitalProfile.Unmigrated);
        var markings = new List<Marking>
        {
            new(LegacyPenis, new List<Color> { Color.White }),
            new(LegacyTesticles, new List<Color> { Color.White }),
            new(LegacyBreasts, new List<Color> { Color.White }),
        };

        return profile.WithCharacterAppearance(profile.Appearance.WithMarkings(markings));
    }

    /// <summary>profile.Validated, which runs the legacy migration. Server thread only.</summary>
    private static HumanoidCharacterProfile Validated(HumanoidCharacterProfile profile, ICommonSession session)
    {
        return (HumanoidCharacterProfile) profile.Validated(session, IoCManager.Instance!);
    }
}
