using System.Linq;
using Content.Shared._WF.RoundSetup;
using Content.Server.Access.Components;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.PdvMedic;

/// <summary>The PDV Jarrah is a PDV job with its medic loadout, rank ladder and ID.</summary>
[TestFixture]
public sealed class WFPdvJarrahTest
{
    private const string Job = "WFPdvJarrah";

    [Test]
    public async Task JarrahIsAPdvMedic()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var job = protos.Index<JobPrototype>(Job);
            Assert.That(job.AssignedCompany, Is.EqualTo("PDV"));
            Assert.That(protos.Index<DepartmentPrototype>("Antag").Roles.Select(r => r.Id), Does.Contain(Job));
            Assert.That(WFFactionCrewPolicy.Rank(WFCrewFaction.PDV, Job),
                Is.EqualTo(WFFactionCrewPolicy.Rank(WFCrewFaction.PDV, "Pirate")));

            var groups = protos.Index<RoleLoadoutPrototype>("Job" + Job).Groups.Select(g => g.Id).ToList();
            Assert.That(groups, Does.Contain("WFPdvRanksJarrah"));
            Assert.That(groups, Does.Contain("WFPdvJarrahHud"));

            var map = entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var card = entities.SpawnEntity("WFPdvJarrahIDCard", new MapCoordinates(0, 0, mapId));
            Assert.That(entities.GetComponent<PresetIdCardComponent>(card).JobName?.Id, Is.EqualTo(Job));
            entities.DeleteEntity(map);
        });

        await pair.CleanReturnAsync();
    }
}
