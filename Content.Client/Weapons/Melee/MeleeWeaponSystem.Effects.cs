using System.Numerics;
using Content.Client.Animations;
using Content.Client.Weapons.Melee.Components;
using Content.Shared.Weapons.Melee;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;
using Robust.Shared.Map;

namespace Content.Client.Weapons.Melee;

public sealed partial class MeleeWeaponSystem
{
    private const string FadeAnimationKey = "melee-fade";
    private const string SlashAnimationKey = "melee-slash";
    private const string ThrustAnimationKey = "melee-thrust";

    /// <summary>
    /// Does all of the melee effects for a player that are predicted, i.e. character lunge and weapon animation.
    /// </summary>
    public override void DoLunge(EntityUid user, EntityUid playerUid, EntityUid weapon, Angle angle, Vector2 localPos, string? animation, bool predicted = true)
    {
        if (!Timing.IsFirstTimePredicted)
            return;

        var lunge = GetLungeAnimation(localPos);

        // Stop any existing lunges on the user.
        _animation.Stop(user, MeleeLungeKey);
        _animation.Play(user, lunge, MeleeLungeKey);

        if (localPos == Vector2.Zero || animation == null)
            return;

        if (!_xformQuery.TryGetComponent(user, out var userXform) || userXform.MapID == MapId.Nullspace)
            return;

        var animationUid = Spawn(animation, userXform.Coordinates);

        if (!TryComp<SpriteComponent>(animationUid, out var sprite)
            || !TryComp<WeaponArcVisualsComponent>(animationUid, out var arcComponent))
        {
            return;
        }

// Forge-Change-Start
        var length = 1f;
        var offset = 1f;
// Forge-Change-End

        var spriteRotation = Angle.Zero;
        if (arcComponent.Animation != WeaponArcAnimation.None
            && TryComp(weapon, out MeleeWeaponComponent? meleeWeaponComponent))
        {
            if (user != weapon
                && TryComp(weapon, out SpriteComponent? weaponSpriteComponent))
                sprite.CopyFrom(weaponSpriteComponent);

            spriteRotation = meleeWeaponComponent.WideAnimationRotation;
//            var distance = Math.Clamp(localPos.Length() / 2f, 0.2f, 1f); // Forge-Change
            if (meleeWeaponComponent.SwingLeft)
                angle *= -1;

// Forge-Change-Start
            length = 1 / MathF.Max(meleeWeaponComponent.AttackRate, 0.01f) * 0.6f;
            offset = meleeWeaponComponent.AnimationOffset;
// Forge-Change-End
        }
        sprite.Rotation = localPos.ToWorldAngle();

        var xform = _xformQuery.GetComponent(animationUid);
        TrackUserComponent track;

        switch (arcComponent.Animation)
        {
            case WeaponArcAnimation.Slash:
                track = EnsureComp<TrackUserComponent>(animationUid);
                track.User = user;
                _animation.Play(animationUid, GetSlashAnimation(sprite, angle, spriteRotation, length, offset), SlashAnimationKey); // Forge-Change: added length, offset
                if (arcComponent.Fadeout)
                    _animation.Play(animationUid, GetFadeAnimation(sprite, length * 0.5f, length + 0.15f), FadeAnimationKey); // Forge-Change: sprite, 0.065f, 0.065f + 0.05f > sprite, length * 0.5f, length + 0.15f
                break;
            case WeaponArcAnimation.Thrust:
                track = EnsureComp<TrackUserComponent>(animationUid);
                track.User = user;
                _animation.Play(animationUid, GetThrustAnimation(sprite, offset, spriteRotation, length), ThrustAnimationKey); // Forge-Change: removed distance, added length, offset
                if (arcComponent.Fadeout)
                    _animation.Play(animationUid, GetFadeAnimation(sprite, length * 0.5f, length + 0.15f), FadeAnimationKey); // Forge-Change: sprite, 0.05f, 0.15f > sprite, length * 0.5f, length + 0.15f
                break;
            case WeaponArcAnimation.None:
                var (mapPos, mapRot) = TransformSystem.GetWorldPositionRotation(userXform);
                var worldPos = mapPos + (mapRot - userXform.LocalRotation).RotateVec(localPos);
                var newLocalPos = Vector2.Transform(worldPos, TransformSystem.GetInvWorldMatrix(xform.ParentUid));
                TransformSystem.SetLocalPositionNoLerp(animationUid, newLocalPos, xform);
                if (arcComponent.Fadeout)
                    _animation.Play(animationUid, GetFadeAnimation(sprite, 0f, 0.15f), FadeAnimationKey);
                break;
        }
    }

