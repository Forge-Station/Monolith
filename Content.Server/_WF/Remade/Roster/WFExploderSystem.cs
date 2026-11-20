using Content.Server._WF.Remade.Limbs;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Roster;

/// <summary>Sets off an exploder's blast: when it chooses to, or when its pustule is shot. The mob dies in its own blast.</summary>
public sealed partial class WFExploderSystem : EntitySystem
{
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFRemadeLimbsSystem _limbs = default!;

    private readonly List<EntityUid> _pending = new();
    private readonly List<EntityUid> _running = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFRemadeExploderComponent, DamageModifyEvent>(OnModify);
        SubscribeLocalEvent<WFRemadeExploderComponent, WFRemadeLimbSeveredEvent>(OnSevered);
    }

    /// <summary>Notes where the hit came from, which comes before it finds its limb.</summary>
    private void OnModify(Entity<WFRemadeExploderComponent> ent, ref DamageModifyEvent args)
    {
        ent.Comp.LastHitAt = _timing.CurTime;
        ent.Comp.LastHitShot = args.Origin is not { } origin
                               || TerminatingOrDeleted(origin)
                               || (_transform.GetWorldPosition(origin) - _transform.GetWorldPosition(ent)).Length() >= ent.Comp.ShotDistance;
    }

    /// <summary>A pustule shot off bursts where it stands. One cut off up close just comes away.</summary>
    private void OnSevered(EntityUid uid, WFRemadeExploderComponent comp, WFRemadeLimbSeveredEvent args)
    {
        if (args.Limb != comp.Pustule || comp.Detonated || comp.LastHitAt != _timing.CurTime || !comp.LastHitShot)
            return;

        // Not here: this runs inside the hit that severed it.
        _pending.Add(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_pending.Count == 0)
            return;

        _running.Clear();
        _running.AddRange(_pending);
        _pending.Clear();
        foreach (var uid in _running)
        {
            Detonate(uid);
        }
    }

    /// <summary>Whether this exploder has already gone off.</summary>
    public bool HasDetonated(EntityUid uid)
    {
        return TryComp<WFRemadeExploderComponent>(uid, out var comp) && comp.Detonated;
    }

    /// <summary>Blows the exploder up where it stands and kills it. Once only, and safe on one that's dead or already gone.</summary>
    public void Detonate(EntityUid uid)
    {
        // A dead one has nothing left to burst.
        if (TerminatingOrDeleted(uid) || !TryComp<WFRemadeExploderComponent>(uid, out var comp) || comp.Detonated || _mobState.IsDead(uid))
            return;

        comp.Detonated = true;
        var at = _transform.GetMapCoordinates(uid);
        _audio.PlayPvs(comp.BurstSound, uid);
        _popup.PopupEntity(Loc.GetString("wf-remade-pustule-burst", ("mob", uid)), uid, PopupType.LargeCaution);
        _explosion.QueueExplosion(at, comp.ExplosionType, comp.Intensity, comp.Slope, comp.MaxIntensity, uid,
            tileBreakScale: 0f, maxTileBreak: 0, canCreateVacuum: false);

        _limbs.TrySever(uid, comp.Pustule, uid);
        if (!TerminatingOrDeleted(uid) && !_mobState.IsDead(uid))
            _mobState.ChangeMobState(uid, MobState.Dead, origin: uid);
    }
}
