using System.Numerics;
using Content.Server._Mono.FireControl;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._Crescent.ShipShields;
using Content.Shared.Construction.Components;
using Content.Shared._WF.Announcements;
using Content.Shared._WF.Lattice;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.Lattice;

/// <summary>
/// An anchor station's answer to being shot at, slow to anger. Hits on its shield are counted per ship; after enough
/// of them the station warns the sector it is under fire, and does nothing else. Only once its shield is down does it
/// fight: it alerts again, calls its drones, and turns its lances on every ship that has fired on it lately and still
/// has someone alive aboard. While the shield holds, or once a ship is dead or quiet, the lances stay silent. The
/// lances fire only from an anchor station.
/// </summary>
public sealed partial class WFLatticeSystem
{
    [Dependency] private FireControlSystem _fireControl = default!;

    /// <summary>Seconds between the lances' aiming passes.</summary>
    private const float DefenceInterval = 0.25f;

    private float _defenceAccumulator;
    private readonly List<EntityUid> _expired = new();

    private void InitializeDefence()
    {
        SubscribeLocalEvent<WFLatticeDefenceWeaponComponent, ShotAttemptedEvent>(OnLanceShotAttempted);
    }

    /// <summary>A lance taken off its anchor station is dead weight.</summary>
    private void OnLanceShotAttempted(Entity<WFLatticeDefenceWeaponComponent> ent, ref ShotAttemptedEvent args)
    {
        if (Transform(ent.Owner).GridUid is not { } grid || !HasComp<WFLatticeAnchorStationComponent>(grid))
            args.Cancel();
    }

    /// <summary>Counts a hit on a station from a grid; enough of them and the station says it is under fire.</summary>
    public void Strike(EntityUid station, EntityUid? attacker, float weight)
    {
        // Another anchor's stray bolts are not an attack: the stations never turn on each other.
        if (attacker is not { } enemy
            || enemy == station
            || HasComp<WFLatticeAnchorStationComponent>(enemy)
            || !Exists(enemy)
            || !TryComp<WFLatticeAnchorStationComponent>(station, out var comp)
            || !TryGetProfile(station, out var profile))
            return;

        var now = _timing.CurTime;
        var strikes = comp.Strikes.TryGetValue(enemy, out var last) && (now - last.Last).TotalSeconds < profile.StrikeWindow
            ? last.Strikes
            : 0f;
        strikes += weight;
        comp.Strikes[enemy] = (strikes, now);

        // Every ship that keeps firing is remembered as an attacker, for when the shield gives.
        comp.Hostiles[enemy] = now + TimeSpan.FromSeconds(profile.HostileSeconds);

        if (strikes >= profile.StrikeLimit && now >= comp.NextAlert)
        {
            comp.NextAlert = now + TimeSpan.FromSeconds(profile.AlertCooldown);
            Alert(station, comp, "wf-lattice-defence-under-fire", enemy, false);
        }
    }

    /// <summary>
    /// Something hit the station directly: it alerts the sector, calls its drones and turns its lances on the attacker at
    /// once, shield or no shield. The attacker is the ship it hit from, or the person themselves if they are on foot.
    /// </summary>
    public void Provoke(EntityUid station, EntityUid? origin)
    {
        if (origin is not { } source
            || Attacker(station, source) is not { } enemy
            || !TryComp<WFLatticeAnchorStationComponent>(station, out var comp)
            || !TryGetProfile(station, out var profile))
            return;

        var now = _timing.CurTime;
        comp.Hostiles[enemy] = now + TimeSpan.FromSeconds(profile.HostileSeconds);
        comp.ProvokedUntil = now + TimeSpan.FromSeconds(profile.HostileSeconds);
        // Engaged either way, so the defence pass doesn't raise a second alarm while this one's cooldown runs.
        var alarm = !comp.Engaged && now >= comp.NextAlert;
        comp.Engaged = true;
        if (alarm)
        {
            comp.NextAlert = now + TimeSpan.FromSeconds(profile.AlertCooldown);
            Alert(station, comp, "wf-lattice-defence", enemy, true);
        }

        Defend(station, enemy);
    }

