using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Migration;
using Content.Shared._WF.Genitals.Prototypes;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Genitals.GenitalTestHelpers;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>The legacy marking table against its golden fixture, and the migration rules.</summary>
/// <remarks>
/// Golden only: the genital marking prototypes have been deleted, so the frozen table is checked against the fixture alone
/// (NoGenitalMarkingsTest keeps them deleted).
/// </remarks>
[TestFixture]
[TestOf(typeof(LegacyGenitalMarkings))]
public sealed class LegacyGenitalMarkingsTest
{
    /// <summary>Genital-category markings in genitals.yml when the table was generated, including the two off-scheme states.</summary>
    private const int LegacyCount = 218;

    private static readonly Color Skin = Color.FromHex("#B07A5E");

    [Test]
    public async Task GoldenTableTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var golden = LegacyGenitalGolden.Entries.ToDictionary(e => e.Id);
            var table = LegacyGenitalMarkings.Entries;

            Assert.Multiple(() =>
            {
                Assert.That(golden, Has.Count.EqualTo(LegacyCount));
                Assert.That(table, Has.Count.EqualTo(LegacyCount));
                Assert.That(table.ContainsKey("Genital-Penis-Amputated"), "amputated special entry");
                Assert.That(table.ContainsKey("Knotted-Wolf-Cock"), "BRC special entry");

                // The table equals the golden fixture.
                foreach (var expected in LegacyGenitalGolden.Entries)
                {
                    if (!table.TryGetValue(expected.Id, out var entry))
                    {
                        Assert.Fail($"{expected.Id} is in the golden fixture but not in the table");
                        continue;
                    }

                    Assert.That(entry.Slot, Is.EqualTo(expected.Slot), expected.Id);
                    Assert.That(entry.Shape, Is.EqualTo(expected.Shape), expected.Id);
                    Assert.That(entry.Step, Is.EqualTo(expected.Step), expected.Id);
                    Assert.That(entry.BakedColor?.ToHexNoAlpha(), Is.EqualTo(expected.BakedColor), expected.Id);
                    Assert.That(entry.Sheathed, Is.EqualTo(expected.Sheathed), expected.Id);
                    Assert.That(entry.RemovesPenis, Is.EqualTo(expected.RemovesPenis), expected.Id);
                }

                // Every shape the table names exists and belongs to the entry's slot.
                foreach (var (id, entry) in table)
                {
                    if (entry.RemovesPenis)
                        continue;

                    Assert.That(proto.TryIndex(new ProtoId<GenitalShapePrototype>(entry.Shape), out var shape), $"{id}: unknown shape {entry.Shape}");
                    Assert.That(shape?.Slot, Is.EqualTo(entry.Slot), $"{id}: shape slot");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StepLengthTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var settings = GenitalProfileValidator.GetSettings(proto);
            Assert.Multiple(() =>
            {
                for (var step = 1; step <= 5; step++)
                {
                    var length = GenitalStateBuilder.StepToLength(step);
                    Assert.That((int) GenitalStateBuilder.LengthToStep(length, settings), Is.EqualTo(step), $"step {step} ({length} cm)");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DecodeSamplesTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var vulpTissue = GenitalColorDefaults.TissueColorFor("Vulpkanin", proto);
            var baked = LegacyGenitalMarkings.Entries["GenitalVaginaHumanSmall"].BakedColor;

            var penisAndSheath = Run(proto, "Human", M("Genital-Penis-Knotted-3-0"), M("Genital-Balls-Sheath2")).Genitals;
            var sheathOnly = Run(proto, "Vulpkanin", M("Genital-Balls-Sheath3")).Genitals;
            var reptileSheathOnly = Run(proto, "Reptilian", M("Genital-Balls-Sheath1")).Genitals;
            var testiclesOnly = Run(proto, "Human", M("Genital-Balls-Single2")).Genitals;
            var humanSheath = Run(proto, "Human", M("Genital-Penis-Human-2-0"), M("Genital-Balls-Sheath2")).Genitals;
            var skinToneVagina = Run(proto, "Human", M("GenitalVaginaHumanSmall")).Genitals;
            var greyVulp = Run(proto, "Vulpkanin", M("Genital-Penis-Knotted-2-0")).Genitals;
            var greyHuman = Run(proto, "Human", M("Genital-Penis-Knotted-2-0")).Genitals;
            var skinMatched = Run(proto, "Human", M("Genital-Penis-Knotted-2-0", Skin.ToHex())).Genitals;
            var custom = Run(proto, "Human", M("Genital-Penis-Knotted-2-0", "#3366CC")).Genitals;
            var amputated = Run(proto, "Human", M("Genital-Penis-Amputated")).Genitals;
            var amputatedTesticles = Run(proto, "Human", M("Genital-Penis-Amputated"), M("Genital-Balls-Sheath2")).Genitals;
            var udders = Run(proto, "Human", M("GenitalBreastsUddersC")).Genitals;

            var all = new[]
            {
                penisAndSheath, sheathOnly, reptileSheathOnly, testiclesOnly, humanSheath, skinToneVagina, greyVulp, greyHuman,
                skinMatched, custom, amputated, amputatedTesticles, udders,
            };

            Assert.Multiple(() =>
            {
                // Penis plus fused sheath art: a sheathed penis with separate testicles.
                Assert.That(penisAndSheath.Version, Is.EqualTo(GenitalProfile.CurrentVersion));
                Assert.That(penisAndSheath.Penis?.Shape, Is.EqualTo(PenisKnotted));
                Assert.That(penisAndSheath.Penis?.LengthCm, Is.EqualTo(28));
                Assert.That(penisAndSheath.Penis?.Sheath, Is.EqualTo(SheathType.Sheath));
                Assert.That(penisAndSheath.Penis?.SheathMatchSkin, Is.True);
                Assert.That(penisAndSheath.Testicles?.Type, Is.EqualTo(TesticleType.External));
                Assert.That(penisAndSheath.Testicles?.Size, Is.EqualTo(2));

                // Fused sheath art without a penis: a sheathed penis in the species preset's shape.
                Assert.That(sheathOnly.Penis?.Shape, Is.EqualTo(PenisKnotted));
                Assert.That(sheathOnly.Penis?.LengthCm, Is.EqualTo(15));
                Assert.That(sheathOnly.Penis?.Sheath, Is.EqualTo(SheathType.Sheath));
                Assert.That(sheathOnly.Penis?.Visibility, Is.EqualTo(GenitalVisibility.Normal));
                Assert.That(sheathOnly.Testicles?.Size, Is.EqualTo(3));

                // The reptile preset's hemipenes cannot have a sheath, so the fallback shape is used.
                Assert.That(reptileSheathOnly.Penis?.Shape, Is.EqualTo(LegacyGenitalMarkings.FallbackSheathShape));
                Assert.That(reptileSheathOnly.Penis?.Sheath, Is.EqualTo(SheathType.Sheath));

                // Testicles only: a penis that is never drawn keeps them, and the old look.
                Assert.That(testiclesOnly.Penis, Is.Not.Null);
                Assert.That(testiclesOnly.Penis?.Visibility, Is.EqualTo(GenitalVisibility.AlwaysHidden));
                Assert.That(testiclesOnly.Penis?.Sheath, Is.EqualTo(SheathType.None));
                Assert.That(testiclesOnly.Testicles?.Size, Is.EqualTo(2));

                // A human-shaped penis cannot have a sheath: shape kept, sheath dropped.
                Assert.That(humanSheath.Penis?.Shape, Is.EqualTo(PenisHuman));
                Assert.That(humanSheath.Penis?.Sheath, Is.EqualTo(SheathType.None));
                Assert.That(humanSheath.Testicles?.Size, Is.EqualTo(2));

                // Skin-tone art keeps its baked colour; the vulva brings the womb.
                Assert.That(baked, Is.Not.Null);
                Assert.That(skinToneVagina.Vagina?.Shape, Is.EqualTo(VaginaHuman));
                Assert.That(skinToneVagina.Vagina?.MatchSkin, Is.False);
                Assert.That(skinToneVagina.Vagina?.Color, Is.EqualTo(baked));
                Assert.That(skinToneVagina.Womb, Is.True);

                // Untinted art: tissue colour on non-human-toned species, match skin on human-toned ones.
                Assert.That(vulpTissue, Is.Not.Null);
                Assert.That(greyVulp.Penis?.MatchSkin, Is.False);
                Assert.That(greyVulp.Penis?.Color, Is.EqualTo(vulpTissue));
                Assert.That(greyHuman.Penis?.MatchSkin, Is.True);

                // A colour equal to the skin matches skin; anything else is kept as a custom colour.
                Assert.That(skinMatched.Penis?.MatchSkin, Is.True);
                Assert.That(custom.Penis?.MatchSkin, Is.False);
                Assert.That(custom.Penis?.Color, Is.EqualTo(Color.FromHex("#3366CC")));

                // Amputation removes the penis; testicles still get a hidden one.
                Assert.That(amputated.IsEmpty, Is.True);
                Assert.That(amputated.Version, Is.EqualTo(GenitalProfile.CurrentVersion));
                Assert.That(amputatedTesticles.Penis?.Visibility, Is.EqualTo(GenitalVisibility.AlwaysHidden));
                Assert.That(amputatedTesticles.Penis?.Sheath, Is.EqualTo(SheathType.None));
                Assert.That(amputatedTesticles.Testicles?.Size, Is.EqualTo(2));

                // Udders C.
                Assert.That(udders.Breasts?.Shape, Is.EqualTo(new ProtoId<GenitalShapePrototype>("GenitalShapeBreastsUdders")));
                Assert.That(udders.Breasts?.Cup, Is.EqualTo(3));

                // Every result already satisfies the organ rules.
                foreach (var result in all)
                {
                    Assert.That(GenitalProfileValidator.ValidateOrgans(result, proto).MemberwiseEquals(result), Is.True);
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OrderIndependenceTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var sheathFirst = Run(proto, "Human", M("Genital-Balls-Sheath2"), M("Genital-Penis-Knotted-3-0"));
            var penisFirst = Run(proto, "Human", M("Genital-Penis-Knotted-3-0"), M("Genital-Balls-Sheath2"));
            var amputatedLast = Run(proto, "Human", M("Genital-Penis-Knotted-3-0"), M("Genital-Penis-Amputated"));
            var amputatedFirst = Run(proto, "Human", M("Genital-Penis-Amputated"), M("Genital-Penis-Knotted-3-0"));
            var pairFirst = Run(proto, "Human", M("GenitalBreastsPairA"), M("GenitalBreastsRoundD"));
            var roundFirst = Run(proto, "Human", M("GenitalBreastsRoundD"), M("GenitalBreastsPairA"));

            Assert.Multiple(() =>
            {
                Assert.That(sheathFirst.Genitals.Penis?.Sheath, Is.EqualTo(SheathType.Sheath));
                Assert.That(sheathFirst.Genitals.MemberwiseEquals(penisFirst.Genitals), Is.True);

                Assert.That(amputatedLast.Genitals.Penis, Is.Null);
                Assert.That(amputatedFirst.Genitals.MemberwiseEquals(amputatedLast.Genitals), Is.True);

                // Two breast markings: the first one listed wins, and both are removed.
                Assert.That(pairFirst.Genitals.Breasts?.Shape, Is.EqualTo(BreastsPair));
                Assert.That(pairFirst.Genitals.Breasts?.Cup, Is.EqualTo(1));
                Assert.That(roundFirst.Genitals.Breasts?.Shape, Is.EqualTo(new ProtoId<GenitalShapePrototype>("GenitalShapeBreastsPairRound")));
                Assert.That(roundFirst.Genitals.Breasts?.Cup, Is.EqualTo(4));
                Assert.That(pairFirst.Appearance.Markings, Is.Empty);
                Assert.That(pairFirst.Genitals.LegacyMarkings, Has.Count.EqualTo(2));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ProfileStateTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var ctx = LegacyGenitalMarkings.ContextFor("Human", Skin, proto);

            var withPregnancy = Run(proto, "Human", M("Pregnant-2"), M("Genital-Penis-Knotted-1-0"));

            var configured = FullProfile();
            var keepConfig = Run(proto, "Human", configured, M("Genital-Penis-Human-1-0"), M("Pregnant-1"));
            var backedUp = FullProfile().AsMigrated(new List<string> { "GenitalBreastsPairC@#FFFFFFFF" });
            var mergedBackup = Run(proto, "Human", backedUp, M("Genital-Penis-Human-1-0"), M("GenitalBreastsPairC"));

            var emptyV1 = Run(proto, "Human", GenitalProfile.Empty, M("Genital-Penis-Knotted-1-0"));

            var failed = GenitalProfile.Failed(GenitalProfile.Unmigrated);
            var failedInput = Appearance(M("Genital-Penis-Knotted-1-0"));
            var failedResult = LegacyGenitalMarkings.Migrate(failedInput, failed, ctx);

            var input = Appearance(M("Genital-Penis-Knotted-3-0", "#3366CC"), M("Pregnant-3"), M("Genital-Balls-Sheath2"));
            var inputStrings = input.Markings.Select(m => m.ToString()).ToList();
            var unmigrated = GenitalProfile.Unmigrated;
            var converted = LegacyGenitalMarkings.Migrate(input, unmigrated, ctx);

            var noLegacy = Appearance(M("Pregnant-1"));
            var untouched = LegacyGenitalMarkings.Migrate(noLegacy, unmigrated, ctx);
            var alreadyMigrated = LegacyGenitalMarkings.Migrate(noLegacy, configured, ctx);

            // Logs one warning, which does not fail the test.
            var unknown = Run(proto, "Human", M("Genital-NotInTheTable"));

            Assert.Multiple(() =>
            {
                // Pregnancy overlays are not genital markings.
                Assert.That(Ids(withPregnancy.Appearance), Is.EqualTo(new[] { "Pregnant-2" }));

                // A real version-1 configuration wins; the old ids go into its backup, after any recorded before.
                Assert.That(keepConfig.Genitals.MemberwiseEquals(configured), Is.True);
                Assert.That(keepConfig.Genitals.LegacyMarkings, Is.EqualTo(new[] { "Genital-Penis-Human-1-0@#FFFFFFFF" }));
                Assert.That(Ids(keepConfig.Appearance), Is.EqualTo(new[] { "Pregnant-1" }));
                Assert.That(mergedBackup.Genitals.LegacyMarkings,
                    Is.EqualTo(new[] { "GenitalBreastsPairC@#FFFFFFFF", "Genital-Penis-Human-1-0@#FFFFFFFF" }));

                // An empty version-1 profile with old ids converts.
                Assert.That(emptyV1.Genitals.Penis?.Shape, Is.EqualTo(PenisKnotted));
                Assert.That(emptyV1.Genitals.Version, Is.EqualTo(GenitalProfile.CurrentVersion));

                // A damaged column is never converted, and its markings stay.
                Assert.That(failedResult.Genitals, Is.SameAs(failed));
                Assert.That(failedResult.Appearance, Is.SameAs(failedInput));

                // The inputs are never modified.
                Assert.That(input.Markings.Select(m => m.ToString()), Is.EqualTo(inputStrings));
                Assert.That(unmigrated.Version, Is.EqualTo(0));
                Assert.That(unmigrated.IsEmpty, Is.True);
                Assert.That(converted.Appearance, Is.Not.SameAs(input));
                Assert.That(Ids(converted.Appearance), Is.EqualTo(new[] { "Pregnant-3" }));

                // The removed strings are recorded for a rollback.
                Assert.That(converted.Genitals.LegacyMarkings,
                    Is.EqualTo(new[] { "Genital-Penis-Knotted-3-0@#3366CCFF", "Genital-Balls-Sheath2@#FFFFFFFF" }));

                // Nothing to convert: the appearance comes back as is, and an unmigrated profile now counts as migrated,
                // so a later anatomy edit on it is saved instead of serialising as "".
                Assert.That(untouched.Appearance, Is.SameAs(noLegacy));
                Assert.That(untouched.Genitals.Version, Is.EqualTo(GenitalProfile.CurrentVersion));
                Assert.That(untouched.Genitals.IsEmpty, Is.True);
                Assert.That(untouched.Genitals.LegacyMarkings, Is.Null);
                Assert.That(alreadyMigrated.Appearance, Is.SameAs(noLegacy));
                Assert.That(alreadyMigrated.Genitals, Is.SameAs(configured));

                // Genital-looking ids that the table does not know are left to marking validation; nothing is converted.
                Assert.That(Ids(unknown.Appearance), Is.EqualTo(new[] { "Genital-NotInTheTable" }));
                Assert.That(unknown.Genitals.Version, Is.EqualTo(GenitalProfile.CurrentVersion));
                Assert.That(unknown.Genitals.IsEmpty, Is.True);
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The validator keeps only well-formed legacy strings, so a crafted client cannot store arbitrary text in the column.</summary>
    [Test]
    public async Task LegacyMarkingsSanitisedTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            const string valid = "Genital-Penis-Knotted-3-0@#FFFFFFFF";
            const string validTwoColors = "Genital-Balls-Sheath2@#3366CCFF,#FFFFFFFF";
            var clean = GenitalProfile.Empty.AsMigrated(new List<string> { valid, validTwoColors });
            var cleanResult = GenitalProfileValidator.ValidateOrgans(clean, proto);

            var crafted = GenitalProfile.Empty.AsMigrated(new List<string>
            {
                valid,
                null,
                "Pregnant-1@#FFFFFFFF",
                "arbitrary text",
                "Genital-Penis-Knotted-3-0@<b>not colours</b>",
                "Genital-Penis-Knotted-3-0@#" + new string('F', GenitalProfileValidator.MaxLegacyMarkingLength),
                "@#FFFFFFFF",
            });
            var issues = new List<GenitalIssue>();
            var craftedResult = GenitalProfileValidator.ValidateOrgans(crafted, proto, issues);
            var ensured = GenitalProfileValidator.EnsureValid(crafted, 30, "Human", proto);

            var flood = Enumerable.Repeat(valid, LegacyGenitalMarkings.Entries.Count + 50).ToList();
            var floodResult = GenitalProfileValidator.ValidateOrgans(GenitalProfile.Empty.AsMigrated(flood), proto);
            var junkOnly = GenitalProfileValidator.ValidateOrgans(GenitalProfile.Empty.AsMigrated(new List<string> { "junk" }), proto);
            var emptyList = GenitalProfileValidator.ValidateOrgans(GenitalProfile.Empty.AsMigrated(new List<string>()), proto);

            Assert.Multiple(() =>
            {
                Assert.That(cleanResult, Is.SameAs(clean), "Clean legacy strings are kept as they are.");
                Assert.That(craftedResult.LegacyMarkings, Is.EqualTo(new[] { valid }));
                Assert.That(issues, Does.Contain(GenitalIssue.InvalidValue));
                Assert.That(crafted.LegacyMarkings, Has.Count.EqualTo(7), "The input list was modified.");
                Assert.That(ensured.LegacyMarkings, Is.EqualTo(new[] { valid }), "EnsureValid applies the same filter.");
                Assert.That(floodResult.LegacyMarkings, Has.Count.EqualTo(LegacyGenitalMarkings.Entries.Count));
                Assert.That(junkOnly.LegacyMarkings, Is.Null);
                Assert.That(emptyList.LegacyMarkings, Is.Null);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AdultOnlyMarkingsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var appearance = Appearance(M("Pregnant-1"), M("Genital-Penis-Knotted-1-0"));
            var minor = GenitalAdultMarkings.StripIfMinor(appearance, 17, proto);
            var adult = GenitalAdultMarkings.StripIfMinor(appearance, 18, proto);

            Assert.Multiple(() =>
            {
                Assert.That(Ids(minor), Is.EqualTo(new[] { "Genital-Penis-Knotted-1-0" }));
                Assert.That(adult, Is.SameAs(appearance));
                Assert.That(appearance.Markings, Has.Count.EqualTo(2));
                var adultOnly = GenitalProfileValidator.GetSettings(proto).AdultOnlyMarkings;
                Assert.That(adultOnly, Does.Contain("Pregnant-4"));
                Assert.That(adultOnly, Does.Not.Contain("Genital-Penis-Knotted-1-0"));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A saved profile whose old genital markings no longer have prototypes still converts through the frozen table when
    /// HumanoidCharacterProfile.EnsureValid runs, and the ids leave the appearance.
    /// </summary>
    [Test]
    public async Task DeletedPrototypesConvertTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        string[] legacy = { "Genital-Penis-Knotted-3-1", "Genital-Balls-Sheath2Alt", "GenitalBreastsRoundC" };

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var id in legacy)
                {
                    Assert.That(proto.HasIndex<MarkingPrototype>(id), Is.False, $"Precondition: {id} has no marking prototype.");
                    Assert.That(LegacyGenitalMarkings.IsLegacyId(id), Is.True, $"Precondition: the frozen table knows {id}.");
                }
            });

            var saved = MakeProfile()
                .WithCharacterAppearance(Appearance(M(legacy[0]), M(legacy[1]), M(legacy[2]), M("Pregnant-1")))
                .WithGenitals(GenitalProfile.Unmigrated);
            var validated = (HumanoidCharacterProfile) saved.Validated(pair.Player!, IoCManager.Instance!);
            var genitals = validated.Genitals;

            Assert.Multiple(() =>
            {
                Assert.That(genitals.Version, Is.EqualTo(GenitalProfile.CurrentVersion));
                Assert.That(genitals.Penis?.Shape, Is.EqualTo(PenisKnotted));
                Assert.That(genitals.Penis?.LengthCm, Is.EqualTo(GenitalStateBuilder.StepToLength(3)));
                Assert.That(genitals.Penis?.Sheath, Is.EqualTo(SheathType.Sheath), "The fused sheath art becomes the penis's sheath.");
                Assert.That(genitals.Testicles?.Type, Is.EqualTo(TesticleType.External));
                Assert.That(genitals.Testicles?.Size, Is.EqualTo(2));
                Assert.That(genitals.Breasts?.Shape.Id, Is.EqualTo("GenitalShapeBreastsPairRound"));
                Assert.That(genitals.Breasts?.Cup, Is.EqualTo(3));
                Assert.That(genitals.LegacyMarkings, Is.EqualTo(legacy.Select(id => id + "@#FFFFFFFF")));
                Assert.That(Ids(validated.Appearance), Is.EqualTo(new[] { "Pregnant-1" }), "Only the pregnancy overlay stays.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The legacy backup survives the adult gate: an excluded species keeps its converted anatomy and backup, and below the
    /// adult age only the backup and the reveal mode stay.
    /// </summary>
    [Test]
    public async Task IneligibleKeepsBackupTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var ipc = (HumanoidCharacterProfile) MakeProfile("IPC")
                .WithCharacterAppearance(Appearance(M("Genital-Penis-Knotted-3-0"), M("GenitalBreastsRoundC")))
                .WithGenitals(GenitalProfile.Unmigrated)
                .Validated(pair.Player!, IoCManager.Instance!);

            var converted = Run(proto, "Human", M("Genital-Penis-Knotted-3-0")).Genitals;
            var minor = GenitalProfileValidator.EnsureValid(converted, 17, "Human", proto);

            Assert.That(ipc.Species.Id, Is.EqualTo("IPC"), "Precondition: IPC is a roundstart species.");
            Assert.Multiple(() =>
            {
                Assert.That(ipc.Appearance.Markings.Any(m => LegacyGenitalMarkings.IsLegacyId(m.MarkingId)), Is.False,
                    "The old ids leave the appearance.");
                Assert.That(ipc.Genitals.LegacyMarkings,
                    Is.EqualTo(new[] { "Genital-Penis-Knotted-3-0@#FFFFFFFF", "GenitalBreastsRoundC@#FFFFFFFF" }),
                    "An excluded species keeps the backup.");
                Assert.That(ipc.Genitals.Penis?.Shape, Is.EqualTo(PenisKnotted),
                    "An excluded species keeps its stored anatomy, which nothing builds.");
                Assert.That(minor.IsEmpty, Is.True, "Below the adult age the organs are cleared.");
                Assert.That(minor.LegacyMarkings, Is.EqualTo(converted.LegacyMarkings), "Below the adult age the backup stays.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A marking with one colour.</summary>
    private static Marking M(string id, string color = "#FFFFFF")
    {
        return new Marking(id, new List<Color> { Color.FromHex(color) });
    }

    private static HumanoidCharacterAppearance Appearance(params Marking[] markings)
    {
        return new HumanoidCharacterAppearance(HairStyles.DefaultHairStyle, Color.Black, HairStyles.DefaultFacialHairStyle,
            Color.Black, Color.Black, Skin, markings.ToList());
    }

    private static string[] Ids(HumanoidCharacterAppearance appearance)
    {
        return appearance.Markings.Select(m => m.MarkingId).ToArray();
    }

    /// <summary>Migrates an unmigrated profile with these markings.</summary>
    private static (HumanoidCharacterAppearance Appearance, GenitalProfile Genitals) Run(IPrototypeManager proto, string species,
        params Marking[] markings)
    {
        return Run(proto, species, GenitalProfile.Unmigrated, markings);
    }

    private static (HumanoidCharacterAppearance Appearance, GenitalProfile Genitals) Run(IPrototypeManager proto, string species,
        GenitalProfile genitals, params Marking[] markings)
    {
        return LegacyGenitalMarkings.Migrate(Appearance(markings), genitals, LegacyGenitalMarkings.ContextFor(species, Skin, proto));
    }
}
