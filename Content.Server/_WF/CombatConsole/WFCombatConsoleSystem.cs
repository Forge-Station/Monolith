using System.Linq;
using System.Numerics;
using Content.Server._Mono.FireControl;
using Content.Server._WF.Cockpit;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Server.Power.EntitySystems;
using Content.Shared._Mono.FireControl;
using Content.Shared._Mono.ShipGuns;
using Content.Shared._WF.CombatConsole;
using Content.Shared.ActionBlocker;
using Content.Shared.Interaction;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._WF.CombatConsole;

/// <summary>Validates fire groups and dispenses powered, connected flares against exposed missile locks.</summary>
public sealed partial class WFCombatConsoleSystem : EntitySystem
{
    [Dependency] private FireControlSystem _fireControl = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    /// <summary>Minimum distance from the console at which a hostile lock counts as a threat.</summary>
    public const float ThreatRange = 250f;
    /// <summary>Seconds of flight at a seeker's top speed that also count as a threat, so fast missiles are met early.</summary>
    public const float ThreatLeadTime = 3f;
    public static readonly TimeSpan BurstInterval = TimeSpan.FromSeconds(15);
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        SubscribeLocalEvent<FireControlConsoleComponent, WFSaveWeaponGroupMessage>(OnSaveGroup);
        SubscribeLocalEvent<FireControlConsoleComponent, WFAutomaticFlaresMessage>(OnAutomatic);
        SubscribeLocalEvent<FireControlConsoleComponent, WFDispenseFlaresMessage>(OnDispense);
        SubscribeLocalEvent<WFFlareLauncherComponent, GunShotEvent>(OnFlareShot);
        SubscribeLocalEvent<WFFlareLauncherComponent, AmmoShotEvent>(OnFlareProjectiles);
    }

    /// <summary>Checks the powered console and server still belong to the same connected grid.</summary>
    public bool TryGetServer(EntityUid uid, FireControlConsoleComponent console,
        out EntityUid serverUid, out FireControlServerComponent server)
    {
        serverUid = console.ConnectedServer ?? EntityUid.Invalid;
        server = default!;
        if (TerminatingOrDeleted(uid) || TerminatingOrDeleted(serverUid) ||
            !TryComp<FireControlServerComponent>(serverUid, out var connected))
            return false;
        server = connected;
        return server.Consoles.Contains(uid) &&
               Transform(uid).Anchored && Transform(uid).GridUid is { } grid &&
               server.ConnectedGrid == grid && Transform(serverUid).GridUid == grid &&
               _power.IsPowered(uid) && _power.IsPowered(serverUid);
    }

    private bool CanOperate(EntityUid uid, FireControlConsoleComponent console, EntityUid actor,
        out EntityUid serverUid, out FireControlServerComponent server)
    {
        if (TryGetServer(uid, console, out serverUid, out server) &&
            (_ui.IsUiOpen(uid, FireControlConsoleUiKey.Key, actor) && CanReach(actor, uid) ||
             EntityManager.System<WFCockpitGunnerySystem>().CanOperate(actor, uid)))
            return true;
        return false;
    }

    /// <summary>The gunnery UI skips engine input validation, so native commands recheck interaction and range as the engine would.</summary>
    private bool CanReach(EntityUid actor, EntityUid uid) =>
        _blocker.CanInteract(actor, uid) &&
        (HasComp<IgnoreUIRangeComponent>(actor) ||
         _interaction.InRangeAndAccessible(actor, uid, _ui.GetUiRange(uid, FireControlConsoleUiKey.Key)));

    private void OnSaveGroup(EntityUid uid, FireControlConsoleComponent console, WFSaveWeaponGroupMessage args)
    {
        if (args.Weapons == null || !WFWeaponGroups.IsValidRequest(args.Slot, args.Weapons.Count) ||
            !CanOperate(uid, console, args.Actor, out _, out var server))
            return;

        var available = server.Controlled.Where(weapon => !TerminatingOrDeleted(weapon) &&
            Transform(weapon).GridUid == server.ConnectedGrid).ToDictionary(weapon => GetNetEntity(weapon), weapon => weapon);
        var flares = available.Where(p => HasComp<WFFlareLauncherComponent>(p.Value)).Select(p => p.Key).ToHashSet();
        var selected = WFWeaponGroups.Filter(args.Weapons, available.Keys.ToHashSet(), flares);
        EnsureComp<WFCombatConsoleComponent>(uid).Groups[args.Slot] =
            selected.Select(net => available[net]).ToHashSet();
        _fireControl.WfRefreshConsole(uid);
    }

    private void OnAutomatic(EntityUid uid, FireControlConsoleComponent console, WFAutomaticFlaresMessage args)
    {
        if (!CanOperate(uid, console, args.Actor, out _, out var server) ||
            args.Enabled && !server.Controlled.Any(HasComp<WFFlareLauncherComponent>))
            return;
        EnsureComp<WFCombatConsoleComponent>(uid).Automatic = args.Enabled;
        _fireControl.WfRefreshConsole(uid);
    }

    private void OnDispense(EntityUid uid, FireControlConsoleComponent console, WFDispenseFlaresMessage args)
    {
        if (!CanOperate(uid, console, args.Actor, out var serverUid, out var server))
            return;
        Dispense(serverUid, server);
        _fireControl.WfRefreshConsole(uid);
    }

    /// <summary>Builds a fresh snapshot; saved memberships survive temporary loss of power.</summary>
    public WFCombatConsoleState GetState(EntityUid uid, FireControlConsoleComponent console)
    {
        if (TerminatingOrDeleted(uid))
            return new WFCombatConsoleState();
        var settings = EnsureComp<WFCombatConsoleComponent>(uid);
        var state = new WFCombatConsoleState { Automatic = settings.Automatic };
        if (!TryGetServer(uid, console, out _, out var server))
            return state;

        state.Threats = settings.Threats = CountThreats(uid, server);
        var shortestCooldown = float.MaxValue;
        foreach (var weapon in server.Controlled)
        {
            if (TerminatingOrDeleted(weapon) || Transform(weapon).GridUid != server.ConnectedGrid)
                continue;
            var net = GetNetEntity(weapon);
            state.WeaponSupplies[net] = GetWeaponSupply(weapon);
            if (TryComp<ShipGunTypeComponent>(weapon, out var type))
                state.WeaponTypes[net] = type.Type;
            if (TryComp<WFFlareLauncherComponent>(weapon, out var flare))
            {
                state.FlareLaunchers.Add(net);
                var ammunition = GetAmmunition(weapon);
                var unlimited = HasUnlimitedSupply(weapon);
                state.UnlimitedSupply |= unlimited;
                state.Ammunition += ammunition;
                if (ammunition > 0 || unlimited)
                    shortestCooldown = Math.Min(shortestCooldown,
                        (float) Math.Max(0, (NextBurst(weapon, flare) - _timing.CurTime).TotalSeconds));
                continue;
            }
            for (var i = 0; i < WFWeaponGroups.Count; i++)
            {
                if (settings.Groups.TryGetValue(i, out var group) && group.Contains(weapon))
                    state.Groups[i].Add(net);
            }
        }
        // Whole seconds keep unchanged snapshots equal during the lockout.
        state.Cooldown = shortestCooldown == float.MaxValue ? 0 : MathF.Ceiling(shortestCooldown);
        return state;
    }

    private int GetAmmunition(EntityUid weapon)
    {
        var ev = new GetAmmoCountEvent();
        RaiseLocalEvent(weapon, ref ev);
        return ev.Count;
    }

    private TimeSpan NextBurst(EntityUid weapon, WFFlareLauncherComponent flare) =>
        TryComp<GunComponent>(weapon, out var gun) && gun.NextFire > flare.NextBurst ? gun.NextFire : flare.NextBurst;

    private bool HasUnlimitedSupply(EntityUid weapon)
    {
        if (TryComp<BallisticAmmoProviderComponent>(weapon, out var provider) && provider.InfiniteUnspawned)
            return true;
        return _containers.TryGetContainer(weapon, "gun_magazine", out var container) &&
               container is ContainerSlot { ContainedEntity: { } magazine } &&
               TryComp<BallisticAmmoProviderComponent>(magazine, out var ammo) && ammo.InfiniteUnspawned;
    }

    private void OnFlareShot(EntityUid uid, WFFlareLauncherComponent flare, ref GunShotEvent args)
    {
        flare.NextBurst = _timing.CurTime + BurstInterval;
    }

    /// <summary>Uses the existing firing path, including power, ammunition, FTL and pacifist restrictions.</summary>
    private void Dispense(EntityUid serverUid, FireControlServerComponent server)
    {
        if (server.ConnectedGrid is not { } grid || !_fireControl.CanFireWeapons(grid))
            return;
        foreach (var weapon in server.Controlled.ToArray())
        {
            if (!TryComp<WFFlareLauncherComponent>(weapon, out var flare) ||
                NextBurst(weapon, flare) > _timing.CurTime || !_power.IsPowered(weapon) ||
                GetAmmunition(weapon) <= 0 && !HasUnlimitedSupply(weapon) ||
                Transform(weapon).GridUid != grid || !Transform(weapon).Anchored ||
                !TryComp<FireControllableComponent>(weapon, out var control) || control.ControllingServer != serverUid ||
                !_fireControl.WfFlareTarget(weapon, out var target))
                continue;

            _fireControl.FireWeapons(serverUid, new List<NetEntity> { GetNetEntity(weapon) }, target, server);
        }
    }

    /// <summary>Counts current hostile locks when publishing state, including the first reopen after inactivity.</summary>
    private int CountThreats(EntityUid uid, FireControlServerComponent server)
    {
        var count = 0;
        var grid = server.ConnectedGrid!.Value;
        var origin = _transform.GetMapCoordinates(uid);
        var seekers = EntityQueryEnumerator<TargetSeekingComponent, ProjectileComponent, TransformComponent>();
        while (seekers.MoveNext(out _, out var seeker, out var projectile, out var xform))
        {
            if (projectile.ProjectileSpent || !seeker.Launched || !seeker.ExposesTracking || seeker.SeekingDisabled ||
                seeker.CurrentTarget is not { } target || TerminatingOrDeleted(target) ||
                target != grid && Transform(target).GridUid != grid || xform.MapID != origin.MapId ||
                projectile.Shooter is { } shooter && TryComp(shooter, out TransformComponent? shooterXform) &&
                shooterXform.GridUid == grid)
                continue;
            var range = Math.Max(ThreatRange, seeker.MaxSpeed * ThreatLeadTime);
            if (Vector2.DistanceSquared(origin.Position, _transform.GetWorldPosition(xform)) <= range * range)
                count++;
        }
        return count;
    }

    public override void Update(float frameTime)
    {
        if (_nextUpdate > _timing.CurTime)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(0.25);
        UpdateFlareDecoys();

        var cockpit = EntityManager.System<WFCockpitGunnerySystem>();
        var consoles = EntityQueryEnumerator<WFCombatConsoleComponent, FireControlConsoleComponent>();
        while (consoles.MoveNext(out var uid, out var settings, out var console))
        {
            var open = _ui.IsUiOpen(uid, FireControlConsoleUiKey.Key) || cockpit.HasViewers(uid);
            if (!open && !settings.Automatic)
                continue;
            // One periodic snapshot serves every window and cockpit linked to this console.
            if (open)
                _fireControl.WfRefreshConsole(uid, periodic: true);
            if (!settings.Automatic || !TryGetServer(uid, console, out var serverUid, out var server))
                continue;
            if (!open)
                settings.Threats = CountThreats(uid, server);
            if (settings.Threats > 0)
                Dispense(serverUid, server);
        }
    }
}
