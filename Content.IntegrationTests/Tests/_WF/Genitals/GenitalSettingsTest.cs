using Content.Shared._Common.Consent;
using Content.Shared._WF.Genitals.Prototypes;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Genitals.GenitalTestHelpers;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>The Default settings name the strip and surgery toggles once those toggles exist.</summary>
[TestFixture]
[TestOf(typeof(GenitalSettingsPrototype))]
public sealed class GenitalSettingsTest
{
    /// <summary>An unset toggle refuses every strip or surgery by others, so a toggle that exists but is not wired is a bug.</summary>
    [Test]
    public async Task ConsentTogglesWiredTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var settings = proto.Index(GenitalSettingsPrototype.DefaultId);
            ProtoId<ConsentTogglePrototype> strip = StripToggle;
            ProtoId<ConsentTogglePrototype> surgery = SurgeryToggle;

            Assert.Multiple(() =>
            {
                if (proto.HasIndex(strip))
                    Assert.That(settings.StripConsent, Is.EqualTo(strip), $"{strip} exists, so settings.yml must set stripConsent to it.");

                if (proto.HasIndex(surgery))
                    Assert.That(settings.SurgeryConsent, Is.EqualTo(surgery), $"{surgery} exists, so settings.yml must set surgeryConsent to it.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