    /// <summary>What a station shoots back at for a hit from this source: a person on foot, or the grid it came from.</summary>
    private EntityUid? Attacker(EntityUid station, EntityUid source)
    {
        if (!Exists(source))
            return null;

        var grid = Transform(source).GridUid;
        EntityUid enemy;
        if (grid is { } from && from != station && !HasComp<MapComponent>(from))
            enemy = from;
        else if (HasComp<MobStateComponent>(source))
            enemy = source;
        else
            return null;

        return HasComp<WFLatticeAnchorStationComponent>(enemy) ? null : enemy;
    }

    /// <summary>Every so often marks what can be damaged on each station, so builds and repairs are covered too.</summary>
    private void MarkParts(TimeSpan now)
    {
        var stations = EntityQueryEnumerator<WFLatticeAnchorStationComponent, TransformComponent>();
        while (stations.MoveNext(out _, out var comp, out var xform))
        {
            if (now < comp.NextMark)
                continue;

            comp.NextMark = now + TimeSpan.FromSeconds(30);
            // Only what stands on the station itself, not the whole world.
            var children = xform.ChildEnumerator;
            while (children.MoveNext(out var uid))
            {
                if (HasComp<DamageableComponent>(uid)
                    && !HasComp<WFLatticeAnchorComponent>(uid)
                    && !HasComp<MobStateComponent>(uid))
                    EnsureComp<WFLatticeStationPartComponent>(uid);

                // The hardpoints and their lances can't be unbolted and carried off.
                if (HasComp<AnchorableComponent>(uid)
                    && (HasComp<HardpointComponent>(uid) || HasComp<WFLatticeDefenceWeaponComponent>(uid)))
                    RemCompDeferred<AnchorableComponent>(uid);
            }
        }
    }

    /// <summary>Announces something about a station to the sector.</summary>
    private void Alert(EntityUid station, WFLatticeAnchorStationComponent comp, string key, EntityUid? enemy, bool flash)
    {
        var anchor = Exists(comp.Core) ? Name(comp.Core) : Name(station);
        var attacker = enemy is { } ship && Exists(ship) ? Name(ship) : Loc.GetString("wf-lattice-defence-unknown");
        var text = Loc.GetString(key, ("anchor", anchor), ("attacker", attacker));
        _chat.DispatchGlobalAnnouncement(text, Loc.GetString("wf-lattice-chip-sender"), true, BreachSound, Color.FromHex("#d030a0"));
        RaiseNetworkEvent(new WFAnnouncementChipEvent
        {
            Level = flash ? WFAnnounceChipLevel.Warning : WFAnnounceChipLevel.Advisory,
            Accent = Color.FromHex("#d030a0"),
            Title = Loc.GetString("wf-lattice-defence-title"),
            Text = text,
            Sender = Loc.GetString("wf-lattice-chip-sender"),
            Duration = 20f,
            Flash = flash,
        });
    }

    /// <summary>Whether a station's shield is down: no emitter, or no field standing.</summary>
    public bool ShieldDown(EntityUid station)
    {
        var shields = EntityQueryEnumerator<WFLatticeAnchorShieldComponent, ShipShieldEmitterComponent, TransformComponent>();
        while (shields.MoveNext(out _, out _, out var emitter, out var xform))
        {
            if (xform.GridUid != station)
                continue;

            if (emitter.Shield is { } field
                && !TerminatingOrDeleted(field)
                && (!TryComp<PhysicsComponent>(field, out var body) || body.CanCollide))
                return false;
        }

        return true;
    }

