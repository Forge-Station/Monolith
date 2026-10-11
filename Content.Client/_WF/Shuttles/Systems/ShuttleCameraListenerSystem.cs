using System.Numerics;
using Content.Shared._WF.Shuttles;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Client._WF.Shuttles.Systems;

/// <summary>
/// Keeps positional sound centred on the pilot's body while a ship camera view carries their eye away.
/// The engine listens from the eye, so each sound is judged from the body and placed to match.
/// </summary>
public sealed class ShuttleCameraListenerSystem : EntitySystem
{
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _xform = default!;

    private EntityQuery<PhysicsComponent> _physicsQuery;
    private MapCoordinates _body;
    private Vector2 _shift;

    /// <summary>Whether sound is being heard from the pilot's body instead of the camera.</summary>
    public bool Active { get; private set; }

    /// <summary>Where positional sound is heard from: the body in a camera view, otherwise the eye.</summary>
    public MapCoordinates GetListener() => Active ? _body : _audio.GetListenerCoordinates();

    /// <summary>What to add to a sound's world position so the eye-bound engine listener hears it from the body.</summary>
    public Vector2 SourceShift => Active ? _shift : Vector2.Zero;

    public override void Initialize()
    {
        base.Initialize();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        // The body's position has to be in hand before the audio system's parallel pass reads it.
        UpdatesBefore.Add(typeof(AudioSystem));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        SetActive(false);
    }

    public override void FrameUpdate(float frameTime)
    {
        if (_player.LocalEntity is not { } pilot ||
            !TryComp<ShuttleCameraComponent>(pilot, out var camera) || camera.View == ShuttleCameraView.Helm ||
            !TryComp(pilot, out TransformComponent? xform) || xform.MapID == MapId.Nullspace)
        {
            SetActive(false);
            return;
        }

        _body = _xform.GetMapCoordinates(pilot, xform);
        _shift = _eye.CurrentEye.Position.Position - _body.Position;
        SetActive(true);
    }

    private void SetActive(bool active)
    {
        if (Active == active)
            return;
        Active = active;
        if (active)
            _audio.ProcessStreamOverride += Process;
        else
            _audio.ProcessStreamOverride -= Process;
    }

    /// <summary>
    /// Judges one sound from the body: false beyond its range, otherwise where the eye-bound listener must be told it is.
    /// </summary>
    public static bool TryPlace(Vector2 body, Vector2 shift, Vector2 source, float maxDistance, float zOffset,
        out Vector2 placed, out Vector2 delta, out float distance)
    {
        delta = source - body;
        distance = delta.Length();
        placed = default;
        if (MathF.Sqrt(distance * distance + zOffset * zOffset) > maxDistance)
            return false;

        if (distance > 0f && distance < 0.01f)
        {
            source = body;
            delta = Vector2.Zero;
            distance = 0f;
        }

        placed = source + shift;
        return true;
    }

    /// <summary>The engine's own per-sound pass with the body as the listener. Runs on the audio job's threads.</summary>
    private void Process(EntityUid entity, AudioComponent component, TransformComponent xform, MapCoordinates eye)
    {
        var listener = _body;
        if (!component.Started)
        {
            component.Started = true;
            component.StartPlaying();
        }

        if (component.Global)
        {
            if (xform.MapID != MapId.Nullspace && listener.MapId != xform.MapID)
            {
                component.Gain = 0f;
                return;
            }

            component.Volume = component.Params.Volume;
            return;
        }

        if (listener.MapId != xform.MapID)
        {
            component.Gain = 0f;
            return;
        }

        var parent = xform.ParentUid;
        component.Volume = component.Params.Volume;
        var source = (component.Flags & AudioFlags.GridAudio) != 0x0
            ? _maps.GetGridPosition(parent)
            : _xform.GetWorldPosition(entity);

        if (!TryPlace(listener.Position, _shift, source, component.MaxDistance, _audio.ZOffset,
                out var placed, out var delta, out var distance))
        {
            component.Gain = 0f;
            return;
        }

        component.Occlusion = (component.Flags & AudioFlags.NoOcclusion) == AudioFlags.NoOcclusion
            ? 0f
            : _audio.GetOcclusion(listener, delta, distance, parent);
        component.Position = placed;

        if (_physicsQuery.TryGetComponent(parent, out var physics))
            component.Velocity = _physics.GetMapLinearVelocity(parent, physics);
    }
}
