using Content.Shared._WF.RMCCore.Hands;
using Content.Shared._WF.RMCCore.Pulling;
using Content.Shared._WF.Xenonids.Parasite;
using Content.Shared.Administration.Logs;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Effects;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._WF.RMCCore.Tackle;

public sealed partial class TackleSystem : EntitySystem
{
    [Dependency] private ISharedAdminLogManager _adminLog = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedColorFlashEffectSystem _colorFlash = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private RMCPullingSystem _rmcPulling = default!;
    [Dependency] private RMCHandsSystem _rmcHands = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedGunSystem _gunSystem = default!;

    private readonly List<EntityUid> _trackersToRemove = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<TackleableComponent, CMDisarmEvent>(OnDisarmed, before: [typeof(SharedHandsSystem), typeof(StaminaSystem)]);
        // WOLFGATE: RMC's skill-based marine disarm (RMCDisarm/RMCDisarmable) is cut with Skills

        SubscribeLocalEvent<TackledRecentlyByComponent, ComponentRemove>(OnByRemove);
        SubscribeLocalEvent<TackledRecentlyByComponent, EntityTerminatingEvent>(OnByRemove);
        SubscribeLocalEvent<TackledRecentlyByComponent, DownedEvent>(OnDowned);

        SubscribeLocalEvent<TackledRecentlyComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<TackledRecentlyComponent, EntityTerminatingEvent>(OnRemove);
    }

    private void OnDisarmed(Entity<TackleableComponent> target, ref CMDisarmEvent args)
    {
        var user = args.User;
        if (!TryComp(user, out TackleComponent? tackle))
            return;

        args.Handled = true;

        DoDisarmEffects(user, target);

        var time = _timing.CurTime;
        var recently = EnsureComp<TackledRecentlyComponent>(user);
        var tracker = recently.Trackers.GetValueOrDefault(target);
        tracker.Count++;
        tracker.Last = time;

        recently.Trackers[target] = tracker;
        Dirty(user, recently);

        var tackledBy = EnsureComp<TackledRecentlyByComponent>(target);
        tackledBy.Tacklers.Add(user);
        Dirty(target, tackledBy);

        if (_net.IsClient)
            return;

        var random = _random.NextFloat(0, 1);

        if ((tracker.Count < tackle.Min || tackle.Chance < random) &&
            tracker.Count < tackle.Max)
        {
            // WOLFGATE: LogType.DisarmedKnockdown doesn't exist here; disarm log types stand in
            _adminLog.Add(LogType.DisarmedAction, $"{ToPrettyString(user)} tried to tackle {ToPrettyString(target)}.");

            var selfPopup = Loc.GetString("cm-tackle-try-self", ("target", Identity.Name(target, EntityManager, user)));
            var targetPopup = Loc.GetString("cm-tackle-try-target", ("user", Identity.Name(user, EntityManager, target)));
            DoPvsPopups(user,
                target,
                selfPopup,
                targetPopup,
                other => Loc.GetString("cm-tackle-try-observer",
                    ("user", Identity.Name(user, EntityManager, other)),
                    ("target", Identity.Name(target, EntityManager, other)))
            );

            return;
        }
        else
        {
            _adminLog.Add(LogType.DisarmedKnockdown, $"{ToPrettyString(user)} tackled down {ToPrettyString(target)}.");

            var selfPopup = Loc.GetString("cm-tackle-success-self", ("target", Identity.Name(target, EntityManager, user)));
            var targetPopup = Loc.GetString("cm-tackle-success-target", ("user", Identity.Name(user, EntityManager, target)));
            DoPvsPopups(user,
                target,
                selfPopup,
                targetPopup,
                other => Loc.GetString("cm-tackle-success-observer",
                    ("user", Identity.Name(user, EntityManager, other)),
                    ("target", Identity.Name(target, EntityManager, other)))
            );
        }

        _audio.PlayPvs(target.Comp.KnockdownSound, target);

        if (!HasComp<VictimInfectedComponent>(target))
        {
            recently.Trackers.Remove(target);
            RemoveTackledBy(target.Owner, user);
        }

        var stun = tackle.StunMin;
        if (tackle.StunMin < tackle.StunMax)
            stun = _random.Next(tackle.StunMin, tackle.StunMax);

        stun *= 2;
        _stun.TryParalyze(target, stun, true);
    }

    private void DoDisarmEffects(EntityUid user, EntityUid target)
    {
        _colorFlash.RaiseEffect(Color.Aqua, new List<EntityUid> { target }, Filter.PvsExcept(user));
    }

    private void DoPvsPopups(EntityUid user, EntityUid target, string selfPopup, string targetPopup, Func<EntityUid, string> othersPopup, PopupType selfPopupType = PopupType.Small)
    {
        _popup.PopupEntity(selfPopup, user, user, selfPopupType);

        foreach (var session in Filter.PvsExcept(user).Recipients)
        {
            if (session.AttachedEntity is not { } recipient)
                continue;

            if (recipient == target)
                _popup.PopupEntity(targetPopup, user, recipient, PopupType.MediumCaution);
            else
                _popup.PopupEntity(othersPopup(recipient), user, recipient, PopupType.SmallCaution);
        }
    }

    private void OnByRemove<T>(Entity<TackledRecentlyByComponent> ent, ref T args)
    {
        foreach (var tackler in ent.Comp.Tacklers)
        {
            if (!TryComp(tackler, out TackledRecentlyComponent? recently))
                continue;

            recently.Trackers.Remove(ent);
            Dirty(tackler, recently);
        }
    }

    private void OnDowned(Entity<TackledRecentlyByComponent> ent, ref DownedEvent args)
    {
        if (HasComp<VictimInfectedComponent>(ent))
            return;

        if (TryComp<BuckleComponent>(ent, out var buckle) && buckle.Buckled)
            return;

        // WOLFGATE: Wolfgate's DownedEvent doesn't say who downed the entity, so RMC's self-down check is skipped

        RemCompDeferred<TackledRecentlyByComponent>(ent);
    }

    private void OnRemove<T>(Entity<TackledRecentlyComponent> ent, ref T args)
    {
        foreach (var tracker in ent.Comp.Trackers)
        {
            if (!TryComp(tracker.Key, out TackledRecentlyByComponent? tackled))
                continue;

            tackled.Tacklers.Remove(ent);
        }
    }

    private void RemoveTackledBy(Entity<TackledRecentlyByComponent?> by, EntityUid tackler)
    {
        if (!Resolve(by, ref by.Comp, false))
            return;

        by.Comp.Tacklers.Remove(tackler);
        Dirty(by);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var time = _timing.CurTime;
        var query = EntityQueryEnumerator<TackledRecentlyComponent>();
        while (query.MoveNext(out var tackler, out var recently))
        {
            _trackersToRemove.Clear();
            foreach (var tracker in recently.Trackers)
            {
                if (time >= tracker.Value.Last + recently.ExpireAfter)
                    _trackersToRemove.Add(tracker.Key);
            }

            foreach (var id in _trackersToRemove)
            {
                recently.Trackers.Remove(id);
                RemoveTackledBy(id, tackler);
            }

            if (recently.Trackers.Count == 0)
                RemCompDeferred<TackledRecentlyComponent>(tackler);
        }
    }
}
