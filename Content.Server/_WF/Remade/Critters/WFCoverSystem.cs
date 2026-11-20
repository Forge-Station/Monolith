using Content.Server.NPC.HTN;
using Content.Shared._DV.Abilities;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Systems;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Movement.Systems;
using Content.Shared.Placeable;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Critters;

/// <summary>
/// Hiding under tables. A hider climbs onto a table and is flagged as sneaking, so the client draws it beneath the table top
/// the way Rodentia sneak (<see cref="CrawlUnderObjectsComponent"/>), and its sight shrinks until the prey is close.
/// </summary>
public sealed class WFCoverSystem : EntitySystem
{
    [Dependency] private ClimbSystem _climb = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private const string VisionKey = "VisionRadius";

    private readonly HashSet<Entity<ClimbableComponent>> _climbables = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFHiderComponent, MobStateChangedEvent>(OnState);
        SubscribeLocalEvent<WFHiderComponent, DamageChangedEvent>(OnDamaged);
        SubscribeLocalEvent<WFHiderComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnState(Entity<WFHiderComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            Emerge(ent, 0f);
    }

    private void OnDamaged(Entity<WFHiderComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased && ent.Comp.Cover != null)
            Emerge(ent, ent.Comp.Cooldown);
    }

    private void OnShutdown(Entity<WFHiderComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Cover != null && !TerminatingOrDeleted(ent))
            Emerge(ent, 0f);
    }

    public bool IsHiding(EntityUid uid)
    {
        return TryComp<WFHiderComponent>(uid, out var hider) && hider.Cover != null;
    }

    /// <summary>Whether something is low cover a mob can get under: a table that isn't glass, on a grid.</summary>
    public bool IsCover(EntityUid uid)
    {
        return !TerminatingOrDeleted(uid)
               && Transform(uid).Anchored
               && HasComp<ClimbableComponent>(uid)
               && HasComp<PlaceableSurfaceComponent>(uid)
               && !HasComp<GlassTableComponent>(uid);
    }

    /// <summary>The nearest free cover in the hider's range, or the one it is already under. Null when it is too soon to hide again.</summary>
    public EntityUid? PickCover(Entity<WFHiderComponent> hider)
    {
        if (hider.Comp.Cover is { } held && IsCover(held))
            return held;

        if (_timing.CurTime < hider.Comp.NoCoverUntil)
            return null;

        var here = Transform(hider);
        if (here.GridUid == null)
            return null;

        _climbables.Clear();
        _lookup.GetEntitiesInRange(here.Coordinates, hider.Comp.Range, _climbables);

        var position = _transform.GetWorldPosition(here);
        EntityUid? nearest = null;
        var best = float.MaxValue;
        foreach (var cover in _climbables)
        {
            if (!IsCover(cover) || Transform(cover).GridUid != here.GridUid || Taken(cover))
                continue;

            var distance = (_transform.GetWorldPosition(cover) - position).LengthSquared();
            if (distance < best)
            {
                best = distance;
                nearest = cover;
            }
        }

        return nearest;
    }

    private bool Taken(EntityUid cover)
    {
        var query = EntityQueryEnumerator<WFHiderComponent>();
        while (query.MoveNext(out _, out var hider))
        {
            if (hider.Cover == cover)
                return true;
        }

        return false;
    }

    /// <summary>Gets a hider onto cover it is standing beside. Fails if it is too far, or is already hiding elsewhere.</summary>
    public bool Hide(Entity<WFHiderComponent> hider, EntityUid cover)
    {
        if (hider.Comp.Cover == cover)
            return true;

        if (hider.Comp.Cover != null
            || !IsCover(cover)
            || !_transform.InRange(hider.Owner, cover, hider.Comp.Reach + 0.5f)
            || !TryComp<ClimbingComponent>(hider, out var climbing))
        {
            hider.Comp.NoCoverUntil = _timing.CurTime + TimeSpan.FromSeconds(hider.Comp.Cooldown);
            return false;
        }

        _climb.ForciblySetClimbing(hider, cover, climbing);
        if (!climbing.IsClimbing)
        {
            hider.Comp.NoCoverUntil = _timing.CurTime + TimeSpan.FromSeconds(hider.Comp.Cooldown);
            return false;
        }

        hider.Comp.Cover = cover;
        SetSneaking(hider, true);

        if (TryComp<HTNComponent>(hider, out var htn))
        {
            hider.Comp.Sight = htn.Blackboard.GetValueOrDefault<float>(VisionKey, EntityManager);
            htn.Blackboard.SetValue(VisionKey, hider.Comp.HiddenSight);
        }

        return true;
    }

    /// <summary>Comes out from under the cover, and won't look for more for a while.</summary>
    public void Emerge(Entity<WFHiderComponent> hider, float cooldown)
    {
        if (cooldown > 0f)
            hider.Comp.NoCoverUntil = _timing.CurTime + TimeSpan.FromSeconds(cooldown);

        if (hider.Comp.Cover == null)
            return;

        hider.Comp.Cover = null;
        SetSneaking(hider, false);

        if (hider.Comp.Sight is { } sight && TryComp<HTNComponent>(hider, out var htn))
            htn.Blackboard.SetValue(VisionKey, sight);

        hider.Comp.Sight = null;
    }

    /// <summary>Flips the Rodentia sneak flag directly, so there's no popup to give the hider away.</summary>
    private void SetSneaking(EntityUid uid, bool on)
    {
        if (!TryComp<CrawlUnderObjectsComponent>(uid, out var crawl))
            return;

        crawl.Enabled = on;
        Dirty(uid, crawl);
        _speed.RefreshMovementSpeedModifiers(uid);
        _appearance.SetData(uid, SneakMode.Enabled, on);
    }
}
