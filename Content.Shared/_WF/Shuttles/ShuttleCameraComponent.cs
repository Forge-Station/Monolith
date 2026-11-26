using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Shuttles;

/// <summary>
/// Which hull camera a pilot is looking through.
/// </summary>
[Serializable, NetSerializable]
public enum ShuttleCameraView : byte
{
    /// <summary>The pilot's own eyes, at the console.</summary>
    Helm,
    Front,
    Rear,
    Left,
    Right,

    /// <summary>The whole ship from outside, with its hulls roofed over. The pilot pans it.</summary>
    External,
}

/// <summary>
/// Sits on a pilot while they fly, holding the view their console is feeding them.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShuttleCameraComponent : Component
{
    public const float MinZoom = 1f;
    public const float MaxZoom = 4f;
    public const float ZoomStep = 0.25f;

    /// <summary>
    /// How far past the hull's bounds the external view can be panned, in tiles.
    /// </summary>
    public const float PanMargin = 10f;

    [DataField, AutoNetworkedField]
    public ShuttleCameraView View = ShuttleCameraView.Helm;

    [DataField, AutoNetworkedField]
    public float Zoom = 1.5f;

    /// <summary>
    /// Whether hull views are fed through a low-light camera, which the client draws. The helm never
    /// is, those are the pilot's own eyes.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool LowLight;

    /// <summary>
    /// The hull camera the eye rides on, or null at the helm.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Camera;

    /// <summary>
    /// The grid being flown while in the external view, which the pan is measured on. Null otherwise.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Grid;

    /// <summary>
    /// When the server next takes a pan from this pilot. Not networked.
    /// </summary>
    public TimeSpan NextPan;

    public override bool SendOnlyToOwner => true;
}

/// <summary>
/// Remembers the last view picked at a console, so sitting back down doesn't reset it.
/// </summary>
[RegisterComponent]
public sealed partial class ShuttleCameraSettingsComponent : Component
{
    [DataField]
    public ShuttleCameraView View = ShuttleCameraView.Helm;

    [DataField]
    public float? Zoom;

    [DataField]
    public bool LowLight;
}

/// <summary>Pilot picked a camera view, zoom or low-light setting.</summary>
[Serializable, NetSerializable]
public sealed class ShuttleCameraSetMessage : BoundUserInterfaceMessage
{
    public ShuttleCameraView View;
    public float Zoom;
    public bool LowLight;

    public ShuttleCameraSetMessage(ShuttleCameraView view, float zoom, bool lowLight)
    {
        View = view;
        Zoom = zoom;
        LowLight = lowLight;
    }
}

/// <summary>Pilot panned the external view. Carries the point on the flown grid they're looking at.</summary>
[Serializable, NetSerializable]
public sealed class ShuttleCameraPanMessage : BoundUserInterfaceMessage
{
    public Vector2 Position;

    public ShuttleCameraPanMessage(Vector2 position)
    {
        Position = position;
    }
}

/// <summary>Pilot changed the zoom alone, leaving the view and low-light as they are.</summary>
[Serializable, NetSerializable]
public sealed class ShuttleCameraZoomMessage : BoundUserInterfaceMessage
{
    public float Zoom;

    public ShuttleCameraZoomMessage(float zoom)
    {
        Zoom = zoom;
    }
}
