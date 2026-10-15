using System.Linq;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Atmos.EntitySystems;
using Robust.Shared.Map.Components;
using Content.Server.Light.EntitySystems;
using Content.Shared._Forge.KIAS;
using Content.Shared.Chemistry.Components;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Verbs;
using Content.Shared.Popups;
using Content.Shared.Interaction;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;

namespace Content.Server._Forge.KIAS;

public sealed class KiasActuatorSystem : EntitySystem
{
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private PoweredLightSystem _lights = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SmokeSystem _smoke = default!;
    [Dependency] private KiasSafetySystem _safety = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _groupLights = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<KiasLightGroupComponent, ComponentStartup>(OnLightStartup);
        SubscribeLocalEvent<KiasLightGroupComponent, ComponentShutdown>(OnLightShutdown);
        SubscribeLocalEvent<KiasLightGroupComponent, EntParentChangedMessage>(OnLightParent);
        SubscribeLocalEvent<KiasLightGroupComponent, GridUidChangedEvent>(OnLightGrid);
        SubscribeLocalEvent<KiasSetLightGroupEvent>(OnGroup);
        SubscribeLocalEvent<KiasLightControllerComponent, GetVerbsEvent<AlternativeVerb>>(OnLightVerbs);
        SubscribeLocalEvent<KiasSuppressionComponent, GetVerbsEvent<AlternativeVerb>>(OnSuppressionVerbs);
        SubscribeLocalEvent<KiasLightControllerComponent, SignalReceivedEvent>(OnLightSignal);
        SubscribeLocalEvent<KiasSuppressionComponent, SignalReceivedEvent>(OnSuppressionSignal);
    }

    private void OnLightStartup(Entity<KiasLightGroupComponent> ent, ref ComponentStartup args) => Index(ent);
    private void OnLightShutdown(Entity<KiasLightGroupComponent> ent, ref ComponentShutdown args) => Unindex(ent);
    private void OnLightParent(Entity<KiasLightGroupComponent> ent, ref EntParentChangedMessage args) => Index(ent);
    private void OnLightGrid(Entity<KiasLightGroupComponent> ent, ref GridUidChangedEvent args) => Index(ent);

    private void Unindex(Entity<KiasLightGroupComponent> ent)
    {
        if (ent.Comp.IndexedGrid is { } old && _groupLights.TryGetValue(old, out var lights))
        {
            lights.Remove(ent);
            if (lights.Count == 0)
                _groupLights.Remove(old);
        }
        ent.Comp.IndexedGrid = null;
    }

    private void Index(Entity<KiasLightGroupComponent> ent)
    {
        Unindex(ent);
        _metadata.AddFlag(ent, MetaDataFlags.ExtraTransformEvents);
        if (TerminatingOrDeleted(ent) || Transform(ent).GridUid is not { } grid)
            return;
        if (!_groupLights.TryGetValue(grid, out var lights))
            _groupLights.Add(grid, lights = new());
        lights.Add(ent);
        ent.Comp.IndexedGrid = grid;
    }

    private void OnGroup(ref KiasSetLightGroupEvent args)
    {
        if (!_kias.ActiveGrids.Contains(args.Grid) || Comp<KiasGridComponent>(args.Grid).Testing || !_groupLights.TryGetValue(args.Grid, out var lights))
            return;
        var group = args.Group;
        var controller = Comp<KiasGridComponent>(args.Grid).Online.OrderBy(uid => uid.Id)
            .FirstOrDefault(uid => _kias.IsOnline(uid) && TryComp<KiasLightControllerComponent>(uid, out var comp) && comp.Group == group);
        var settings = controller.Valid ? Comp<KiasLightControllerComponent>(controller) : null;
        foreach (var light in lights.ToArray())
        {
            if (!TerminatingOrDeleted(light) && Transform(light).GridUid == args.Grid
                && Comp<KiasLightGroupComponent>(light).Group == args.Group)
            {
                if (settings != null && _lights.GetBulb(light) is { } bulb && TryComp<LightBulbComponent>(bulb, out var component))
                {
                    component.LightEnergy = settings.Brightness;
                    Dirty(bulb, component);
                    EntityManager.System<SharedLightBulbSystem>().SetColor(bulb, Color.FromHex(settings.Color));
                }
                _lights.SetState(light, args.Enabled);
            }
        }
    }

    public void SetLights(EntityUid controller, bool enabled)
    {
        if (!_kias.IsOnline(controller) || !TryComp<KiasLightControllerComponent>(controller, out var comp)
            || Transform(controller).GridUid is not { } grid)
            return;
        var ev = new KiasSetLightGroupEvent(grid, comp.Group, enabled);
        RaiseLocalEvent(grid, ref ev, true);
    }

    public void SetFixture(EntityUid target, bool enabled)
    {
        if (!_kias.IsOnline(target) || !HasComp<KiasLightFixtureComponent>(target)
            || Transform(target).GridUid is not { } grid || Comp<KiasGridComponent>(grid).Testing) return;
        EntityManager.System<Content.Shared.Power.EntitySystems.SharedPowerReceiverSystem>().SetPowerDisabled(target, !enabled);
        _lights.SetState(target, enabled);
    }

    private void OnLightVerbs(Entity<KiasLightControllerComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !_kias.IsOnline(ent) || Transform(ent).GridUid is not { } grid || !_kias.CanConfigure(grid, args.User))
            return;
        var uid = ent.Owner;
        foreach (var enabled in new[] { true, false })
        {
            var state = enabled;
            var actor = args.User;
            args.Verbs.Add(new AlternativeVerb { Text = Loc.GetString(state ? "kias-lights-on" : "kias-lights-off"),
                Act = () => { if (_kias.CanConfigure(grid, actor)) SetLights(uid, state); } });
        }
    }

    private void OnLightSignal(Entity<KiasLightControllerComponent> ent, ref SignalReceivedEvent args)
    {
        if (args.Port is "On" or "Off")
            SetLights(ent, args.Port == "On");
    }

    private void OnSuppressionVerbs(Entity<KiasSuppressionComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || Transform(ent).GridUid is not { } grid || !_kias.CanConfigure(grid, args.User))
            return;
        var uid = ent.Owner;
        var actor = args.User;
        args.Verbs.Add(new AlternativeVerb { Text = Loc.GetString("kias-suppress"), Act = () => Suppress(uid, actor) });
    }

    private void OnSuppressionSignal(Entity<KiasSuppressionComponent> ent, ref SignalReceivedEvent args)
    {
        if (args.Port == "KiasSuppress" && (args.Trigger is not { } source
            || !TerminatingOrDeleted(source) && Transform(source).GridUid == Transform(ent).GridUid
            && (!HasComp<KiasDeviceComponent>(source) || _kias.IsOnline(source))))
            Suppress(ent);
    }

    public bool Suppress(EntityUid uid, EntityUid? actor = null)
    {
        if (TerminatingOrDeleted(uid) || actor is { } user && (Transform(uid).GridUid is not { } actorGrid
            || !_kias.CanConfigure(actorGrid, user) || !_interaction.InRangeUnobstructed(user, uid))) return false;
        if (!_kias.IsOnline(uid) || !HasComp<KiasSuppressionComponent>(uid)
            || Transform(uid).GridUid is not { } grid || !_kias.HasRole(grid, KiasDeviceRole.Atmosphere)
            || Comp<KiasGridComponent>(grid).Testing
            || !_containers.TryGetContainer(uid, "kias-cartridge", out var slot)
            || slot.ContainedEntities.FirstOrDefault() is not { Valid: true } cartridge
            || TerminatingOrDeleted(cartridge) || EntityManager.IsQueuedForDeletion(cartridge)
            || !HasComp<KiasSuppressionCartridgeComponent>(cartridge))
        {
            if (actor is { } recipient)
            {
                var reason = !_kias.IsOnline(uid) ? "kias-suppression-offline"
                    : Transform(uid).GridUid is { } current && !_kias.HasRole(current, KiasDeviceRole.Atmosphere) ? "kias-suppression-no-atmosphere"
                    : Transform(uid).GridUid is { } testing && Comp<KiasGridComponent>(testing).Testing ? "kias-suppression-testing"
                    : "kias-suppression-empty";
                _popup.PopupEntity(Loc.GetString(reason), uid, recipient);
            }
            return false;
        }
        var solution = new Solution();
        solution.AddReagent("Water", 100);
        var transform = Transform(uid);
        var outlet = transform.Coordinates;
        if (TryComp<MapGridComponent>(grid, out var map))
        {
            var tile = EntityManager.System<SharedMapSystem>().TileIndicesFor(grid, map, transform.Coordinates);
            if (EntityManager.System<AtmosphereSystem>().IsTileAirBlocked(grid, tile, mapGridComp: map))
                outlet = new EntityCoordinates(grid, transform.LocalPosition + transform.LocalRotation.ToWorldVec());
        }
        var foam = Spawn("Foam", outlet);
        _smoke.StartSmoke(foam, solution, 10, 12);
        QueueDel(cartridge);
        _safety.Publish(grid, Loc.GetString("kias-suppression-activated", ("location", _safety.Location(grid, uid))));
        return true;
    }
}
