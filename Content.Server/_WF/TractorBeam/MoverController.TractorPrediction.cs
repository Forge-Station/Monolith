using System.Numerics;
using Content.Server.Shuttles.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server.Physics.Controllers;

public sealed partial class MoverController
{
    /// <summary>Forecast the real late brake response without spending engine thrust twice.</summary>
    public (Vector2 Linear, float Angular, bool LinearHeld, bool AngularHeld) PredictTractorBraking(
        EntityUid uid, ShuttleComponent shuttle, PhysicsComponent body, float elapsed,
        Vector2 extraImpulse = default, float extraAngularImpulse = 0)
    {
        _shuttlePropulsion.TryGetValue(uid, out var propulsion);
        var linear = body.LinearVelocity + body.Force * body.InvMass * elapsed + extraImpulse * body.InvMass;
        var angular = body.AngularVelocity + body.Torque * body.InvI * elapsed + extraAngularImpulse * body.InvI;
        var linearHeld = false;
        var angularHeld = false;
        if (propulsion.LinearInput == Vector2.Zero)
        {
            linear -= propulsion.Force * body.InvMass * elapsed;
            var rotation = _transform.GetWorldRotation(uid);
            var available = GetDirectionThrust((-rotation).RotateVec(-linear), shuttle, body, Transform(uid)) *
                ShuttleComponent.BrakeCoefficient;
            var delta = rotation.RotateVec(available) * body.InvMass * elapsed;
            linearHeld = delta.LengthSquared() >= linear.LengthSquared() && delta.LengthSquared() > 0;
            linear = linearHeld ? Vector2.Zero : linear + delta;
        }
        if (propulsion.AngularInput == 0f)
        {
            angular -= propulsion.Torque * body.InvI * elapsed;
            var available = shuttle.AngularThrust * ShuttleComponent.BrakeCoefficient * body.InvI * elapsed;
            angularHeld = available > 0 && MathF.Abs(angular) <= available;
            angular -= Math.Clamp(angular, -available, available);
        }
        return (linear, angular, linearHeld, angularHeld);
    }
}
