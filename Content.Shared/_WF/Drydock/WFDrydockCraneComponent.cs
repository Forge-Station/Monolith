using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Drydock;

/// <summary>
/// Two-by-two crane. A straight arm runs from its root down the bay to the ship's far end, and a telescopic boom
/// slews from a carriage riding that arm. Units are tiles.
/// </summary>
/// <remarks>
/// Arm frame: <c>across</c> is the local X (mirrored about <see cref="ArmX"/>) and <c>along</c> is the distance down the
/// arm from its root, <c>ArmRoot - local.Y</c>. The boom's angle 0 points across into the bay; 90 folds it along the arm.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFDrydockCraneComponent : Component
{
    /// <summary>Power ratio below which a crane stalls: no work, no movement, lights out.</summary>
    public const float StallRatio = 0.25f;

    /// <summary>Console this crane is linked to with a multitool. The cranes linked to a console form its berth.</summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? LinkedConsole;

    /// <summary>Longest arm, in tiles.</summary>
    [DataField, AutoNetworkedField]
    public float MaxArm = 36f;

    /// <summary>Tiles of arm between the root and the start of the berth, so the work area stands clear of the machine.</summary>
    [DataField]
    public float BerthGap;

    /// <summary>Target arm length, in tiles.</summary>
    [DataField, AutoNetworkedField]
    public float Arm;

    /// <summary>Arm extension and retraction speed, in tiles per second.</summary>
    [DataField]
    public float ArmSpeed = 4f;

    /// <summary>Power drawn while idle, in watts.</summary>
    [DataField]
    public float IdleLoad = 500f;

    /// <summary>Power drawn while working, in watts.</summary>
    [DataField, AutoNetworkedField]
    public float WorkingLoad = 15000f;

    /// <summary>Multiplier on how fast the crane works and moves, before power; raised by part upgrades.</summary>
    [DataField, AutoNetworkedField]
    public float SpeedMultiplier = 1f;

    /// <summary>Share of the working power received, 0 to 1; scales how fast the crane works and moves.</summary>
    [DataField, AutoNetworkedField]
    public float PowerRatio = 1f;

    /// <summary>Whether the machine is flipped: the boom swings toward -X. See <see cref="MirrorInPlace"/>.</summary>
    [DataField, AutoNetworkedField]
    public bool Mirrored;

    /// <summary>
    /// Whether mirroring keeps the body on its tiles and moves the arm to the body's other column. Such a crane is
    /// mirrored with a Cutting tool; otherwise the arm stays put and the body shifts to -X.
    /// </summary>
    [DataField]
    public bool MirrorInPlace;

    /// <summary>Local Y where the arm leaves the body. The body's centre sits one tile above it.</summary>
    [DataField]
    public float ArmRoot = -0.5f;

    /// <summary>Number of nested boom sections.</summary>
    [DataField, AutoNetworkedField]
    public int Sections = 4;

    /// <summary>Length of one boom section, which is also the retracted boom length, in tiles.</summary>
    [DataField, AutoNetworkedField]
    public int SectionLength = 5;

    /// <summary>Boom angle in degrees while parked; 90 folds it along the arm.</summary>
    [DataField]
    public float ParkAngle = 90f;

    /// <summary>Carriage speed along the arm, in tiles per second.</summary>
    [DataField]
    public float Speed = 3f;

    /// <summary>Speed of the tool head along the boom, in tiles per second.</summary>
    [DataField]
    public float TrolleySpeed = 4f;

    /// <summary>Boom extension speed, in tiles per second.</summary>
    [DataField]
    public float ExtendSpeed = 2.5f;

    /// <summary>Boom slewing speed, in degrees per second.</summary>
    [DataField]
    public float SlewSpeed = 45f;

    /// <summary>Target carriage position along the arm.</summary>
    [DataField, AutoNetworkedField]
    public float Position;

    /// <summary>Target tool head position along the boom.</summary>
    [DataField, AutoNetworkedField]
    public float TrolleyPosition;

    /// <summary>Target boom length; clamped between one section and <see cref="MaxExtension"/>.</summary>
    [DataField, AutoNetworkedField]
    public float Extension;

    /// <summary>Target boom angle in degrees, in the arm frame.</summary>
    [DataField, AutoNetworkedField]
    public float Angle = 90f;

    /// <summary>Whether the crane is working; drives the beacons, machine lights and motor hum.</summary>
    [DataField, AutoNetworkedField]
    public bool Active;

    /// <summary>Name of the solid entity blocking the arm lane, or null when clear.</summary>
    [DataField, AutoNetworkedField]
    public string? Blocker;

    /// <summary>Whether a solid entity blocks the arm lane; a blocked crane does no work.</summary>
    public bool Blocked => Blocker != null;

    /// <summary>Longest boom: each extra section adds its length less one tile of overlap.</summary>
    public float MaxExtension => SectionLength + (Sections - 1) * (SectionLength - 1);

    /// <summary>Client-only animated arm length.</summary>
    [ViewVariables]
    public float VisualArm;

    /// <summary>Client-only animated carriage position.</summary>
    [ViewVariables]
    public float VisualPosition;

    /// <summary>Client-only animated tool head position.</summary>
    [ViewVariables]
    public float VisualTrolley;

    /// <summary>Client-only animated boom length.</summary>
    [ViewVariables]
    public float VisualExtension;

    /// <summary>Client-only animated boom angle in degrees.</summary>
    [ViewVariables]
    public float VisualAngle;

    /// <summary>Crane-local unit direction of the boom at its animated angle.</summary>
    public Vector2 VisualBoomDirection
    {
        get
        {
            var rad = VisualAngle * (MathF.PI / 180f);
            return ArmToLocal(MathF.Cos(rad), MathF.Sin(rad)) - ArmToLocal(0f, 0f);
        }
    }

    /// <summary>Local X of the arm: the body's first column, or its second when mirrored in place.</summary>
    public float ArmX => Mirrored && MirrorInPlace ? 1f : 0f;

    /// <summary>Converts an arm-frame point to crane-local coordinates.</summary>
    public Vector2 ArmToLocal(float across, float along)
    {
        return new Vector2(Mirrored ? ArmX - across : across, ArmRoot - along);
    }

    /// <summary>Converts a crane-local point to arm-frame coordinates, as (across, along).</summary>
    public Vector2 LocalToArm(Vector2 local)
    {
        return new Vector2(Mirrored ? ArmX - local.X : local.X, ArmRoot - local.Y);
    }
}
