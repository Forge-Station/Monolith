#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Shitmed.Medical.Surgery.Tools;
using Content.Shared._WF.Wolfmed.Surgery;
using Content.Shared.Tools.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Feedback #136: a tool with several modes does the surgery each of its modes stands in for. A power drill unbolts a
/// core housing like a wrench, jaws of life pull a round like wirecutters, an omnitool re-flashes a core like a
/// multitool.
/// </summary>
[TestFixture]
public sealed class WolfmedMultiToolSurgeryTest : WolfmedGameTest
{
    /// <summary>Tools with a mode that is not really that tool: a jack, a shovel.</summary>
    private static readonly HashSet<string> Excluded = ["MaintenanceJack", "NFShovel"];

    [Test]
    public async Task EveryModeCarriesItsSurgeryTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var proto in prototypes.EnumeratePrototypes<EntityPrototype>().OrderBy(p => p.ID))
                {
                    if (proto.Abstract || Excluded.Contains(proto.ID) ||
                        !proto.TryGetComponent(out MultipleToolComponent? tool, factory))
                        continue;

                    var modes = tool.Entries.SelectMany(entry => entry.Behavior).Select(q => q.ToString()).ToHashSet();
                    if (proto.ID == "PowerDrill")
                        Assert.That(modes, Does.Contain("Anchoring"), "the drill's modes were not read; this test proves nothing.");
                    void Needs<T>(string mode) where T : IComponent
                    {
                        if (modes.Contains(mode))
                            Assert.That(proto.Components.ContainsKey(factory.GetComponentName(typeof(T))), Is.True,
                                $"{proto.ID} works as a {mode} tool but cannot do that tool's surgery ({typeof(T).Name}).");
                    }

                    Needs<WolfmedHullPlateComponent>("Anchoring");
                    Needs<WolfmedCoreProbeComponent>("Pulsing");
                    Needs<HemostatComponent>("Cutting");
                    Needs<HemostatComponent>("Screwing");
                }
            });
        });
    }
}