    private Animation GetSlashAnimation(SpriteComponent sprite, Angle arc, Angle spriteRotation, float length, float offset) // Forge-Change: added float length, float offset
    {
        var startRotation = sprite.Rotation + arc * 0.5f; // Forge-Change 2 > 0.5
        var endRotation = sprite.Rotation - arc * 0.5f; // Forge-Change 2 > 0.5

        var startRotationOffset = startRotation.RotateVec(new Vector2(0f, -offset * 0.9f)); // Forge-Change -1 > 0.9
        var minRotationOffset = sprite.Rotation.RotateVec(new Vector2(0f, -offset * 1.1f)); // Forge-Change
        var endRotationOffset = endRotation.RotateVec(new Vector2(0f, -offset * 0.9f)); // Forge-Change -1 > 0.9

        startRotation += spriteRotation;
        endRotation += spriteRotation;

        return new Animation()
        {
            Length = TimeSpan.FromSeconds(length + 0.05f), // Forge-Change: added + 0.05f
            AnimationTracks =
            {
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Rotation),
                    KeyFrames =
                    {
//                       new AnimationTrackProperty.KeyFrame(startRotation, 0f), // Forge-Change Commented
//                       new AnimationTrackProperty.KeyFrame(startRotation, slashStart), // Forge-Change Commented
//                       new AnimationTrackProperty.KeyFrame(endRotation, slashEnd) // Forge-Change Commented
// Forge-Change-Start
                        new AnimationTrackProperty.KeyFrame(Angle.Lerp(startRotation, endRotation, 0.0f), length * 0.0f),
                        new AnimationTrackProperty.KeyFrame(Angle.Lerp(startRotation, endRotation, 0.5f), length * 0.10f),
                        new AnimationTrackProperty.KeyFrame(Angle.Lerp(startRotation, endRotation, 1.0f), length * 0.15f),
                        new AnimationTrackProperty.KeyFrame(Angle.Lerp(startRotation, endRotation, 0.9f), length * 0.20f),
                        new AnimationTrackProperty.KeyFrame(Angle.Lerp(startRotation, endRotation, 0.8f), length * 0.6f, Easings.OutQuart),
// Forge-Change-End
                    },
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    KeyFrames =
                    {
//                        new AnimationTrackProperty.KeyFrame(startRotationOffset, 0f), // Forge-Change Commented
//                        new AnimationTrackProperty.KeyFrame(startRotationOffset, slashStart), // Forge-Change Commented
//                        new AnimationTrackProperty.KeyFrame(endRotationOffset, slashEnd) // Forge-Change Commented
// Forge-Change-Start
                        new AnimationTrackProperty.KeyFrame(Vector2.Lerp(startRotationOffset, endRotationOffset, 0.0f), length * 0.0f),
                        new AnimationTrackProperty.KeyFrame(minRotationOffset, length * 0.10f),
                        new AnimationTrackProperty.KeyFrame(Vector2.Lerp(startRotationOffset, endRotationOffset, 1.0f), length * 0.15f),
                        new AnimationTrackProperty.KeyFrame(Vector2.Lerp(startRotationOffset, endRotationOffset, 0.8f), length * 0.6f, Easings.OutQuart),
// Forge-Change-End
                    },
                },
            },
        };
    }

    private Animation GetThrustAnimation(SpriteComponent sprite, float offset, Angle spriteRotation, float length) // Forge-Change: added offset, float length. Removed distance
    {
//        const float thrustEnd = 0.05f; // Forge-Change Commented
//        const float length = 0.15f; // Forge-Change Commented
//        var startOffset = sprite.Rotation.RotateVec(new Vector2(0f, -distance / 5f)); // Forge-Change Commented
//        var endOffset = sprite.Rotation.RotateVec(new Vector2(0f, -distance)); // Forge-Change Commented
// Forge-Change-Start
        var startOffset = Vector2.Zero;
        var endOffset = sprite.Rotation.RotateVec(new Vector2(0f, -offset * 1.2f));
// Forge-Change-End
        sprite.Rotation += spriteRotation;

        return new Animation()
        {
            Length = TimeSpan.FromSeconds(length),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    KeyFrames =
                    {
//                        new AnimationTrackProperty.KeyFrame(startOffset, 0f), // Forge-Change Commented
//                        new AnimationTrackProperty.KeyFrame(endOffset, thrustEnd), // Forge-Change Commented
//                        new AnimationTrackProperty.KeyFrame(endOffset, length), // Forge-Change Commented
// Forge-Change-Start
                        new AnimationTrackProperty.KeyFrame(Vector2.Lerp(startOffset, endOffset, 0f), length * 0f),
                        new AnimationTrackProperty.KeyFrame(Vector2.Lerp(startOffset, endOffset, 0.65f), length * 0.10f),
                        new AnimationTrackProperty.KeyFrame(Vector2.Lerp(startOffset, endOffset, 1f), length * 0.20f),
                        new AnimationTrackProperty.KeyFrame(Vector2.Lerp(startOffset, endOffset, 0.9f), length * 0.30f),
                        new AnimationTrackProperty.KeyFrame(Vector2.Lerp(startOffset, endOffset, 0.7f), length * 0.60f, Easings.OutQuart),
// Forge-Change-End
                    },
                },
            },
        };
    }

    private Animation GetFadeAnimation(SpriteComponent sprite, float start, float end)
    {
        return new Animation
        {
            Length = TimeSpan.FromSeconds(end),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(sprite.Color, start),
                        new AnimationTrackProperty.KeyFrame(sprite.Color.WithAlpha(0f), end)
                    }
                }
            }
        };
    }

    /// <summary>
    /// Get the sprite offset animation to use for mob lunges.
    /// </summary>
    private Animation GetLungeAnimation(Vector2 direction)
    {
        const float length = 0.1f;

        return new Animation
        {
            Length = TimeSpan.FromSeconds(length),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
//                        new AnimationTrackProperty.KeyFrame(direction.Normalized() * 0.15f, 0f), // Forge-Change Commented
//                        new AnimationTrackProperty.KeyFrame(Vector2.Zero, length) // Forge-Change Commented
// Forge-Change-Start
                        new AnimationTrackProperty.KeyFrame(Vector2.Zero, 0f),
                        new AnimationTrackProperty.KeyFrame(direction.Normalized() * 0.15f, length * 0.4f),
                        new AnimationTrackProperty.KeyFrame(Vector2.Zero, length * 0.6f),
// Forge-Change-End
                    }
                }
            }
        };
    }

    /// <summary>
    /// Updates the effect positions to follow the user
    /// </summary>
    private void UpdateEffects()
    {
        var query = EntityQueryEnumerator<TrackUserComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var arcComponent, out var xform))
        {
            if (arcComponent.User == null || EntityManager.Deleted(arcComponent.User) || !xform.ParentUid.IsValid()) // Forge-Change: added || EntityManager.Deleted(arcComponent.User) || !xform.ParentUid.IsValid())
                continue;

            Vector2 targetPos = TransformSystem.GetWorldPosition(arcComponent.User.Value);

            if (arcComponent.Offset != Vector2.Zero)
            {
                var entRotation = TransformSystem.GetWorldRotation(xform);
                targetPos += entRotation.RotateVec(arcComponent.Offset);
            }

//            TransformSystem.SetWorldPosition(uid, targetPos); // Forge-Change Commented
// Forge-Change-Start
            var localPos = Vector2.Transform(targetPos, TransformSystem.GetInvWorldMatrix(xform.ParentUid));
            TransformSystem.SetLocalPositionNoLerp(uid, localPos, xform);
// Forge-Change-End
        }
    }
}
