using System.Linq;
using Content.Shared._Mono.Company;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Asf;

/// <summary>Checks which ASF roles open which Lantern Post doors and lockers, and that every ASF role can use the shipyard console.</summary>
[TestFixture]
public sealed class AsfAccessTest
{
    private static readonly string[] Jobs = ["WFAsfEnvoy", "WFAsfEnforcer", "WFAsfFieldResearcher", "WFAsfColonist"];

    /// <summary>Lock, then the jobs whose ID opens it.</summary>
    private static readonly (string Target, string[] Allowed)[] Locks =
    [
        ("WFAsfAirlock", Jobs),
        ("WFAsfAirlockBridge", ["WFAsfEnvoy", "WFAsfEnforcer", "WFAsfFieldResearcher"]),
        ("WFAsfAirlockSecurity", ["WFAsfEnvoy", "WFAsfEnforcer"]),
        ("WFAsfLockerSecurity", ["WFAsfEnvoy", "WFAsfEnforcer"]),
        ("WFAsfAirlockCommand", ["WFAsfEnvoy"]),
    ];

    private static readonly EntProtoId ShipyardConsole = "WFAsfComputerShipyard";

    [Test]
    public async Task AsfRolesOpenTheirLocks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var access = entities.System<AccessReaderSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            foreach (var job in Jobs)
            {
                var card = entities.SpawnEntity(job + "IDCard", map.GridCoords);
                foreach (var (target, allowed) in Locks)
                {
                    var lockEnt = entities.SpawnEntity(target, map.GridCoords);
                    Assert.That(access.IsAllowed(card, lockEnt), Is.EqualTo(allowed.Contains(job)),
                        allowed.Contains(job) ? $"{job} should open {target}." : $"{job} shouldn't open {target}.");
                    entities.DeleteEntity(lockEnt);
                }

                Assert.That(prototypes.Index<JobPrototype>(job).AssignedCompany.Id, Is.EqualTo("WFAsf"));
            }

            // A buyer's only ID card is in the console's slot while they buy, so the console must not check ID access.
            var console = prototypes.Index(ShipyardConsole);
            Assert.That(console.TryGetComponent<AccessReaderComponent>(out _, factory), Is.False,
                $"{ShipyardConsole} checks ID access, which denies a buyer whose card is in its slot.");
            Assert.That(console.TryGetComponent<CompanyAccessReaderComponent>(out var company, factory), Is.True);
            Assert.That(company!.RequiredCompanies.Select(c => c.Id), Does.Contain("WFAsf"));
        });

        await pair.CleanReturnAsync();
    }
}
