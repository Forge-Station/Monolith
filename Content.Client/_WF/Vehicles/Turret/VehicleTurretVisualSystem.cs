using System.Linq;
using System.Numerics;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared._WF.Vehicles.Turret;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._WF.Vehicles.Turret;

/// <summary>Draws a turret's overlay sprite on its vehicle, turned and offset as the turret is.</summary>
public sealed partial class VehicleTurretVisualSystem : EntitySystem
{
    private const float PixelsPerMeter = 32f;

    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private VehicleTurretSystem _turret = default!;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(VehicleTurretSystem));
        SubscribeLocalEvent<VehicleTurretVisualComponent, ComponentInit>(OnVisualInit);
        SubscribeLocalEvent<VehicleTurretVisualComponent, AfterAutoHandleStateEvent>(OnVisualState);
        SubscribeLocalEvent<HardpointIntegrityComponent, AfterAutoHandleStateEvent>(OnIntegrityState);
    }

    public override void FrameUpdate(float frameTime)
    {
        var query = EntityQueryEnumerator<VehicleTurretVisualComponent>();
        while (query.MoveNext(out var uid, out var visual))
        {
            if (!visual.SpriteInitialized)
                UpdateVisual((uid, visual));

            if (!TryGetEntity(visual.Turret, out var turretUid))
                continue;

            if (!TryComp(turretUid, out VehicleTurretComponent? turret))
                continue;

            if (!TryComputeRenderedTransform((EntityUid) turretUid,
                    turret,
                    out var vehicleUid,
                    out _,
                    out var localOffset,
                    out var localRotation))
            {
                continue;
            }

            if (turret.OffsetSpritePivot)
            {
                var facing = (_transform.GetWorldRotation(vehicleUid) + localRotation + _eye.CurrentEye.Rotation).Reduced();
                var pivot = _turret.GetPixelOffset(turret, facing) / PixelsPerMeter;
                localOffset -= localRotation.RotateVec(VehicleTurretDirectionHelpers.GetLocalOffsetForRenderDirection(pivot, facing));
            }

            var visualXform = Transform(uid);
            visualXform.ActivelyLerping = false;
            _transform.SetLocalRotationNoLerp(uid, localRotation, visualXform);
            _transform.SetLocalPositionNoLerp(uid, localOffset, visualXform);
        }
    }

    /// <summary>Where a turret's sprite is drawn now and which way it points.</summary>
    public bool TryGetRenderedPose(EntityUid turretUid, out EntityCoordinates origin, out Angle worldRotation)
    {
        origin = default;
        worldRotation = Angle.Zero;

        if (!TryComp(turretUid, out VehicleTurretComponent? turret))
            return false;

        if (!TryComputeRenderedTransform(turretUid,
                turret,
                out var vehicle,
                out var vehicleRotation,
                out var localOffset,
                out var localRotation))
        {
            return false;
        }

        origin = _transform.GetMoverCoordinates(new EntityCoordinates(vehicle, localOffset));
        worldRotation = (vehicleRotation + localRotation).Reduced();
        return true;
    }

    private void OnVisualInit(Entity<VehicleTurretVisualComponent> ent, ref ComponentInit args)
    {
        UpdateVisual(ent);
    }

    private void OnVisualState(Entity<VehicleTurretVisualComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateVisual(ent);
    }

    private void OnIntegrityState(Entity<HardpointIntegrityComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!HasComp<VehicleTurretComponent>(ent.Owner))
            return;

        RefreshLinkedVisuals(ent.Owner);
    }

    private void UpdateVisual(Entity<VehicleTurretVisualComponent> ent)
    {
        if (!TryComp(ent.Owner, out SpriteComponent? sprite))
            return;

        if (!TryGetEntity(ent.Comp.Turret, out var turretUid))
            return;

        if (TryComp(turretUid, out VehicleTurretComponent? turret) &&
            !string.IsNullOrWhiteSpace(turret.OverlayState))
        {
            SetOverlayDepth(ent.Owner, (EntityUid) turretUid, sprite);
            var overlayState = ResolveOverlayState((EntityUid) turretUid, turret);
            if (!string.IsNullOrWhiteSpace(turret.OverlayRsi))
                _sprite.LayerSetRsi((ent.Owner, sprite), 0, new ResPath(turret.OverlayRsi), overlayState);
            else
                _sprite.LayerSetRsiState((ent.Owner, sprite), 0, overlayState);

            _sprite.LayerSetVisible((ent.Owner, sprite), 0, true);
            ent.Comp.SpriteInitialized = true;
            return;
        }

        if (!TryComp(turretUid, out SpriteComponent? turretSprite))
            return;

        if (turretSprite.BaseRSI == null || !turretSprite.AllLayers.Any())
            return;

        SetOverlayDepth(ent.Owner, (EntityUid) turretUid, sprite);
        var state = _sprite.LayerGetRsiState((turretUid.Value, turretSprite), 0);
        _sprite.LayerSetRsi((ent.Owner, sprite), 0, turretSprite.BaseRSI, state);
        _sprite.LayerSetVisible((ent.Owner, sprite), 0, true);
        ent.Comp.SpriteInitialized = true;
    }

    private void SetOverlayDepth(EntityUid visualUid, EntityUid turretUid, SpriteComponent sprite)
    {
        var depth = (int) DrawDepth.OverMobs;
        if (TryComp(turretUid, out VehicleTurretComponent? turret))
            depth += turret.OverlayDepthOffset;
        if (HasComp<VehicleTurretAttachmentComponent>(turretUid))
            depth += 1;

        if (sprite.DrawDepth != depth)
            _sprite.SetDrawDepth((visualUid, sprite), depth);
    }

    private string ResolveOverlayState(EntityUid turretUid, VehicleTurretComponent turret)
    {
        if (!TryComp(turretUid, out HardpointIntegrityComponent? integrity) ||
            integrity.Integrity > 0f ||
            string.IsNullOrWhiteSpace(turret.OverlayDamagedState))
        {
            return turret.OverlayState;
        }

        return turret.OverlayDamagedState;
    }

    private void RefreshLinkedVisuals(EntityUid turretUid)
    {
        var netTurret = GetNetEntity(turretUid);
        var query = EntityQueryEnumerator<VehicleTurretVisualComponent>();
        while (query.MoveNext(out var uid, out var visual))
        {
            if (visual.Turret != netTurret)
                continue;

            UpdateVisual((uid, visual));
        }
    }

    private bool TryComputeRenderedTransform(
        EntityUid turretUid,
        VehicleTurretComponent turret,
        out EntityUid vehicle,
        out Angle vehicleRot,
        out Vector2 localOffset,
        out Angle localRotation)
    {
        vehicle = default;
        vehicleRot = Angle.Zero;
        localOffset = Vector2.Zero;
        localRotation = Angle.Zero;

        if (!_turret.TryGetVehicle(turretUid, out vehicle))
            return false;

        _turret.TryGetAnchorTurret(turretUid, turret, out var anchorUid, out var anchorTurret);

        vehicleRot = _transform.GetWorldRotation(vehicle);
        var eyeRot = _eye.CurrentEye.Rotation;
        var baseFacingAngle = _turret.GetVehicleFacingAngle(vehicle, vehicleRot);
        var anchorFacingAngle = GetRenderFacing(anchorTurret, anchorTurret, vehicleRot, baseFacingAngle, eyeRot);
        var anchorPixelOffset = _turret.GetPixelOffset(anchorTurret, anchorFacingAngle) / PixelsPerMeter;
        var anchorLocalOffset = GetVehicleLocalOffset(anchorTurret, anchorPixelOffset, vehicleRot, eyeRot, anchorFacingAngle);

        var targetLocalRotation = anchorTurret.RotateToCursor ? anchorTurret.WorldRotation : Angle.Zero;
        localOffset = anchorLocalOffset;
        localRotation = targetLocalRotation;

        if (anchorUid == turretUid)
            return true;

        var turretFacingAngle = GetRenderFacing(turret, anchorTurret, vehicleRot, baseFacingAngle, eyeRot);
        var worldOffset = _turret.GetPixelOffset(turret, turretFacingAngle) / PixelsPerMeter;
        Vector2 turretLocalOffset;

        if (turret.OffsetRotatesWithTurret)
        {
            if (turret.UseDirectionalOffsets)
            {
                var dir = VehicleTurretDirectionHelpers.GetRenderAlignedCardinalDir(turretFacingAngle);
                var snappedAngle = dir.ToAngle();
                turretLocalOffset = (targetLocalRotation - snappedAngle).RotateVec(worldOffset);
            }
            else
            {
                turretLocalOffset = targetLocalRotation.RotateVec(worldOffset);
            }
        }
        else
        {
            turretLocalOffset = GetVehicleLocalOffset(turret, worldOffset, vehicleRot, eyeRot, turretFacingAngle);
        }

        localOffset += turretLocalOffset;
        return true;
    }

    private Angle GetRenderFacing(
        VehicleTurretComponent turret,
        VehicleTurretComponent anchorTurret,
        Angle vehicleRot,
        Angle baseFacingAngle,
        Angle eyeRot)
    {
        return (_turret.GetOffsetFacing(turret, anchorTurret, vehicleRot, baseFacingAngle) + eyeRot).Reduced();
    }

    private static Vector2 GetVehicleLocalOffset(
        VehicleTurretComponent turret,
        Vector2 offset,
        Angle vehicleRot,
        Angle eyeRot,
        Angle renderFacing)
    {
        // An offset given per pose goes with the pose on screen, which is turned by what is left of the hull's angle.
        if (turret.UseDirectionalOffsets)
            return VehicleTurretDirectionHelpers.GetLocalOffsetForRenderDirection(offset, renderFacing);

        return (-vehicleRot).RotateVec(offset);
    }
}
