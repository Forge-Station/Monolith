using System.Linq;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Components;
using Content.Server._Mono.NPC.HTN;
using Content.Server.Shuttles.Systems;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Server._Forge.KIAS.Controllers;
using Content.Shared._Mono.Company;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.KIAS;

public sealed class KiasNavigationSystem : EntitySystem
{
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private KiasSafetySystem _notifications = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedShuttleSystem _shuttles = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<FTLCompletedEvent>(OnFtl, after: new[] { typeof(FTLAntiCollisionSystem) });
        SubscribeLocalEvent<KiasAutopilotArrivedEvent>(OnAutopilot);
        SubscribeLocalEvent<ShipSteererComponent, ComponentShutdown>(OnSteeringShutdown);
        SubscribeLocalEvent<KiasBluespaceDisturbanceEvent>(OnContact);
    }

    private void OnSteeringShutdown(EntityUid uid, ShipSteererComponent component, ref ComponentShutdown args)
    {
        if (component.Status != ShipSteeringStatus.InRange || !HasComp<ShuttleConsoleComponent>(uid)
            || Transform(uid).GridUid is not { } grid || TerminatingOrDeleted(uid))
            return;
        var arrived = new KiasAutopilotArrivedEvent(grid, uid);
        RaiseLocalEvent(grid, ref arrived, true);
    }

    private void OnFtl(ref FTLCompletedEvent args)
    {
        if (_kias.ActiveGrids.Count == 0 || !TryComp<TransformComponent>(args.Entity, out var xform))
            return;
        var arrival = _transform.GetMapCoordinates(args.Entity, xform);
        var announced = new HashSet<EntityUid>();
        foreach (var horizon in _lookup.GetEntitiesInRange<KiasHorizonComponent>(arrival, 2000f))
        {
            if (!_kias.IsOnline(horizon) || Transform(horizon).GridUid is not { } grid || grid == args.Entity
                || !_kias.HasRole(grid, KiasDeviceRole.Navigation))
                continue;
            var position = _transform.GetMapCoordinates(horizon);
            if (position.MapId != arrival.MapId)
                continue;
            var offset = arrival.Position - position.Position;
            var range = Math.Clamp(horizon.Comp.Range, 0, 2000f);
            if (offset.LengthSquared() > range * range)
                continue;
            var ev = new KiasBluespaceDisturbanceEvent(grid, args.Entity, offset.Length(), (Angle.FromWorldVec(offset).Degrees + 360) % 360, Classify(grid, args.Entity));
            var controllers = EntityManager.System<KiasControllerIoSystem>();
            controllers.Emit(horizon, "Horizon", "ContactEntity", KiasGraphValue.Reference(args.Entity));
            controllers.Emit(horizon, "Horizon", "Distance", KiasGraphValue.Numeric(ev.Distance));
            controllers.Emit(horizon, "Horizon", "Bearing", KiasGraphValue.Numeric(ev.Bearing));
            controllers.Emit(horizon, "Horizon", "Disposition", KiasGraphValue.Enumeration((int) ev.Disposition));
            controllers.Emit(horizon, "Horizon", "Contact", KiasGraphValue.Pulse);
            if (!announced.Add(grid)) continue;
            RaiseLocalEvent(grid, ref ev, true);
        }
    }

    public KiasContactDisposition Classify(EntityUid ownGrid, EntityUid contact)
    {
        if (!TryComp<KiasGridComponent>(ownGrid, out var runtime) || !runtime.Online.Any(uid => _kias.IsOnline(uid) && HasComp<KiasIffReceiverComponent>(uid)))
            return KiasContactDisposition.Unknown;
        if (_shuttles.GetIFFLabel(contact) == null)
            return KiasContactDisposition.Unknown;
        if (TryComp<CompanyComponent>(ownGrid, out var ownCompany) && TryComp<CompanyComponent>(contact, out var company)
            && company.CompanyName != "None" && company.CompanyName == ownCompany.CompanyName)
            return KiasContactDisposition.Friendly;
        if (TryComp<ShuttleFactionComponent>(ownGrid, out var ownFaction) && TryComp<ShuttleFactionComponent>(contact, out var faction))
        {
            if (!string.IsNullOrWhiteSpace(faction.Faction) && faction.Faction != "None" && faction.Faction == ownFaction.Faction)
                return KiasContactDisposition.Friendly;
            if (_prototypes.HasIndex<NpcFactionPrototype>(faction.Faction) && _prototypes.HasIndex<NpcFactionPrototype>(ownFaction.Faction)
                && _factions.IsFactionHostile(faction.Faction, ownFaction.Faction))
                return KiasContactDisposition.Hostile;
        }
        return HasComp<IFFComponent>(contact) ? KiasContactDisposition.Neutral : KiasContactDisposition.Unknown;
    }

    private void OnContact(ref KiasBluespaceDisturbanceEvent args)
    {
        if (!_kias.HasRole(args.Grid, KiasDeviceRole.Navigation))
            return;
        _notifications.Publish(args.Grid, Loc.GetString("kias-contact",
            ("disposition", Loc.GetString($"kias-contact-{args.Disposition.ToString().ToLowerInvariant()}")),
            ("range", MathF.Round(args.Distance / 1000f, 1)), ("bearing", Math.Round(args.Bearing))),
            args.Disposition is KiasContactDisposition.Unknown or KiasContactDisposition.Hostile, announce: false, key: $"contact:{args.Contact}");
    }

    private void OnAutopilot(ref KiasAutopilotArrivedEvent args)
    {
        if (_kias.HasRole(args.Grid, KiasDeviceRole.Navigation))
            _notifications.Publish(args.Grid, Loc.GetString("kias-autopilot-arrived"), announce: false);
    }
}
