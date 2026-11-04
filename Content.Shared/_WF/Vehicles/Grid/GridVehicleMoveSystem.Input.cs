using System;
using System.Numerics;
using Content.Shared._WF.NpcVehicleCrew;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared.Movement.Components;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Content.Shared.Movement.Systems;

namespace Content.Shared._WF.Vehicles.Grid;

/// <summary>Reads the operator's held keys as a grid-local direction, and as throttle and steering.</summary>
public sealed partial class GridVehicleMoverSystem : EntitySystem
{
    /// <summary>What the driver asks for: a grid direction for the four-way drive, throttle and steering for the free one.</summary>
    private readonly record struct VehicleControlInput(Vector2i Direction, float Throttle, float Steering);

    private Vector2i GetInputDirection(InputMoverComponent input, Angle turn)
    {
        var buttons = input.HeldMoveButtons;
        var dir = Vector2i.Zero;

        if ((buttons & MoveButtons.Up) != 0) dir += new Vector2i(0, 1);
        if ((buttons & MoveButtons.Down) != 0) dir += new Vector2i(0, -1);
        if ((buttons & MoveButtons.Right) != 0) dir += new Vector2i(1, 0);
        if ((buttons & MoveButtons.Left) != 0) dir += new Vector2i(-1, 0);

        if (dir == Vector2i.Zero)
            return dir;

        if (dir.X != 0 && dir.Y != 0)
        {
            if (Math.Abs(dir.X) >= Math.Abs(dir.Y))
                dir = new Vector2i(Math.Sign(dir.X), 0);
            else
                dir = new Vector2i(0, Math.Sign(dir.Y));
        }

        var turned = turn.RotateVec(new Vector2(dir.X, dir.Y));
        return new Vector2i((int) MathF.Round(turned.X), (int) MathF.Round(turned.Y));
    }

    /// <summary>The operator's held keys; a driver who is dead, unconscious or stunned steers nothing, whatever keys they still hold.</summary>
    private VehicleControlInput GetMoverInput(EntityUid uid, RMCVehicleComponent vehicle, TransformComponent vehicleXform)
    {
        if (vehicle.Operator is not { } op)
            return default;

        if (!_actionBlocker.CanConsciouslyPerformAction(op) || HasComp<StunnedComponent>(op))
            return default;

        // An NPC crew's driver steers through the autopilot instead of held keys.
        if (TryComp<WFVehicleAutopilotComponent>(uid, out var autopilot) && autopilot.Active)
            return new VehicleControlInput(autopilot.Direction, autopilot.Throttle, autopilot.Steering);

        if (!TryComp<InputMoverComponent>(op, out var inputComp))
            return default;

        // Forward and back are the throttle; left and right turn the wheel, anticlockwise being positive.
        var buttons = inputComp.HeldMoveButtons;
        var throttle = 0f;
        var steering = 0f;
        if ((buttons & MoveButtons.Up) != 0)
            throttle += 1f;
        if ((buttons & MoveButtons.Down) != 0)
            throttle -= 1f;
        if ((buttons & MoveButtons.Left) != 0)
            steering += 1f;
        if ((buttons & MoveButtons.Right) != 0)
            steering -= 1f;

        // A driver watching from the interior has the outside view turned by their camera keys; the four-way drive goes
        // where the keys point on that view. One standing on the vehicle's own map drives by the grid, as before.
        var turn = Transform(op).MapID != vehicleXform.MapID
            ? inputComp.TargetRelativeRotation.GetCardinalDir().ToAngle()
            : Angle.Zero;

        return new VehicleControlInput(GetInputDirection(inputComp, turn), throttle, steering);
    }
}
