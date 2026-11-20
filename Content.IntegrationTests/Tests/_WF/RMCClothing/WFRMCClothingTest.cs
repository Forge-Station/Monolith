using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.RMCClothing;
using Content.Shared._WF.RMCClothing.Ghillie;
using Content.Shared._WF.RMCClothing.Patterns;
using Content.Shared._WF.RMCCore.Stealth;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.VendingMachines;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.RMCClothing;

/// <summary>The ghillie suit hides and reveals its wearer, and every ported item can be picked in a faction loadout.</summary>
[TestFixture]
public sealed class WFRMCClothingTest
{
    private const string Ghillie = "WFRMCGhillie";
    private const string Cloak = "WFRMCCloakM68";

    [Test]
    public async Task GhillieHidesAndBreaks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var maps = entities.System<SharedMapSystem>();
        var inventory = entities.System<InventorySystem>();
        var ghillies = entities.System<WFGhillieSuitSystem>();

        EntityUid human = default;
        EntityUid suit = default;
        WFGhillieSuitComponent suitComp = default!;
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            human = entities.SpawnEntity("MobHuman", coords);
            suit = entities.SpawnEntity(Ghillie, coords);
            suitComp = entities.GetComponent<WFGhillieSuitComponent>(suit);
            Assert.That(inventory.TryEquip(human, suit, "outerClothing", silent: true, force: true), Is.True);
            entities.EventBus.RaiseLocalEvent(suit, new WFGhilliePreparePositionEvent { Performer = human });
            Assert.That(entities.HasComponent<WFGhillieHiddenComponent>(human), Is.False, "Hiding waits for the do-after.");
        });

        // The do-after, then partway through the fade.
        double useDelay = 0, fade = 0;
        await server.WaitPost(() =>
        {
            useDelay = suitComp.UseDelay.TotalSeconds;
            fade = suitComp.InvisibilityDelay.TotalSeconds;
        });
        await server.WaitRunTicks((int) ((useDelay + fade / 2 + 0.5) * timing.TickRate));
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<EntityActiveInvisibleComponent>(human).Opacity,
                Is.GreaterThan(suitComp.Opacity + 0.1f), "The wearer fades out gradually.");
        });

        await server.WaitRunTicks((int) (fade * timing.TickRate));
        await server.WaitAssertion(() =>
        {
            Assert.That(suitComp.Enabled, Is.True);
            Assert.That(entities.HasComponent<WFGhillieHiddenComponent>(human), Is.True);
            Assert.That(entities.HasComponent<EntityTurnInvisibleComponent>(human), Is.True);
            Assert.That(entities.GetComponent<EntityActiveInvisibleComponent>(human).Opacity,
                Is.EqualTo(suitComp.Opacity).Within(0.001f), "The wearer has faded out.");

            // Each shot adds one step of visibility.
            var gun = entities.SpawnEntity("WeaponPistolMk58", entities.GetComponent<TransformComponent>(human).Coordinates);
            var gunComp = entities.GetComponent<GunComponent>(gun);
            for (var i = 0; i < 2; i++)
                entities.EventBus.RaiseLocalEvent(human, new SelfBeforeGunShotEvent(human, (gun, gunComp), new()));
            Assert.That(entities.GetComponent<EntityActiveInvisibleComponent>(human).Opacity,
                Is.EqualTo(suitComp.Opacity + 2 * suitComp.AddedOpacityOnShoot).Within(0.001f));

            // The action again comes out of hiding.
            entities.EventBus.RaiseLocalEvent(suit, new WFGhilliePreparePositionEvent { Performer = human });
            Assert.That(suitComp.Enabled, Is.False);
            Assert.That(entities.HasComponent<WFGhillieHiddenComponent>(human), Is.False);
            Assert.That(entities.HasComponent<EntityActiveInvisibleComponent>(human), Is.False);
            Assert.That(entities.HasComponent<EntityTurnInvisibleComponent>(human), Is.False);

            // Taking the suit off breaks the cover.
            ghillies.SetHidden((suit, suitComp), human, true);
            Assert.That(entities.HasComponent<WFGhillieHiddenComponent>(human), Is.True);
            Assert.That(inventory.TryUnequip(human, "outerClothing", silent: true, force: true), Is.True);
            Assert.That(suitComp.Enabled, Is.False);
            Assert.That(entities.HasComponent<WFGhillieHiddenComponent>(human), Is.False);
            Assert.That(entities.HasComponent<RMCPassiveStealthComponent>(human), Is.False);

            // A thermal cloak on the back takes over the cover from a worn suit.
            Assert.That(inventory.TryEquip(human, suit, "outerClothing", silent: true, force: true), Is.True);
            var cloak = entities.SpawnEntity(Cloak, entities.GetComponent<TransformComponent>(human).Coordinates);
            Assert.That(inventory.TryEquip(human, cloak, "back", silent: true, force: true), Is.True);
            var cloakComp = entities.GetComponent<WFGhillieSuitComponent>(cloak);
            ghillies.SetHidden((suit, suitComp), human, true);
            ghillies.SetHidden((cloak, cloakComp), human, true);
            Assert.That(suitComp.Enabled, Is.False);
            Assert.That(entities.GetComponent<WFGhillieHiddenComponent>(human).Suit, Is.EqualTo(cloak));
            Assert.That(inventory.TryUnequip(human, "back", silent: true, force: true), Is.True);
            Assert.That(entities.HasComponent<WFGhillieHiddenComponent>(human), Is.False);

            // Taking the suit off while settling in, without moving, doesn't hide the wearer.
            entities.EventBus.RaiseLocalEvent(suit, new WFGhilliePreparePositionEvent { Performer = human });
            Assert.That(inventory.TryUnequip(human, "outerClothing", silent: true, force: true), Is.True);
        });

        await server.WaitRunTicks((int) ((useDelay + 1) * timing.TickRate));
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<WFGhillieHiddenComponent>(human), Is.False);
            Assert.That(suitComp.Enabled, Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryItemIsInAFactionLoadout()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var grouped = protos.EnumeratePrototypes<LoadoutGroupPrototype>().Where(g => !g.Hidden) // a hidden group is never shown
                .SelectMany(g => g.Loadouts.Select(l => l.Id))
                .ToHashSet();
            var offered = new HashSet<string>();
            foreach (var loadout in protos.EnumeratePrototypes<LoadoutPrototype>())
            {
                if (!grouped.Contains(loadout.ID))
                    continue;
                offered.UnionWith(loadout.Equipment.Values.Select(e => e.Id));
                offered.UnionWith(loadout.Storage.Values.SelectMany(l => l).Select(e => e.Id));
            }

            var items = protos.EnumeratePrototypes<EntityPrototype>()
                .Where(p => !p.Abstract && p.ID.StartsWith("WFRMC") && !p.ID.StartsWith("WFRMCLook") && p.TryComp<ClothingComponent>(out _, factory))
                .Select(p => p.ID)
                .ToList();
            Assert.That(items, Has.Count.GreaterThan(300));
            var missing = items.Where(i => !offered.Contains(i)).ToList();
            Assert.That(missing, Is.Empty, "not in any loadout group: " + string.Join(", ", missing.Take(20)));

            Assert.That(protos.Index<RoleLoadoutPrototype>("JobPirate").Groups.Select(g => g.Id),
                Does.Contain("WFPdvRanksRakhshan"));
            Assert.That(protos.Index<RoleLoadoutPrototype>("JobPirateCaptain").Groups.Select(g => g.Id),
                Does.Contain("WFPdvRanksGrandVizier"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RoleArmorIsLockedToItsRoles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var profile = HumanoidCharacterProfile.DefaultWithSpecies();
            var effect = protos.Index<LoadoutPrototype>("WFRMCLoadoutArmorM3EOD").Effects
                .OfType<WFRoleWhitelistLoadoutEffect>().Single();
            var deps = server.InstanceDependencyCollection;
            Assert.That(effect.Validate(profile, new RoleLoadout("JobTsfEngineer"), null, deps, out _), Is.True);
            Assert.That(effect.Validate(profile, new RoleLoadout("JobDeputy"), null, deps, out var reason), Is.False);
            Assert.That(reason?.ToString(), Does.Not.Contain("wf-role-whitelist"));

            // Common armor carries no lock.
            Assert.That(protos.Index<LoadoutPrototype>("WFRMCLoadoutArmorM3").Effects, Is.Empty);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OutfittersStockTheirFactionWithoutLimit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protos = pair.Server.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var tsf = protos.Index<VendingMachineInventoryPrototype>("WFRMCTsfOutfitterInventory").StartingInventory;
            var pdv = protos.Index<VendingMachineInventoryPrototype>("WFRMCPdvOutfitterInventory").StartingInventory;
            Assert.That(tsf["WFRMCArmorM3"], Is.EqualTo(uint.MaxValue));
            Assert.That(pdv["WFRMCArmorUM5"], Is.EqualTo(uint.MaxValue));
            Assert.That(tsf, Does.ContainKey("WFRMCGhillie"));
            Assert.That(pdv, Does.ContainKey("WFRMCGhillie"));
            Assert.That(pdv, Does.Not.ContainKey("WFRMCArmorM3"), "TSF gear stays out of the PDV outfitter.");
            Assert.That(tsf, Does.Not.ContainKey("WFRMCArmorM3EOD"), "Role gear stays in the loadouts.");
            Assert.That(tsf.Keys.Concat(pdv.Keys).Where(k => k.StartsWith("WFRMCLook")), Is.Empty);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PatternSwitchesTheLook()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var patterns = entities.System<WFCamoPatternSystem>();

        await server.WaitAssertion(() =>
        {
            entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var armor = entities.SpawnEntity("WFRMCArmorM3", new MapCoordinates(0, 0, mapId));
            var comp = entities.GetComponent<WFCamoPatternComponent>(armor);
            Assert.That(comp.Patterns.Select(p => p.Name), Does.Contain("snow"));
            Assert.That(entities.GetComponent<ClothingComponent>(armor).RsiPath, Does.EndWith("jungle.rsi"));

            patterns.SetPattern((armor, comp), comp.Patterns.Select(p => p.Name).ToList().IndexOf("snow"));
            Assert.That(comp.Patterns[comp.Current].Name, Is.EqualTo("snow"));
            Assert.That(entities.GetComponent<ClothingComponent>(armor).RsiPath, Does.EndWith("snow.rsi"));
            Assert.That(entities.GetComponent<ItemComponent>(armor).RsiPath, Does.EndWith("snow.rsi"));
        });

        await pair.CleanReturnAsync();
    }
}
