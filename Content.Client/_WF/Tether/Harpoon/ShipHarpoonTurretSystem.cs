using Content.Shared._WF.Tether.Harpoon;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Map;

namespace Content.Client._WF.Tether.Harpoon;

/// <summary>
/// Client half of the harpoon turret. It tells the server where the operator is pointing, throttled, so the
/// turret swings with the cursor instead of only when it fires.
/// </summary>
public sealed class ShipHarpoonTurretSystem : SharedShipHarpoonTurretSystem
{
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlayerManager _player = default!;

    /// <summary>Shortest gap between two aim updates.</summary>
    private static readonly TimeSpan AimInterval = TimeSpan.FromSeconds(0.1);

    /// <summary>Smallest change worth sending.</summary>
    private static readonly Angle AimThreshold = Angle.FromDegrees(2);

    private TimeSpan _nextAim;
    private Angle _lastAim;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!Timing.IsFirstTimePredicted || Timing.CurTime < _nextAim)
            return;

        if (_player.LocalEntity is not { } player ||
            !TryComp<MannedTurretOperatorComponent>(player, out var operatorComp) ||
            !TryGetEntity(operatorComp.Turret, out var turret))
            return;

        var mouse = _eye.PixelToMap(_input.MouseScreenPosition);
        if (mouse.MapId == MapId.Nullspace)
            return;

        var offset = mouse.Position - Transforms.GetWorldPosition(turret.Value);
        if (offset.LengthSquared() < 0.01f)
            return;

        var aim = offset.ToWorldAngle();
        if (Math.Abs((aim - _lastAim).Reduced().Theta) < AimThreshold.Theta)
            return;

        _lastAim = aim;
        _nextAim = Timing.CurTime + AimInterval;
        RaisePredictiveEvent(new HarpoonTurretAimEvent(GetNetEntity(turret.Value), aim));
    }
}
