using System.Linq;
using Content.Shared._WF.Genitals.Migration;
using Content.Shared._WF.Genitals.Prototypes;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>
/// The legacy genital markings stay deleted. The client no longer hides Genital-category markings from viewers
/// without adult content, so one ported back in would be drawn for everyone; anatomy belongs to the organ system.
/// </summary>
[TestFixture]
[TestOf(typeof(MarkingPrototype))]
public sealed class NoGenitalMarkingsTest
{
    /// <summary>Sprite layers that only the legacy genital markings drew into.</summary>
    private static readonly HumanoidVisualLayers[] LegacyLayers =
    {
        HumanoidVisualLayers.Genital, HumanoidVisualLayers.Penis, HumanoidVisualLayers.Breasts,
    };

    /// <summary>No marking, on either side, has the Genital category, draws into a legacy genital layer, or reuses a legacy table id.</summary>
    [Test]
    public async Task NoGenitalCategoryMarkingsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var serverProto = pair.Server.ResolveDependency<IPrototypeManager>();
        var clientProto = pair.Client.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() => AssertNoGenitalMarkings(serverProto, "server"));
        await pair.Client.WaitAssertion(() => AssertNoGenitalMarkings(clientProto, "client"));

        await pair.CleanReturnAsync();
    }

    /// <summary>The adult-only markings the settings name (the pregnancy overlays left in genitals.yml) still exist.</summary>
    [Test]
    public async Task AdultOnlyMarkingsKeptTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var settings = proto.Index(GenitalSettingsPrototype.DefaultId);
            Assert.Multiple(() =>
            {
                Assert.That(settings.AdultOnlyMarkings, Is.Not.Empty);
                foreach (var id in settings.AdultOnlyMarkings)
                {
                    Assert.That(proto.HasIndex<MarkingPrototype>(id), $"adultOnlyMarkings names unknown marking {id}.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    private static void AssertNoGenitalMarkings(IPrototypeManager proto, string side)
    {
        var markings = proto.EnumeratePrototypes<MarkingPrototype>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(markings, Is.Not.Empty, $"{side}: no marking prototypes loaded.");
            foreach (var marking in markings)
            {
                Assert.That(marking.MarkingCategory, Is.Not.EqualTo(MarkingCategories.Genital),
                    $"{side}: {marking.ID} is a Genital-category marking; anatomy is drawn by the organ system.");
                Assert.That(LegacyLayers, Does.Not.Contain(marking.BodyPart),
                    $"{side}: {marking.ID} draws into the legacy genital layer {marking.BodyPart}.");
                Assert.That(LegacyGenitalMarkings.IsLegacyId(marking.ID), Is.False,
                    $"{side}: {marking.ID} reuses a legacy genital marking id, which the profile migration strips.");
            }
        });
    }
}
