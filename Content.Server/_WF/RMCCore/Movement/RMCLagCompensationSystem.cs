using Content.Server.Movement.Systems;
using Content.Shared._WF.RMCCore.Movement;
using Robust.Server.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.RMCCore.Movement;

public sealed partial class RMCLagCompensationSystem : SharedRMCLagCompensationSystem
{
    [Dependency] private LagCompensationSystem _lagCompensation = default!;

    public override void Initialize()
    {
        base.Initialize();

        // WOLFGATE: Wolfgate's LagCompensationSystem.BufferTime is a 750 ms constant, so rmc.lag_compensation_milliseconds isn't applied
    }

    public override (EntityCoordinates Coordinates, Angle Angle) GetCoordinatesAngle(EntityUid uid,
        ICommonSession? pSession,
        TransformComponent? xform = null)
    {
        return _lagCompensation.GetCoordinatesAngle(uid, pSession, xform);
    }

    public override Angle GetAngle(EntityUid uid, ICommonSession? session, TransformComponent? xform = null)
    {
        return _lagCompensation.GetAngle(uid, session, xform);
    }

    public override EntityCoordinates GetCoordinates(EntityUid uid, ICommonSession? session, TransformComponent? xform = null)
    {
        return _lagCompensation.GetCoordinates(uid, session, xform);
    }
}
