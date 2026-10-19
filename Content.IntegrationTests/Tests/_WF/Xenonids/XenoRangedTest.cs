#nullable enable
using System.Linq;
using Content.Shared._WF.RMCCore.Actions;
using Content.Shared._WF.Xenonids;
using Content.Shared._WF.Xenonids.Bombard;
using Content.Shared._WF.Xenonids.Burrow;
using Content.Shared._WF.Xenonids.GasToggle;
using Content.Shared._WF.Xenonids.Paralyzing;
using Content.Shared._WF.Xenonids.Spray;
using Content.Shared._WF.Xenonids.Stomp;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>The ranged castes: boiler, spitter, sentinel and burrower spawn with their abilities and the toggles change state.</summary>
[TestFixture]
public sealed class XenoRangedTest
{
    /// <summary>Each caste spawns as a xeno with its own ability components and actions.</summary>
    [Test]
    public async Task CastesSpawnWithAbilities()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var rmcActions = entities.System<SharedRMCActionsSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            // CMXenoBoiler is abstract; RMCXenoBoiler is the playable boiler
            var boiler = entities.SpawnEntity("RMCXenoBoiler", map.GridCoords);
            var spitter = entities.SpawnEntity("CMXenoSpitter", map.GridCoords);
            var sentinel = entities.SpawnEntity("CMXenoSentinel", map.GridCoords);
            var burrower = entities.SpawnEntity("CMXenoBurrower", map.GridCoords);

            foreach (var xeno in new[] { boiler, spitter, sentinel, burrower })
            {
                Assert.That(entities.HasComponent<XenoComponent>(xeno), $"{entities.ToPrettyString(xeno)} is a xeno.");
            }

            Assert.That(entities.HasComponent<XenoBombardComponent>(boiler));
            Assert.That(entities.HasComponent<XenoGasToggleComponent>(boiler));
            Assert.That(rmcActions.GetActionsWithEvent<XenoBombardActionEvent>(boiler).Any(), "The boiler gets Bombard.");
            Assert.That(rmcActions.GetActionsWithEvent<XenoGasToggleActionEvent>(boiler).Any(), "The boiler gets Toggle Gas Type.");

            Assert.That(entities.HasComponent<XenoSprayAcidComponent>(spitter));
            Assert.That(rmcActions.GetActionsWithEvent<XenoSprayAcidActionEvent>(spitter).Any(), "The spitter gets Spray Acid.");

            Assert.That(entities.HasComponent<XenoParalyzingSlashComponent>(sentinel));
            Assert.That(rmcActions.GetActionsWithEvent<XenoParalyzingSlashActionEvent>(sentinel).Any(), "The sentinel gets Paralyzing Slash.");

            Assert.That(entities.HasComponent<XenoBurrowComponent>(burrower));
            Assert.That(entities.HasComponent<XenoStompComponent>(burrower));
            Assert.That(rmcActions.GetActionsWithEvent<XenoBurrowActionEvent>(burrower).Any(), "The burrower gets Burrow.");
            Assert.That(rmcActions.GetActionsWithEvent<XenoStompActionEvent>(burrower).Any(), "The burrower gets Tremor.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Toggle Gas Type flips the boiler between acid and neurotoxin and back.</summary>
    [Test]
    public async Task BoilerTogglesGasType()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var rmcActions = entities.System<SharedRMCActionsSystem>();
        var map = await pair.CreateTestMap();

        EntityUid boiler = default;
        await server.WaitPost(() => boiler = entities.SpawnEntity("RMCXenoBoiler", map.GridCoords));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var toggle = entities.GetComponent<XenoGasToggleComponent>(boiler);
            var bombard = entities.GetComponent<XenoBombardComponent>(boiler);
            var action = rmcActions.GetActionsWithEvent<XenoGasToggleActionEvent>(boiler).First();
            var first = bombard.Projectile;
            Assert.That(toggle.IsNeurotoxin, Is.False);

            var ev = new XenoGasToggleActionEvent { Performer = boiler, Action = action };
            entities.EventBus.RaiseLocalEvent(boiler, ev);
            Assert.That(ev.Handled);
            Assert.That(toggle.IsNeurotoxin, Is.True, "The first toggle switches to neurotoxin.");
            Assert.That(bombard.Projectile, Is.Not.EqualTo(first), "Bombard fires the other glob.");

            ev = new XenoGasToggleActionEvent { Performer = boiler, Action = action };
            entities.EventBus.RaiseLocalEvent(boiler, ev);
            Assert.That(toggle.IsNeurotoxin, Is.False, "The second toggle switches back to acid.");
            Assert.That(bombard.Projectile, Is.EqualTo(first));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Burrowing takes a doafter, then the burrower is underground until it resurfaces.</summary>
    [Test]
    public async Task BurrowerBurrowsAndResurfaces()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var rmcActions = entities.System<SharedRMCActionsSystem>();
        var map = await pair.CreateTestMap();

        EntityUid burrower = default;
        await server.WaitPost(() => burrower = entities.SpawnEntity("CMXenoBurrower", map.GridCoords));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var burrow = entities.GetComponent<XenoBurrowComponent>(burrower);
            var action = rmcActions.GetActionsWithEvent<XenoBurrowActionEvent>(burrower).First();
            Assert.That(burrow.Active, Is.False);

            var ev = new XenoBurrowActionEvent { Performer = burrower, Action = action, Target = map.GridCoords };
            entities.EventBus.RaiseLocalEvent(burrower, ev);
        });

        await pair.RunSeconds(3);

        await server.WaitAssertion(() =>
        {
            var burrow = entities.GetComponent<XenoBurrowComponent>(burrower);
            Assert.That(burrow.Active, Is.True, "The burrower is underground once the doafter ends.");

            var up = new BurrowedEvent(false);
            entities.EventBus.RaiseLocalEvent(burrower, ref up);
            Assert.That(burrow.Active, Is.False, "Resurfacing clears the burrow.");
        });

        await pair.CleanReturnAsync();
    }
}
