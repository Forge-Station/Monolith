using System.Numerics;
using Content.Server._WF.Remade.Limbs;
using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Content.Shared.Throwing;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Elites;

/// <summary>Drags a downed mender's severed limbs back onto it, and keeps it down until it is whole or has lost one for good.</summary>
public sealed class WFReclaimSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFRemadeLimbsSystem _limbs = default!;
    [Dependency] private WFRisesSystem _rises = default!;

    private static readonly TimeSpan DragSoundGap = TimeSpan.FromSeconds(2.2);

    private readonly Dictionary<EntityUid, List<Entity<WFSeveredLimbComponent>>> _byOwner = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFReclaimComponent, WFRiseAttemptEvent>(OnRiseAttempt);
        SubscribeLocalEvent<WFReclaimComponent, WFRemadeLimbSeveredEvent>(OnSevered);
    }

    /// <summary>A limb torn off a mender flies, so it lies out where the tentacles have to fetch it.</summary>
    private void OnSevered(Entity<WFReclaimComponent> ent, ref WFRemadeLimbSeveredEvent args)
    {
        if (args.Item is not { } item)
            return;

        var direction = _random.NextAngle().ToVec() * _random.NextFloat(ent.Comp.FlingMin, ent.Comp.FlingMax);
        _throwing.TryThrow(item, direction, 6f, recoil: false, doSpin: true);
    }

    /// <summary>It gets up only once every limb is back on.</summary>
    private void OnRiseAttempt(Entity<WFReclaimComponent> ent, ref WFRiseAttemptEvent args)
    {
        if (TryComp<WFRemadeLimbsComponent>(ent, out var limbs) && limbs.Severed.Count > 0)
            args.Cancelled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _byOwner.Clear();
        var severed = EntityQueryEnumerator<WFSeveredLimbComponent>();
        while (severed.MoveNext(out var uid, out var limb))
        {
            if (limb.Source is not { } owner || TerminatingOrDeleted(uid))
                continue;

            if (!_byOwner.TryGetValue(owner, out var list))
                _byOwner[owner] = list = new List<Entity<WFSeveredLimbComponent>>();

            list.Add((uid, limb));
        }

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFReclaimComponent, WFRisesComponent, WFRemadeLimbsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var reclaim, out var rises, out var limbs, out var xform))
        {
            var items = _byOwner.GetValueOrDefault(uid);
            if (rises.Final || rises.RiseAt == null)
            {
                Release(items);
                reclaim.Reaching = false;
                reclaim.ReachAt = null;
                reclaim.OutOfReach.Clear();
                continue;
            }

            reclaim.ReachAt ??= now + TimeSpan.FromSeconds(reclaim.ReachDelay);
            if (now < reclaim.ReachAt)
                continue;

            Reclaim((uid, reclaim, rises, limbs, xform), items, frameTime, now);
        }
    }

    private void Reclaim(Entity<WFReclaimComponent, WFRisesComponent, WFRemadeLimbsComponent, TransformComponent> ent,
        List<Entity<WFSeveredLimbComponent>>? items, float frameTime, TimeSpan now)
    {
        var (uid, reclaim, rises, limbs, xform) = ent;
        if (limbs.Severed.Count == 0)
            return;

        if (!reclaim.Reaching)
        {
            reclaim.Reaching = true;
            _popup.PopupEntity(Loc.GetString("wf-remade-mender-reaches", ("mob", uid)), uid, PopupType.LargeCaution);
        }

        var here = _transform.GetWorldPosition(xform);
        var dragging = false;
        foreach (var id in new List<string>(limbs.Severed))
        {
            var index = items?.FindIndex(limb => limb.Comp.Limb == id) ?? -1;
            if (index < 0 || TerminatingOrDeleted(items![index]))
            {
                Lose((uid, rises));
                return;
            }

            var limb = items[index];
            var limbXform = Transform(limb);
            var away = _transform.GetWorldPosition(limbXform) - here;
            if (_container.IsEntityInContainer(limb) || limbXform.MapID != xform.MapID || away.Length() > reclaim.Reach)
            {
                RemCompDeferred<WFReclaimTentacleComponent>(limb);
                if (!reclaim.OutOfReach.TryGetValue(id, out var since))
                    reclaim.OutOfReach[id] = now;
                else if ((now - since).TotalSeconds >= reclaim.LostAfter)
                {
                    Lose((uid, rises));
                    return;
                }

                continue;
            }

            reclaim.OutOfReach.Remove(id);
            var tentacle = EnsureComp<WFReclaimTentacleComponent>(limb);
            if (tentacle.Puller != uid)
            {
                tentacle.Puller = uid;
                Dirty(limb, tentacle);
            }

            var distance = away.Length();
            if (distance <= reclaim.Grab)
            {
                Reattach((uid, reclaim), limbs, limb, id);
                continue;
            }

            var step = Math.Min(distance - reclaim.Grab * 0.5f, reclaim.PullSpeed * frameTime);
            _physics.SetLinearVelocity(limb, Vector2.Zero);
            _transform.SetWorldPosition(limb, here + away - Vector2.Normalize(away) * step);
            dragging = true;
        }

        if (dragging && now >= reclaim.NextDragSound)
        {
            reclaim.NextDragSound = now + DragSoundGap;
            _audio.PlayPvs(reclaim.DragSound, uid, AudioParams.Default.WithVolume(-6f).WithMaxDistance(8f));
        }

        if (limbs.Severed.Count == 0)
            _rises.Rise((uid, rises), now);
    }

    private void Reattach(Entity<WFReclaimComponent> ent, WFRemadeLimbsComponent limbs, EntityUid limb, string id)
    {
        if (!_limbs.Regrow(ent, id, limbs))
            return;

        ent.Comp.Reattached++;
        QueueDel(limb);
        _audio.PlayPvs(ent.Comp.AttachSound, ent, AudioParams.Default.WithVolume(-3f));
        _popup.PopupEntity(Loc.GetString("wf-remade-mender-reattaches", ("mob", ent.Owner)), ent, PopupType.MediumCaution);
    }

    /// <summary>A limb is gone for good: it can never be whole again, so it stays down.</summary>
    private void Lose(Entity<WFRisesComponent> ent)
    {
        _rises.EndForGood(ent, "wf-remade-mender-gives-up");
        Release(_byOwner.GetValueOrDefault(ent));
    }

    private void Release(List<Entity<WFSeveredLimbComponent>>? items)
    {
        if (items == null)
            return;

        foreach (var item in items)
        {
            RemCompDeferred<WFReclaimTentacleComponent>(item);
        }
    }
}
