using Content.Shared.Atmos.Components;
using Content.Shared.RCD.Systems;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Content.Shared.RCD.Components;

/// <summary>
/// Main component for the RCD
/// Optionally uses LimitedChargesComponent.
/// Charges can be refilled with RCD ammo
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(RCDSystem))]
public sealed partial class RCDComponent : Component
{
    /// <summary>
    /// List of RCD prototypes that the device comes loaded with
    /// </summary>
    [DataField, AutoNetworkedField]
    public HashSet<ProtoId<RCDPrototype>> AvailablePrototypes { get; set; } = new();

    /// <summary>
    /// Sound that plays when a RCD operation successfully completes
    /// </summary>
    [DataField]
    public SoundSpecifier SuccessSound { get; set; } = new SoundPathSpecifier("/Audio/Items/deconstruct.ogg");

    /// <summary>
    /// The ProtoId of the currently selected RCD prototype
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<RCDPrototype> ProtoId { get; set; } = "Invalid";

    #region Forge-Change

    /// <summary>
    /// Indicates if a mirrored version of the construction prototype should be used (if available)
    /// </summary>
    [AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public bool UseMirrorPrototype;

    /// <summary>
    /// Indicates whether this is an RCD or an RPD
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsRpd;

    /// <summary>
    /// Layer-specific entity prototype selected for the next RPD construction
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntProtoId? ConstructionEntity;

    /// <summary>
    /// Eye rotation used when resolving pipe layer from cursor position
    /// </summary>
    [DataField, AutoNetworkedField]
    public Angle PipeLayerEyeRotation;

    /// <summary>
    /// Pipe layer (primary / secondary / tertiary) for the next RPD construction
    /// </summary>
    [DataField, AutoNetworkedField]
    public AtmosPipeLayer ConstructionPipeLayer = AtmosPipeLayer.Primary;

    #endregion

    /// <summary>
    /// A cached copy of currently selected RCD prototype
    /// </summary>
    /// <remarks>
    /// If the ProtoId is changed, make sure to update the CachedPrototype as well
    /// </remarks>
    [ViewVariables(VVAccess.ReadOnly)]
    public RCDPrototype CachedPrototype { get; set; } = default!;

    /// <summary>
    /// The direction constructed entities will face upon spawning
    /// </summary>
    [DataField, AutoNetworkedField]
    public Direction ConstructionDirection
    {
        get
        {
            return _constructionDirection;
        }
        set
        {
            _constructionDirection = value;
            ConstructionTransform = new Transform(new(), _constructionDirection.ToAngle());
        }
    }

    private Direction _constructionDirection = Direction.South;

    /// <summary>
    /// Returns a rotated transform based on the specified ConstructionDirection
    /// </summary>
    /// <remarks>
    /// Contains no position data
    /// </remarks>
    [ViewVariables(VVAccess.ReadOnly)]
    public Transform ConstructionTransform { get; private set; } = default!;

    /// <summary>
    /// Mono - delay multiplier for the RCD
    /// </summary>
    [DataField, AutoNetworkedField]
    public float DelayMultiplier = 1f;
}
