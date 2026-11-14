using System.Collections.Generic;
using System.Linq;
using Content.Shared.EntityTable;
using Content.Shared.EntityTable.Conditions;
using Content.Shared.Xenoarchaeology.Artifact;
using Content.Shared.Xenoarchaeology.Artifact.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Xenoarch;

/// <summary>Checks the upgraded entity tables the xenoarch trigger rewrite relies on, and artifact generation on top of them.</summary>
[TestFixture]
public sealed class XenoarchEntityTableTest
{
    private const string Table = "WFXenoarchTestTable";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFXenoarchTestEntA

- type: entity
  id: WFXenoarchTestEntB

- type: entityTable
  id: WFXenoarchTestTable
  table: !type:GroupSelector
    conditionsForChildren:
    - !type:ExcludeEntitiesFromContextCondition
    children:
    - id: WFXenoarchTestEntA
    - id: WFXenoarchTestEntB
";

    /// <summary>An entity excluded through the context is never picked, and the old System.Random overload is deterministic.</summary>
    [Test]
    public async Task ContextExclusionAndSeededRolls()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var tables = server.System<EntityTableSystem>();
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var table = proto.Index<EntityTablePrototype>(Table).Table;
            for (var i = 0; i < 50; i++)
            {
                var ctx = new EntityTableContext
                {
                    { ExcludeEntitiesFromContextCondition.EntitiesToExclude, new HashSet<EntProtoId> { "WFXenoarchTestEntA" } },
                };
                Assert.That(tables.GetSpawns(table, ctx: ctx), Is.EqualTo(new EntProtoId[] { "WFXenoarchTestEntB" }));
            }

            var first = tables.GetSpawns(table, new System.Random(42)).ToList();
            var second = tables.GetSpawns(table, new System.Random(42)).ToList();
            Assert.That(first, Has.Count.EqualTo(1));
            Assert.That(second, Is.EqualTo(first));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Generated artifacts get a node count in range, and every node a trigger, with no trigger repeated on one artifact.</summary>
    [Test]
    public async Task ArtifactsGenerateWithUniqueTriggers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var artifacts = server.System<SharedXenoArtifactSystem>();
        var maps = server.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            var mapUid = maps.CreateMap(out _);
            foreach (var proto in new[] { "ComplexXenoArtifact", "ComplexXenoArtifactItem" })
            {
                for (var i = 0; i < 20; i++)
                {
                    var uid = entities.SpawnEntity(proto, new EntityCoordinates(mapUid, i * 3, 0));
                    var comp = entities.GetComponent<XenoArtifactComponent>(uid);
                    var nodes = artifacts.GetAllNodes((uid, comp)).ToList();

                    Assert.That(nodes, Is.Not.Empty, $"{proto} generated no nodes.");
                    Assert.That(nodes.Count, Is.LessThanOrEqualTo(comp.NodeCount.Max), $"{proto} generated too many nodes.");
                    Assert.That(nodes.All(n => n.Comp.TriggerTip != null), $"{proto} has a node without a trigger.");
                    var tips = nodes.Select(n => n.Comp.TriggerTip).ToList();
                    Assert.That(tips.Distinct().Count(), Is.EqualTo(tips.Count), $"{proto} repeats a trigger.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }
}
