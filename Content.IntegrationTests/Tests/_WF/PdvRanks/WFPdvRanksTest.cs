using System.Linq;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.PdvRanks;

/// <summary>Every PDV rank in the loadout menu has a Fluent name the client can show.</summary>
[TestFixture]
public sealed class WFPdvRanksTest
{
    [Test]
    public async Task RankNamesResolveOnTheClient()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var protos = client.ResolveDependency<IPrototypeManager>();
        var loc = client.ResolveDependency<ILocalizationManager>();

        await client.WaitAssertion(() =>
        {
            var ranks = protos.EnumeratePrototypes<LoadoutGroupPrototype>()
                .Where(g => g.ID.StartsWith("WFPdvRanks"))
                .SelectMany(g => g.Loadouts)
                .Select(l => protos.Index(l).Name)
                .Distinct()
                .ToList();
            Assert.That(ranks, Is.Not.Empty);
            foreach (var name in ranks)
                Assert.That(loc.TryGetString(name, out _), Is.True, name);
        });

        await pair.CleanReturnAsync();
    }
}
