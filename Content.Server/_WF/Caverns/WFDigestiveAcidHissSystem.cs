using Content.Shared._WF.Caverns;
using Content.Shared.Damage.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Caverns;

/// <summary>
/// Loops a pool's burn sound on whoever digestive acid is burning, and stops it once they leave the acid, die or stop
/// taking contact damage. Spared natives and catwalk walkers are never burned, so they never hiss.
/// </summary>
public sealed partial class WFDigestiveAcidHissSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private WFDigestiveAcidSystem _acid = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(0.25);

    private readonly List<EntityUid> _changed = new();
    private TimeSpan _nextCheck;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFDigestiveAcidHissComponent, ComponentShutdown>(OnShutdown);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + Interval;

        var hissing = EntityQueryEnumerator<WFDigestiveAcidHissComponent>();
        while (hissing.MoveNext(out var uid, out _))
        {
            if (BurnSound(uid) == null)
                _changed.Add(uid);
        }

        foreach (var uid in _changed)
        {
            RemComp<WFDigestiveAcidHissComponent>(uid);
        }

        _changed.Clear();

        var burning = EntityQueryEnumerator<DamagedByContactComponent>();
        while (burning.MoveNext(out var uid, out _))
        {
            if (!HasComp<WFDigestiveAcidHissComponent>(uid))
                _changed.Add(uid);
        }

        foreach (var uid in _changed)
        {
            if (BurnSound(uid) is not { } sound)
                continue;

            var hiss = AddComp<WFDigestiveAcidHissComponent>(uid);
            hiss.Stream = _audio.PlayPvs(sound, uid, sound.Params.WithLoop(true))?.Entity;
        }

        _changed.Clear();
    }

    /// <summary>The burn sound of a pool digesting this entity right now; null when no pool is, or it is dead.</summary>
    private SoundSpecifier? BurnSound(EntityUid uid)
    {
        if (!HasComp<DamagedByContactComponent>(uid)
            || _mobState.IsDead(uid)
            || !TryComp<PhysicsComponent>(uid, out var body))
            return null;

        foreach (var contact in _physics.GetContactingEntities(uid, body))
        {
            if (TryComp<WFDigestiveAcidComponent>(contact, out var pool)
                && pool.BurnSound != null
                && _acid.Digests((contact, pool), uid))
                return pool.BurnSound;
        }

        return null;
    }

    private void OnShutdown(Entity<WFDigestiveAcidHissComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent.Comp.Stream))
            _audio.Stop(ent.Comp.Stream);
    }
}