    /// <summary>Whether a ship still has anyone alive aboard to fight back: a player or an NPC crewman.</summary>
    private bool Crewed(EntityUid grid)
    {
        // A person on foot is their own crew.
        if (TryComp<MobStateComponent>(grid, out var self))
            return self.CurrentState != MobState.Dead;

        var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (mobs.MoveNext(out var uid, out var state, out var xform))
        {
            if (xform.GridUid == grid
                && state.CurrentState != MobState.Dead
                && (HasComp<ActorComponent>(uid) || HasComp<ActiveNPCComponent>(uid)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Every quarter second: forgets quiet attackers, and if a station's shield is down, engages the nearest attacker
    /// still crewed and in reach. The first pass with the shield down alerts and calls the drones.
    /// </summary>
    private void UpdateDefence(float frameTime)
    {
        _defenceAccumulator += frameTime;
        if (_defenceAccumulator < DefenceInterval)
            return;

        _defenceAccumulator = 0f;
        var now = _timing.CurTime;
        MarkParts(now);
        var stations = EntityQueryEnumerator<WFLatticeAnchorStationComponent, TransformComponent>();
        while (stations.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Hostiles.Count == 0)
            {
                comp.Engaged = false;
                continue;
            }

            _expired.Clear();
            foreach (var (enemy, until) in comp.Hostiles)
            {
                if (now >= until || TerminatingOrDeleted(enemy))
                    _expired.Add(enemy);
            }

            foreach (var enemy in _expired)
            {
                comp.Hostiles.Remove(enemy);
                comp.Strikes.Remove(enemy);
            }

            if (comp.Hostiles.Count == 0 || !TryGetProfile(uid, out var profile))
                continue;

            // The shield holding means the station is not yet in danger, unless something struck it directly.
            if (now >= comp.ProvokedUntil && !ShieldDown(uid))
            {
                comp.Engaged = false;
                continue;
            }

            var from = _transform.GetWorldPosition(xform);
            EntityUid? target = null;
            var best = profile.DefenceRange;
            foreach (var enemy in comp.Hostiles.Keys)
            {
                var enemyXform = Transform(enemy);
                if (enemyXform.MapUid != xform.MapUid)
                    continue;

                var distance = Vector2.Distance(from, _transform.GetWorldPosition(enemyXform));
                if (distance > best || !Crewed(enemy))
                    continue;

                best = distance;
                target = enemy;
            }

            if (target is not { } aim)
                continue;

            if (!comp.Engaged)
            {
                comp.Engaged = true;
                Alert(uid, comp, "wf-lattice-defence", aim, true);
                Defend(uid, aim);
            }

            FireLances(uid, aim);
        }
    }

    /// <summary>Fires every lance on a station at a grid, leading it by its speed.</summary>
    private void FireLances(EntityUid station, EntityUid target)
    {
        var targetXform = Transform(target);
        var targetPos = _transform.GetWorldPosition(targetXform);
        var velocity = TryComp<PhysicsComponent>(target, out var body) ? body.LinearVelocity : Vector2.Zero;
        var lances = EntityQueryEnumerator<WFLatticeDefenceWeaponComponent, TransformComponent>();
        while (lances.MoveNext(out var lance, out _, out var xform))
        {
            if (xform.GridUid != station || !xform.Anchored)
                continue;

            var from = _transform.GetWorldPosition(xform);
            var speed = TryComp<GunComponent>(lance, out var gun) ? MathF.Max(gun.ProjectileSpeedModified, 1f) : 500f;
            var lead = targetPos + velocity * (Vector2.Distance(from, targetPos) / speed);
            _fireControl.AttemptFire(lance, lance, _transform.ToCoordinates(new Robust.Shared.Map.MapCoordinates(lead, xform.MapID)), noServer: true);
        }
    }

    private bool TryGetProfile(EntityUid station, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WFLatticeProfilePrototype? profile)
    {
        profile = null;
        return Transform(station).MapUid is { } map
               && TryComp<WFLatticeFieldComponent>(map, out var field)
               && _proto.TryIndex(field.Profile, out profile);
    }
}
