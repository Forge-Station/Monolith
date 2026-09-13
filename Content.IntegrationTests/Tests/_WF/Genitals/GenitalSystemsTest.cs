using System.Linq;
using Content.Client._WF.Genitals;
using Content.Server._WF.Genitals;
using Content.Shared._WF.Genitals.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>Each side runs exactly one instance of the two abstract anatomy systems, and base-type lookups reach it.</summary>
[TestFixture]
public sealed class GenitalSystemsTest
{
    /// <remarks>Connected, because a disconnected client has shut its entity systems down.</remarks>
    [Test]
    public async Task OneInstancePerSideTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server.ResolveDependency<IEntitySystemManager>();
        var client = pair.Client.ResolveDependency<IEntitySystemManager>();

        Assert.Multiple(() =>
        {
            Assert.That(CountSystems<SharedGenitalsSystem>(server), Is.EqualTo(1), "Server instances of SharedGenitalsSystem.");
            Assert.That(CountSystems<GenitalConsentSystem>(server), Is.EqualTo(1), "Server instances of GenitalConsentSystem.");
            Assert.That(CountSystems<SharedGenitalsSystem>(client), Is.EqualTo(1), "Client instances of SharedGenitalsSystem.");
            Assert.That(CountSystems<GenitalConsentSystem>(client), Is.EqualTo(1), "Client instances of GenitalConsentSystem.");

            Assert.That(server.GetEntitySystem<SharedGenitalsSystem>(), Is.TypeOf<GenitalsSystem>());
            Assert.That(server.GetEntitySystem<GenitalConsentSystem>(), Is.TypeOf<ServerGenitalConsentSystem>());
            Assert.That(client.GetEntitySystem<SharedGenitalsSystem>(), Is.TypeOf<ClientGenitalsSystem>());
            Assert.That(client.GetEntitySystem<GenitalConsentSystem>(), Is.TypeOf<ClientGenitalConsentSystem>());
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Instantiated system types that are T or derive from it.</summary>
    private static int CountSystems<T>(IEntitySystemManager systems) where T : IEntitySystem
    {
        return systems.GetEntitySystemTypes().Count(t => typeof(T).IsAssignableFrom(t));
    }
}
