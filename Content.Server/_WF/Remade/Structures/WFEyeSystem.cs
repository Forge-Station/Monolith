using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Director;
using Content.Shared._WF.Remade.Structures;
using Content.Shared.Examine;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Structures;

/// <summary>
/// An eye watches a radius. A crewman walking into view makes it open and turn to follow them, tells the Director where they are as
/// if the Remade had heard them, and now and then lets the crewman feel watched.
/// </summary>
public sealed partial class WFEyeSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFGrowthSystem _growth = default!;
    [Dependency] private WFRemadeDirectorSystem _director = default!;

    private static readonly TimeSpan ScanRate = TimeSpan.FromSeconds(0.75);

    private readonly HashSet<Entity<ActorComponent>> _near = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFEyeComponent, WFGrowthComponent>();
        while (query.MoveNext(out var uid, out var eye, out var growth))
        {
            if (now < eye.NextScan)
                continue;

            eye.NextScan = now + ScanRate;
            if (!growth.Active)
            {
                eye.OpenUntil = null;
                _growth.SetState((uid, growth), WFGrowthState.Idle);
                continue;
            }

            Scan((uid, eye, growth), now);
        }
    }

    private void Scan(Entity<WFEyeComponent, WFGrowthComponent> ent, TimeSpan now)
    {
        var eye = ent.Comp1;
        if (Watch(ent) is { } crewman)
        {
            eye.OpenUntil = now + eye.StaysOpen;
            _growth.SetState((ent.Owner, ent.Comp2), WFGrowthState.Active);
            Turn(ent, crewman);
            Tell(ent, crewman, now);
            return;
        }

        if (eye.OpenUntil is { } until && now >= until)
        {
            eye.OpenUntil = null;
            _growth.SetState((ent.Owner, ent.Comp2), WFGrowthState.Idle);
        }
    }

    /// <summary>The nearest living crewman in range and in sight.</summary>
    private EntityUid? Watch(Entity<WFEyeComponent, WFGrowthComponent> ent)
    {
        _near.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp1.Range, _near);

        var origin = _transform.GetWorldPosition(ent);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var actor in _near)
        {
            if (HasComp<WFRemadeComponent>(actor) || !HasComp<MobStateComponent>(actor) || _mobState.IsDead(actor))
                continue;

            var distance = (_transform.GetWorldPosition(actor) - origin).LengthSquared();
            if (distance >= bestDistance || !_examine.InRangeUnOccluded(ent.Owner, actor.Owner, ent.Comp1.Range, null))
                continue;

            best = actor;
            bestDistance = distance;
        }

        return best;
    }

    private void Turn(EntityUid eye, EntityUid crewman)
    {
        var direction = _transform.GetWorldPosition(crewman) - _transform.GetWorldPosition(eye);
        if (direction.LengthSquared() > 0.01f)
            _transform.SetWorldRotation(eye, direction.ToWorldAngle());
    }

    /// <summary>Lets the Director hear of the sighting, and the crewman feel it.</summary>
    private void Tell(Entity<WFEyeComponent, WFGrowthComponent> ent, EntityUid crewman, TimeSpan now)
    {
        var eye = ent.Comp1;
        if (now >= eye.NextAlert)
        {
            eye.NextAlert = now + eye.AlertEvery;
            eye.Alerts++;
            _director.Noise(_transform.GetMapCoordinates(crewman), eye.AlertRange);
        }

        if (now >= eye.NextNotice)
        {
            eye.NextNotice = now + eye.NoticeEvery;
            _popup.PopupEntity(Loc.GetString("wf-remade-eye-watched"), crewman, crewman, PopupType.MediumCaution);
        }
    }
}
