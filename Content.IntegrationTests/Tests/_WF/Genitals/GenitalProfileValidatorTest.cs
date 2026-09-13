using System.Collections.Generic;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Prototypes;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Genitals.GenitalTestHelpers;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>Eligibility, organ rules and value normalisation in GenitalProfileValidator, plus GenitalProfile's linked edits.</summary>
[TestFixture]
[TestOf(typeof(GenitalProfileValidator))]
public sealed class GenitalProfileValidatorTest
{
    [Test]
    public async Task EligibilityTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var settings = GenitalProfileValidator.GetSettings(proto);
            var full = FullProfile().WithRevealMode(GenitalRevealMode.ClothingRemoval);

            var minorIssues = new List<GenitalIssue>();
            var minor = GenitalProfileValidator.EnsureValid(full, 17, "Human", proto, minorIssues);
            var ipcIssues = new List<GenitalIssue>();
            var ipc = GenitalProfileValidator.EnsureValid(full, 30, "IPC", proto, ipcIssues);
            var adult = GenitalProfileValidator.EnsureValid(full, 18, "Human", proto);

            Assert.Multiple(() =>
            {
                Assert.That(settings.AdultAge, Is.EqualTo(18));
                Assert.That(GenitalProfileValidator.IsEligible(17, "Human", settings, out var underage), Is.False);
                Assert.That(underage, Is.EqualTo(GenitalIssue.Underage));
                Assert.That(GenitalProfileValidator.IsEligible(30, "IPC", settings, out var excluded), Is.False);
                Assert.That(excluded, Is.EqualTo(GenitalIssue.SpeciesExcluded));
                Assert.That(GenitalProfileValidator.IsEligible(18, "Human", settings, out var none), Is.True);
                Assert.That(none, Is.Null);

                // Below the adult age: Empty, reveal mode kept.
                Assert.That(minor.IsEmpty, Is.True);
                Assert.That(minor.RevealMode, Is.EqualTo(GenitalRevealMode.ClothingRemoval));
                Assert.That(minor.Version, Is.EqualTo(GenitalProfile.CurrentVersion));
                Assert.That(minorIssues, Does.Contain(GenitalIssue.Underage));

                // An excluded species keeps its anatomy, which nothing builds, so excluding a species deletes nothing.
                Assert.That(ipc.MemberwiseEquals(full), Is.True);
                Assert.That(ipcIssues, Does.Contain(GenitalIssue.SpeciesExcluded));

                // The save confirmation asks only below the adult age, with the age clamped as saving clamps it.
                Assert.That(GenitalProfileValidator.IsAdultClamped(5, "IPC", proto), Is.False);
                Assert.That(GenitalProfileValidator.IsAdultClamped(30, "IPC", proto), Is.True);

                // Eligible and valid: unchanged.
                Assert.That(adult.MemberwiseEquals(full), Is.True);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OrganRulesTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var settings = GenitalProfileValidator.GetSettings(proto);
            var empty = GenitalProfile.Empty;
            var knotted = new PenisProfile(PenisKnotted);

            var humanSheath = Validate(proto, empty.WithPenis(new PenisProfile(PenisHuman, sheath: SheathType.Sheath)), out var humanSheathIssues);
            var humanSlit = Validate(proto, empty.WithPenis(new PenisProfile(PenisHuman, sheath: SheathType.Slit)), out _);
            var hemiSheath = Validate(proto, empty.WithPenis(new PenisProfile(PenisHemi, sheath: SheathType.Sheath)), out _);
            var hemiSlit = Validate(proto, empty.WithPenis(new PenisProfile(PenisHemi, sheath: SheathType.Slit)), out var hemiSlitIssues);

            var loneTesticles = Validate(proto, empty.WithTesticles(new TesticlesProfile(TesticleType.External)), out var loneTesticlesIssues);
            var loneWomb = Validate(proto, empty.WithWomb(true), out var loneWombIssues);

            var shortPenis = Validate(proto, empty.WithPenis(knotted.With(lengthCm: 2)), out var shortIssues);
            var longPenis = Validate(proto, empty.WithPenis(knotted.With(lengthCm: 200)), out _);
            var bigTesticles = Validate(proto, empty.WithPenis(knotted).WithTesticles(new TesticlesProfile(TesticleType.External, 9)), out var bigIssues);
            var smallTesticles = Validate(proto, empty.WithPenis(knotted).WithTesticles(new TesticlesProfile(TesticleType.External, 0)), out _);
            var internalTesticles = Validate(proto, empty.WithPenis(knotted).WithTesticles(new TesticlesProfile(TesticleType.Internal, 5)), out var internalIssues);
            var bigCup = Validate(proto, empty.WithBreasts(new BreastsProfile(BreastsPair, 25)), out var cupIssues);
            var smallCup = Validate(proto, empty.WithBreasts(new BreastsProfile(BreastsPair, 0)), out _);

            // Deliberately missing id (a local, so static prototype id validation never sees it).
            ProtoId<GenitalShapePrototype> missingShape = "GenitalShapeDoesNotExist";
            var unknown = Validate(proto, empty.WithPenis(new PenisProfile(missingShape)).WithTesticles(new TesticlesProfile(TesticleType.External)), out var unknownIssues);
            var unknownEnsured = GenitalProfileValidator.EnsureValid(
                empty.WithPenis(new PenisProfile(missingShape)).WithBreasts(new BreastsProfile(BreastsPair, 3)), 30, "Human", proto);
            var unknownTwice = GenitalProfileValidator.EnsureValid(unknownEnsured, 30, "Human", proto);
            var wrongPenis = Validate(proto, empty.WithPenis(new PenisProfile(VaginaHuman)), out var wrongIssues);
            var wrongVagina = Validate(proto, empty.WithVagina(new VaginaProfile(BreastsPair)), out _);
            var wrongBreasts = Validate(proto, empty.WithBreasts(new BreastsProfile(PenisHuman)), out _);

            var full = FullProfile();
            var fullValidated = Validate(proto, full, out var fullIssues);

            Assert.Multiple(() =>
            {
                // Sheaths follow the shape's allowedSheaths.
                Assert.That(humanSheath.Penis!.Sheath, Is.EqualTo(SheathType.None));
                Assert.That(humanSheathIssues, Does.Contain(GenitalIssue.SheathNotAllowed));
                Assert.That(humanSlit.Penis!.Sheath, Is.EqualTo(SheathType.None));
                Assert.That(hemiSheath.Penis!.Sheath, Is.EqualTo(SheathType.None));
                Assert.That(hemiSlit.Penis!.Sheath, Is.EqualTo(SheathType.Slit));
                Assert.That(hemiSlitIssues, Is.Empty);

                // Linked options.
                Assert.That(loneTesticles.Testicles, Is.Null);
                Assert.That(loneTesticlesIssues, Does.Contain(GenitalIssue.TesticlesWithoutPenis));
                Assert.That(loneWomb.Womb, Is.False);
                Assert.That(loneWombIssues, Does.Contain(GenitalIssue.WombWithoutVagina));

                // Clamps.
                Assert.That(shortPenis.Penis!.LengthCm, Is.EqualTo(settings.MinLengthCm));
                Assert.That(shortIssues, Does.Contain(GenitalIssue.LengthClamped));
                Assert.That(longPenis.Penis!.LengthCm, Is.EqualTo(settings.MaxLengthCm));
                Assert.That(bigTesticles.Testicles!.Size, Is.EqualTo(GenitalProfileValidator.MaxTesticleSize));
                Assert.That(bigIssues, Does.Contain(GenitalIssue.SizeClamped));
                Assert.That(smallTesticles.Testicles!.Size, Is.EqualTo(GenitalProfileValidator.MinTesticleSize));
                Assert.That(bigCup.Breasts!.Cup, Is.EqualTo(GenitalProfileValidator.MaxCup));
                Assert.That(cupIssues, Does.Contain(GenitalIssue.CupClamped));
                Assert.That(smallCup.Breasts!.Cup, Is.EqualTo(GenitalProfileValidator.MinCup));

                // Internal testicles are not drawn or described: the size resets to the default.
                Assert.That(internalTesticles.Testicles!.Type, Is.EqualTo(TesticleType.Internal));
                Assert.That(internalTesticles.Testicles!.Size, Is.EqualTo(settings.DefaultTesticleSize));
                Assert.That(internalIssues, Is.Empty);

                // Unknown and wrong-slot shapes drop the organ (and its linked options).
                Assert.That(unknown.Penis, Is.Null);
                Assert.That(unknown.Testicles, Is.Null);
                Assert.That(unknownIssues, Does.Contain(GenitalIssue.UnknownShape));

                // On the server, a dropped unknown shape keeps the stored column until the player edits anatomy.
                Assert.That(unknown.LoadFailed, Is.False, "ValidateOrgans alone never flags LoadFailed.");
                Assert.That(unknownEnsured.LoadFailed, Is.True, "EnsureValid flags a dropped unknown shape LoadFailed.");
                Assert.That(unknownEnsured.Penis, Is.Null);
                Assert.That(unknownEnsured.Breasts, Is.Not.Null, "The other organs stay.");
                Assert.That(unknownTwice.MemberwiseEquals(unknownEnsured), Is.True, "EnsureValid stays idempotent.");
                Assert.That(wrongPenis.Penis, Is.Null);
                Assert.That(wrongIssues, Does.Contain(GenitalIssue.WrongSlotShape));
                Assert.That(wrongVagina.Vagina, Is.Null);
                Assert.That(wrongBreasts.Breasts, Is.Null);

                // A valid profile comes back as the same instance.
                Assert.That(fullValidated, Is.SameAs(full));
                Assert.That(fullIssues, Is.Empty);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NormalisationTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var empty = GenitalProfile.Empty;
            var knotted = new PenisProfile(PenisKnotted);

            // Undefined enum values fall back to their defaults.
            var badEnums = Validate(proto,
                empty.WithRevealMode((GenitalRevealMode) 9)
                    .WithPenis(new PenisProfile(PenisKnotted, sheath: (SheathType) 9, visibility: (GenitalVisibility) 9))
                    .WithVagina(new VaginaProfile(VaginaHuman, visibility: (GenitalVisibility) 9))
                    .WithBreasts(new BreastsProfile(BreastsPair, visibility: (GenitalVisibility) 9)),
                out var badEnumIssues);

            // A testicle type of None or an undefined value drops the testicles.
            var noneTesticles = Validate(proto, empty.WithPenis(knotted).WithTesticles(new TesticlesProfile(TesticleType.None)), out _);
            var badTesticles = Validate(proto, empty.WithPenis(knotted).WithTesticles(new TesticlesProfile((TesticleType) 9)), out var badTesticleIssues);

            // Colours: NaN, infinite and out-of-range channels become white.
            var nan = Validate(proto, empty.WithPenis(knotted.With(color: new Color(float.NaN, 0f, 0f))), out var nanIssues).Penis!.Color;
            var infinite = Validate(proto, empty.WithPenis(knotted.With(sheathColor: new Color(0f, float.PositiveInfinity, 0f))), out _).Penis!.SheathColor;
            var tooBright = Validate(proto, empty.WithVagina(new VaginaProfile(VaginaHuman, color: new Color(1.5f, 0f, 0f))), out _).Vagina!.Color;
            var negative = Validate(proto, empty.WithBreasts(new BreastsProfile(BreastsPair, color: new Color(0f, 0f, -0.1f))), out _).Breasts!.Color;

            // Alpha is forced opaque and channels are quantised to 8 bits, so a second pass changes nothing.
            var oddColor = new Color(0.123f, 0.456f, 0.789f, 0.5f);
            var quantised = Validate(proto, empty.WithPenis(knotted.With(matchSkin: false, color: oddColor)), out var quantisedIssues);
            var requantised = GenitalProfileValidator.ValidateOrgans(quantised, proto);
            var color = quantised.Penis!.Color;

            // A LoadFailed profile passes the rules but keeps its flag and version (it is never migrated).
            var failed = GenitalProfile.Failed(GenitalProfile.Unmigrated.WithPenis(knotted));
            var failedValidated = Validate(proto, failed, out var failedIssues);
            var failedEnsured = GenitalProfileValidator.EnsureValid(failed, 30, "Human", proto);

            Assert.Multiple(() =>
            {
                Assert.That(badEnums.RevealMode, Is.EqualTo(GenitalRevealMode.UndergarmentRemoval));
                Assert.That(badEnums.Penis!.Sheath, Is.EqualTo(SheathType.None));
                Assert.That(badEnums.Penis!.Visibility, Is.EqualTo(GenitalVisibility.Normal));
                Assert.That(badEnums.Vagina!.Visibility, Is.EqualTo(GenitalVisibility.Normal));
                Assert.That(badEnums.Breasts!.Visibility, Is.EqualTo(GenitalVisibility.Normal));
                Assert.That(badEnumIssues, Does.Contain(GenitalIssue.InvalidValue));

                Assert.That(noneTesticles.Testicles, Is.Null);
                Assert.That(badTesticles.Testicles, Is.Null);
                Assert.That(badTesticleIssues, Does.Contain(GenitalIssue.InvalidValue));

                Assert.That(nan, Is.EqualTo(Color.White));
                Assert.That(nanIssues, Does.Contain(GenitalIssue.InvalidValue));
                Assert.That(infinite, Is.EqualTo(Color.White));
                Assert.That(tooBright, Is.EqualTo(Color.White));
                Assert.That(negative, Is.EqualTo(Color.White));

                Assert.That(color.A, Is.EqualTo(1f));
                Assert.That(color.R * 255f, Is.EqualTo(MathF.Round(color.R * 255f)).Within(0.001f));
                Assert.That(color.G * 255f, Is.EqualTo(MathF.Round(color.G * 255f)).Within(0.001f));
                Assert.That(color.B * 255f, Is.EqualTo(MathF.Round(color.B * 255f)).Within(0.001f));
                Assert.That(color.R, Is.EqualTo(oddColor.R).Within(1f / 255f));
                Assert.That(quantisedIssues, Is.Empty);
                Assert.That(requantised, Is.SameAs(quantised));

                Assert.That(failedValidated.LoadFailed, Is.True);
                Assert.That(failedValidated.Version, Is.EqualTo(0));
                Assert.That(failedValidated.Penis, Is.Not.Null);
                Assert.That(failedIssues, Does.Contain(GenitalIssue.LoadFailed));
                Assert.That(failedEnsured.LoadFailed, Is.True);
                Assert.That(failedEnsured.Version, Is.EqualTo(0));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public void ProfileEditsTest()
    {
        var full = FullProfile();
        var noPenis = full.WithPenis(null);
        var vaginaAdded = GenitalProfile.Empty.WithVagina(new VaginaProfile(VaginaHuman));
        var wombRemoved = vaginaAdded.WithWomb(false);
        var vaginaChanged = wombRemoved.WithVagina(new VaginaProfile(VaginaSlit));
        var failed = GenitalProfile.Failed(full);
        var clone = full.Clone();
        var first = GenitalProfile.Empty;
        var second = GenitalProfile.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(GenitalProfile.Empty.Version, Is.EqualTo(GenitalProfile.CurrentVersion));
            Assert.That(GenitalProfile.Unmigrated.Version, Is.EqualTo(0));
            Assert.That(first, Is.Not.SameAs(second), "Empty must return a fresh instance.");
            Assert.That(GenitalProfile.Empty.IsEmpty, Is.True);

            // Removing the penis removes its linked testicles.
            Assert.That(noPenis.Penis, Is.Null);
            Assert.That(noPenis.Testicles, Is.Null);

            // Adding a vagina adds the womb; changing it keeps the womb choice; removing it removes the womb.
            Assert.That(vaginaAdded.Womb, Is.True);
            Assert.That(vaginaChanged.Womb, Is.False);
            Assert.That(vaginaAdded.WithVagina(null).Womb, Is.False);

            // Every edit clears LoadFailed.
            Assert.That(failed.LoadFailed, Is.True);
            Assert.That(failed.WithWomb(true).LoadFailed, Is.False);

            // Clone is deep and equal.
            Assert.That(clone, Is.Not.SameAs(full));
            Assert.That(clone.Penis, Is.Not.SameAs(full.Penis));
            Assert.That(clone.MemberwiseEquals(full), Is.True);
            Assert.That(full.MemberwiseEquals(noPenis), Is.False);
        });
    }

    /// <summary>Every issue the creator shows as a warning has its line; the age, species and load gates have their own.</summary>
    [Test]
    public async Task WarningKeysTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var loc = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var issue in Enum.GetValues<GenitalIssue>())
                {
                    if (issue is GenitalIssue.Underage or GenitalIssue.SpeciesExcluded or GenitalIssue.LoadFailed)
                        continue;

                    Assert.That(loc.HasString($"wf-genitals-warning-{issue}"), Is.True, $"No creator warning for {issue}.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    private static GenitalProfile Validate(IPrototypeManager proto, GenitalProfile profile, out List<GenitalIssue> issues)
    {
        issues = new List<GenitalIssue>();
        return GenitalProfileValidator.ValidateOrgans(profile, proto, issues);
    }
}
