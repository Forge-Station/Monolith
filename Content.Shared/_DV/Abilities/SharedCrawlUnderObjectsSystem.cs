using System.Numerics;
using Content.Shared.Climbing.Events;
using Content.Shared.IdentityManagement;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._DV.Abilities;

/// <summary>
/// Sneaking slows the mob down and shrinks its circle fixtures so it can squeeze past mobs and furniture.
/// Walking through tables stays blocked; once it has climbed onto one it is drawn underneath it.
/// Runs predicted on the client; the server stays authoritative.
/// </summary>
// WOLFGATE: rewritten as a shared, predicted system with HardLight's squeeze geometry. The Delta-V original was
// server-only and stripped table bits from the collision mask instead.
public abstract partial class SharedCrawlUnderObjectsSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private MovementSpeedModifierSystem _movespeed = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CrawlUnderObjectsComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<FixturesComponent, ComponentStartup>(OnFixturesStartup);
        SubscribeLocalEvent<CrawlUnderObjectsComponent, ToggleCrawlingStateEvent>(OnToggle);
        SubscribeLocalEvent<CrawlUnderObjectsComponent, AttemptClimbEvent>(OnAttemptClimb);
        SubscribeLocalEvent<CrawlUnderObjectsComponent, StandAttemptEvent>(OnStandAttempt);
        SubscribeLocalEvent<CrawlUnderObjectsComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovespeed);
        SubscribeLocalEvent<CrawlUnderObjectsComponent, DownedEvent>(OnDowned);
        SubscribeLocalEvent<CrawlUnderObjectsComponent, StoodEvent>(OnStood);
    }

    private void OnStartup(Entity<CrawlUnderObjectsComponent> ent, ref ComponentStartup args)
    {
        EnsureBaselineInflation(ent);
    }

    // Fixtures may start after the crawl component does; try the baseline again once they exist.
    private void OnFixturesStartup(Entity<FixturesComponent> ent, ref ComponentStartup args)
    {
        if (TryComp<CrawlUnderObjectsComponent>(ent, out var crawl))
            EnsureBaselineInflation((ent.Owner, crawl));
    }

    private void OnToggle(Entity<CrawlUnderObjectsComponent> ent, ref ToggleCrawlingStateEvent args)
    {
        if (args.Handled)
            return;

        // Re-predicted inputs start from the server state, which already includes the toggle.
        if (_net.IsClient && !_timing.IsFirstTimePredicted)
            return;

        args.Handled = TrySetEnabled(ent, !ent.Comp.Enabled);
    }

    private void OnAttemptClimb(Entity<CrawlUnderObjectsComponent> ent, ref AttemptClimbEvent args)
    {
        if (ent.Comp.Enabled)
            args.Cancelled = true;
    }

    private void OnStandAttempt(Entity<CrawlUnderObjectsComponent> ent, ref StandAttemptEvent args)
    {
        if (ent.Comp.Enabled)
            args.Cancel();
    }

    private void OnRefreshMovespeed(Entity<CrawlUnderObjectsComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Enabled && !_standing.IsDown(ent))
            args.ModifySpeed(ent.Comp.SneakSpeedModifier, ent.Comp.SneakSpeedModifier);
    }

    /// <summary>
    /// Going down ends sneaking; the downed body gets the squeeze scale of its own until it stands again.
    /// </summary>
    private void OnDowned(Entity<CrawlUnderObjectsComponent> ent, ref DownedEvent args)
    {
        if (ent.Comp.Enabled)
            SetEnabled(ent, false, popup: false);

        if (ent.Comp.DownedScaleApplied || !TryComp<FixturesComponent>(ent, out var fixtures))
            return;

        var scale = GetSqueezeScale(ent.Comp);
        if (MathHelper.CloseTo(scale, 1f))
            return;

        CaptureCircles((ent.Owner, fixtures), ent.Comp.DownedCircles);
        ApplyCircles((ent.Owner, fixtures), ent.Comp.DownedCircles, scale);
        ent.Comp.DownedScaleApplied = true;
    }

    private void OnStood(Entity<CrawlUnderObjectsComponent> ent, ref StoodEvent args)
    {
        if (!ent.Comp.DownedScaleApplied || !TryComp<FixturesComponent>(ent, out var fixtures))
            return;

        RestoreCircles((ent.Owner, fixtures), ent.Comp.DownedCircles);
        ent.Comp.DownedCircles.Clear();
        ent.Comp.DownedScaleApplied = false;
    }

    /// <summary>
    /// Tries to start or stop sneaking. Starting fails while downed; stopping is always allowed so
    /// nobody gets stuck squeezed.
    /// </summary>
    public bool TrySetEnabled(Entity<CrawlUnderObjectsComponent> ent, bool enabled)
    {
        if (ent.Comp.Enabled == enabled)
            return false;

        if (enabled)
        {
            EnsureBaselineInflation(ent);

            if (_standing.IsDown(ent))
                return false;
        }

        SetEnabled(ent, enabled, popup: true);
        return true;
    }

    private void SetEnabled(Entity<CrawlUnderObjectsComponent> ent, bool enabled, bool popup)
    {
        ent.Comp.Enabled = enabled;

        // Geometry first, so movement prediction uses the new hitbox in the same tick.
        if (TryComp<FixturesComponent>(ent, out var fixtures))
        {
            var scale = GetSqueezeScale(ent.Comp);
            var modify = !MathHelper.CloseTo(scale, 1f);

            if (enabled && modify)
            {
                CaptureCircles((ent.Owner, fixtures), ent.Comp.ChangedCircles);
                ApplyCircles((ent.Owner, fixtures), ent.Comp.ChangedCircles, scale);
            }
            else if (!enabled && modify)
            {
                RestoreCircles((ent.Owner, fixtures), ent.Comp.ChangedCircles);
                ent.Comp.ChangedCircles.Clear();
            }
            else
            {
                ent.Comp.ChangedCircles.Clear();
            }
        }

        _movespeed.RefreshMovementSpeedModifiers(ent);
        _appearance.SetData(ent, SneakMode.Enabled, enabled);
        Dirty(ent);

        if (!popup)
            return;

        var key = enabled ? "crawl-under-objects-toggle-on" : "crawl-under-objects-toggle-off";
        _popup.PopupPredicted(Loc.GetString(key),
            Loc.GetString(key + "-other", ("person", Identity.Entity(ent, EntityManager))),
            ent,
            ent);
    }

    /// <summary>
    /// Applies <see cref="CrawlUnderObjectsComponent.UnsqueezedRadiusScale"/> once, before any squeeze.
    /// </summary>
    private void EnsureBaselineInflation(Entity<CrawlUnderObjectsComponent> ent)
    {
        if (ent.Comp.BaselineInflationApplied
            || ent.Comp.Enabled
            || MathHelper.CloseTo(ent.Comp.UnsqueezedRadiusScale, 1f))
            return;

        if (!TryComp<FixturesComponent>(ent, out var fixtures))
            return;

        foreach (var (key, fixture) in fixtures.Fixtures)
        {
            if (fixture.Shape is not PhysShapeCircle circle)
                continue;

            _physics.SetPositionRadius(ent, key, fixture, circle, circle.Position, circle.Radius * ent.Comp.UnsqueezedRadiusScale, fixtures);
        }

        ent.Comp.BaselineInflationApplied = true;
    }

    private static float GetSqueezeScale(CrawlUnderObjectsComponent comp)
    {
        if (MathHelper.CloseTo(comp.UnsqueezedRadiusScale, 0f))
            return comp.SqueezeRadiusScale;

        return comp.SqueezeRadiusScale / comp.UnsqueezedRadiusScale;
    }

    private static void CaptureCircles(Entity<FixturesComponent> ent, List<(string key, Vector2 position, float radius)> output)
    {
        output.Clear();
        foreach (var (key, fixture) in ent.Comp.Fixtures)
        {
            if (fixture.Shape is PhysShapeCircle circle)
                output.Add((key, circle.Position, circle.Radius));
        }
    }

    private void ApplyCircles(Entity<FixturesComponent> ent, List<(string key, Vector2 position, float radius)> circles, float scale)
    {
        foreach (var (key, position, radius) in circles)
        {
            if (ent.Comp.Fixtures.TryGetValue(key, out var fixture) && fixture.Shape is PhysShapeCircle circle)
                _physics.SetPositionRadius(ent, key, fixture, circle, position, radius * scale, ent.Comp);
        }
    }

    private void RestoreCircles(Entity<FixturesComponent> ent, List<(string key, Vector2 position, float radius)> circles)
    {
        foreach (var (key, position, radius) in circles)
        {
            if (ent.Comp.Fixtures.TryGetValue(key, out var fixture) && fixture.Shape is PhysShapeCircle circle)
                _physics.SetPositionRadius(ent, key, fixture, circle, position, radius, ent.Comp);
        }
    }
}
