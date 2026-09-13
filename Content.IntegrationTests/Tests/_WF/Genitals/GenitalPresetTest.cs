using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Prototypes;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Genitals.GenitalTestHelpers;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>Presets are valid, their shapes exist, and every roundstart species has exactly one preset.</summary>
[TestFixture]
[TestOf(typeof(GenitalPresetPrototype))]
public sealed class GenitalPresetTest
{
    [Test]
    public async Task PresetsValidateTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var presets = proto.EnumeratePrototypes<GenitalPresetPrototype>().Where(p => !pair.IsTestPrototype(p)).ToList();
            Assert.That(presets, Is.Not.Empty);

            Assert.Multiple(() =>
            {
                foreach (var preset in presets)
                {
                    CheckEntry(proto, preset, preset.Male, "male");
                    CheckEntry(proto, preset, preset.Female, "female");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpeciesCoverageTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var settings = GenitalProfileValidator.GetSettings(proto);
            var presets = proto.EnumeratePrototypes<GenitalPresetPrototype>().Where(p => !pair.IsTestPrototype(p)).ToList();

            var listings = new Dictionary<string, List<string>>();
            foreach (var preset in presets)
            {
                foreach (var species in preset.Species)
                {
                    AddListing(listings, species.Id, preset.ID);
                }

                foreach (var species in preset.ExplicitFallback)
                {
                    AddListing(listings, species.Id, preset.ID + " (explicitFallback)");
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(presets.Count(p => p.Fallback), Is.EqualTo(1), "Exactly one preset must have fallback: true.");

                foreach (var preset in presets.Where(p => !p.Fallback))
                {
                    Assert.That(preset.ExplicitFallback, Is.Empty, $"{preset.ID} lists explicitFallback species but is not the fallback preset.");
                }

                foreach (var id in listings.Keys)
                {
                    Assert.That(proto.HasIndex<SpeciesPrototype>(id), $"A preset lists unknown species {id}.");
                }

                foreach (var species in proto.EnumeratePrototypes<SpeciesPrototype>())
                {
                    if (!species.RoundStart || pair.IsTestPrototype(species) || settings.ExcludedSpecies.Contains(species.ID))
                        continue;

                    listings.TryGetValue(species.ID, out var where);
                    Assert.That(where?.Count ?? 0, Is.EqualTo(1),
                        $"Roundstart species {species.ID} must be listed in exactly one preset or in the fallback's explicitFallback; " +
                        $"found in: {(where == null ? "none" : string.Join(", ", where))}.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ColorPolicyTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var canine = GenitalColorDefaults.FindPreset("Vulpkanin", proto);
            var human = GenitalColorDefaults.FindPreset("Human", proto);
            Assert.That(canine, Is.Not.Null);
            Assert.That(human, Is.Not.Null);
            if (canine == null || human == null)
                return;

            // Vulpkanin skin is not human-toned: the shaft and vulva take the tissue colour, the rest matches skin.
            var vulp = GenitalColorDefaults.Apply(canine.Male.WithVagina(new VaginaProfile(VaginaHuman)), "Vulpkanin", proto);
            // Human skin is human-toned: everything matches skin.
            var person = GenitalColorDefaults.Apply(human.Male.WithVagina(new VaginaProfile(VaginaHuman)), "Human", proto);

            Assert.Multiple(() =>
            {
                Assert.That(canine.ID, Is.EqualTo("GenitalPresetCanine"));
                Assert.That(human.Fallback, Is.True);

                Assert.That(vulp.Penis!.MatchSkin, Is.False);
                Assert.That(vulp.Penis!.Color, Is.EqualTo(canine.TissueColor));
                Assert.That(vulp.Penis!.SheathMatchSkin, Is.True);
                Assert.That(vulp.Testicles!.MatchSkin, Is.True);
                Assert.That(vulp.Vagina!.MatchSkin, Is.False);
                Assert.That(vulp.Vagina!.Color, Is.EqualTo(canine.TissueColor));

                Assert.That(person.Penis!.MatchSkin, Is.True);
                Assert.That(person.Vagina!.MatchSkin, Is.True);
                Assert.That(GenitalColorDefaults.TissueColorFor("Human", proto), Is.Null);
            });
        });

        await pair.CleanReturnAsync();
    }

    private static void CheckEntry(IPrototypeManager proto, GenitalPresetPrototype preset, GenitalProfile entry, string sex)
    {
        Assert.That(entry.IsEmpty, Is.False, $"{preset.ID} {sex} has no organs.");

        foreach (var shape in Shapes(entry))
        {
            Assert.That(proto.HasIndex(shape), $"{preset.ID} {sex}: unknown shape {shape.Id}.");
        }

        var issues = new List<GenitalIssue>();
        var validated = GenitalProfileValidator.ValidateOrgans(entry, proto, issues);
        Assert.That(issues, Is.Empty, $"{preset.ID} {sex}: {string.Join(", ", issues)}.");
        Assert.That(validated.MemberwiseEquals(entry), Is.True, $"{preset.ID} {sex} does not validate to itself.");
    }

    private static IEnumerable<ProtoId<GenitalShapePrototype>> Shapes(GenitalProfile profile)
    {
        if (profile.Penis != null)
            yield return profile.Penis.Shape;

        if (profile.Vagina != null)
            yield return profile.Vagina.Shape;

        if (profile.Breasts != null)
            yield return profile.Breasts.Shape;
    }

    private static void AddListing(Dictionary<string, List<string>> listings, string species, string where)
    {
        if (!listings.TryGetValue(species, out var list))
        {
            list = new List<string>();
            listings[species] = list;
        }

        list.Add(where);
    }
}
