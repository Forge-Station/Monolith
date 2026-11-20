using System.Numerics;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.CombatConsole;

/// <summary>A live countermeasure fired by a connected Sunny launcher.</summary>
[RegisterComponent]
public sealed partial class WFFlareDecoyComponent : Component
{
    public EntityUid ProtectedGrid;
}

public sealed partial class WFCombatConsoleSystem
{
    private void OnFlareProjectiles(EntityUid uid, WFFlareLauncherComponent launcher, AmmoShotEvent args)
    {
        if (Transform(uid).GridUid is not { } grid)
            return;
        foreach (var projectile in args.FiredProjectiles)
        {
            if (TerminatingOrDeleted(projectile) || !HasComp<PhysicsComponent>(projectile))
                continue;
            EnsureComp<WFFlareDecoyComponent>(projectile).ProtectedGrid = grid;
        }
    }

    /// <summary>Lets an escaped flare distract an existing hostile lock using the seeker's normal range and field of view.</summary>
    private void UpdateFlareDecoys()
    {
        var decoys = new List<(EntityUid Uid, EntityUid Grid, TransformComponent Transform, Vector2 Position)>();
        var flares = EntityQueryEnumerator<WFFlareDecoyComponent, TransformComponent, PhysicsComponent>();
        while (flares.MoveNext(out var uid, out var flare, out var xform, out _))
        {
            if (xform.GridUid != null || TerminatingOrDeleted(flare.ProtectedGrid) ||
                TryComp<ProjectileComponent>(uid, out var projectile) && projectile.ProjectileSpent)
                continue;
            decoys.Add((uid, flare.ProtectedGrid, xform, _transform.GetWorldPosition(xform)));
        }
        if (decoys.Count == 0)
            return;
        var targeting = EntityManager.System<TargetSeekingSystem>();
        var seekers = EntityQueryEnumerator<TargetSeekingComponent, ProjectileComponent, TransformComponent>();
        while (seekers.MoveNext(out var uid, out var seeker, out var projectile, out var xform))
        {
            if (projectile.ProjectileSpent || !seeker.Launched || seeker.SeekingDisabled || seeker.TrackDelay > 0 ||
                seeker.CurrentTarget is not { } target || TerminatingOrDeleted(target))
                continue;
            var targetXform = Transform(target);
            if (targetXform.MapID != xform.MapID)
                continue;
            var protectedGrid = targetXform.GridUid ?? target;
            if (projectile.Shooter is { } shooter && TryComp(shooter, out TransformComponent? shooterXform) &&
                shooterXform.GridUid == protectedGrid)
                continue;
            var origin = _transform.GetWorldPosition(xform);
            var closest = Vector2.DistanceSquared(origin, _transform.GetWorldPosition(targetXform));
            closest = Math.Min(closest, seeker.DetectionRange * seeker.DetectionRange);
            EntityUid? selected = null;
            foreach (var decoy in decoys)
            {
                if (decoy.Grid != protectedGrid || decoy.Transform.MapID != xform.MapID)
                    continue;
                var distance = Vector2.DistanceSquared(origin, decoy.Position);
                if (distance >= closest || Math.Abs(Angle.ShortestDistance(_transform.GetWorldRotation(xform),
                        (decoy.Position - origin).ToWorldAngle()).Degrees) > seeker.ScanArc / 2)
                    continue;
                closest = distance;
                selected = decoy.Uid;
            }
            if (selected != null)
                targeting.SetSeekerTarget((uid, seeker), selected, xform);
        }
    }
}
