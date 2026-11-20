using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WF.Caverns;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics.Joints;
using Robust.Shared.Timing;

namespace Content.Server._WF.Caverns;

public sealed partial class WFCavernStairsSystem
{
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private SharedContainerSystem _container = default!;

    /// <summary>How much rope a pull taken up again after a level change has, in tiles: about what one started beside its puller has.</summary>
    public const float CarryRope = 1.15f;

    /// <summary>Pulls a change of level ended, and the tick it ended them on.</summary>
    private readonly List<(EntityUid Puller, EntityUid Pulled, GameTick Tick)> _ended = new();

    private void UpdatePulled()
    {
        // A tick late: the hand that held the pull is only free once the tick it ended on is over.
        for (var i = _ended.Count - 1; i >= 0; i--)
        {
            var (puller, pulled, tick) = _ended[i];

            if (tick >= _timing.CurTick)
                continue;

            _ended.RemoveAt(i);
            BringPulled(puller, pulled);
        }
    }

    /// <summary>
    /// Has what a puller was pulling follow it to the level it changed to, a tick from now. A change of level ends
    /// every pull: it clears the joints of whoever changes map.
    /// </summary>
    public void BringPulledSoon(EntityUid puller, EntityUid pulled)
    {
        foreach (var ended in _ended)
        {
            if (ended.Puller == puller && ended.Pulled == pulled)
                return;
        }

        _ended.Add((puller, pulled, _timing.CurTick));
    }

    /// <summary>Puts what was pulled on its puller's spot, a level up or down if need be, and takes hold of it again.</summary>
    public bool BringPulled(EntityUid puller, EntityUid pulled)
    {
        if (TerminatingOrDeleted(puller)
            || TerminatingOrDeleted(pulled)
            || !TryComp<CEZPhysicsComponent>(puller, out var level)
            || !HasComp<CEZPhysicsComponent>(pulled)
            || _container.IsEntityInContainer(pulled))
            return false;

        var pullerXform = Transform(puller);

        if (pullerXform.MapUid is not { } to || Transform(pulled).MapUid is not { } from)
            return false;

        if (to != from)
        {
            int offset;
            if (TryComp<WFCavernLayerComponent>(from, out var below) && below.Ground == to)
                offset = 1;
            else if (TryComp<WFCavernLayerComponent>(to, out var layer) && layer.Ground == from)
                offset = -1;
            else
                return false;

            if (!_zLevels.TryMove(pulled, offset) || Transform(pulled).MapUid != to)
                return false;
        }

        // Onto the puller's own spot, which is known to hold it up: on stairs the tiles behind it don't.
        _transform.SetWorldPosition(pulled, _transform.GetWorldPosition(pullerXform));
        _zLevels.SetZPosition(pulled, level.LocalPosition);
        _zLevels.SetZVelocity(pulled, 0f);

        if (!_pulling.TryStartPull(puller, pulled)
            || !TryComp<PullableComponent>(pulled, out var pullable)
            || !TryComp<JointComponent>(pulled, out var joints))
            return false;

        // A pull's rope is as long as its two ends were apart, which here is nothing.
        var id = pullable.PullJointId;
        if (id != null && joints.GetJoints.TryGetValue(id, out var joint) && joint is DistanceJoint rope)
            rope.MaxLength = CarryRope;

        return true;
    }
}
