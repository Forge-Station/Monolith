using System.Numerics;
using Content.Server._WF.Remade.Conversion;
using Content.Shared._WF.Remade.Structures;
using Content.Shared.Examine;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Structures;

/// <summary>A cyst hangs on a wall and spits a bio-bomb at anyone crossing its arc in sight, then takes a while to reload.</summary>
public sealed partial class WFCystSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFGrowthSystem _growth = default!;

    private static readonly TimeSpan ScanRate = TimeSpan.FromSeconds(0.4);
    private static readonly TimeSpan FirstLoad = TimeSpan.FromSeconds(8);

    private readonly HashSet<Entity<MobStateComponent>> _seen = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCystComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<WFCystComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.ReloadAt = _timing.CurTime + FirstLoad;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFCystComponent, WFGrowthComponent>();
        while (query.MoveNext(out var uid, out var cyst, out var growth))
        {
            if (now < cyst.NextScan)
                continue;

            cyst.NextScan = now + ScanRate;
            if (!growth.Active)
            {
                cyst.Ready = false;
                continue;
            }

            if (!cyst.Ready)
            {
                if (now < cyst.ReloadAt)
                    continue;

                cyst.Ready = true;
                _growth.SetState((uid, growth), WFGrowthState.Active);
            }

            if (FindTarget((uid, cyst)) is { } target)
                Fire((uid, cyst, growth), target, now);
        }
    }

    /// <summary>The nearest living, non-Remade mob in its arc that it can see.</summary>
    private EntityUid? FindTarget(Entity<WFCystComponent> ent)
    {
        _seen.Clear();
        var coordinates = Transform(ent).Coordinates;
        _lookup.GetEntitiesInRange(coordinates, ent.Comp.Range, _seen);

        var origin = _transform.GetWorldPosition(ent);
        var facing = _transform.GetWorldRotation(ent).ToWorldVec();
        var minDot = MathF.Cos(MathHelper.DegreesToRadians(ent.Comp.Arc / 2f));
        EntityUid? best = null;
        var bestDistance = float.MaxValue;

        foreach (var mob in _seen)
        {
            if (HasComp<WFRemadeComponent>(mob) || _mobState.IsDead(mob))
                continue;

            var offset = _transform.GetWorldPosition(mob) - origin;
            var distance = offset.Length();
            if (distance < 0.1f || Vector2.Dot(offset / distance, facing) < minDot || distance >= bestDistance)
                continue;

            if (!_examine.InRangeUnOccluded(ent, mob.Owner, ent.Comp.Range, null))
                continue;

            best = mob;
            bestDistance = distance;
        }

        return best;
    }

    private void Fire(Entity<WFCystComponent, WFGrowthComponent> ent, EntityUid target, TimeSpan now)
    {
        var cyst = ent.Comp1;
        var direction = _transform.GetWorldPosition(target) - _transform.GetWorldPosition(ent);
        var bomb = Spawn(cyst.Bomb, Transform(ent).Coordinates.Offset(Vector2.Normalize(direction) * 0.5f));
        _gun.ShootProjectile(bomb, direction, Vector2.Zero, ent, null, cyst.BombSpeed);

        _audio.PlayPvs(cyst.FireSound, ent);
        cyst.Ready = false;
        cyst.ReloadAt = now + cyst.Reload;
        cyst.Shots++;
        _growth.SetState((ent.Owner, ent.Comp2), WFGrowthState.Idle);
    }
}
