using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.FtlEffects;

/// <summary>Replicates the departure ripple's clock without changing shuttle movement.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause, UnsavedComponent]
public sealed partial class FtlDepartureComponent : Component
{
    /// <summary>Beginning of the drive's spool-up.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan Started;

    /// <summary>Scheduled departure, replaced with the actual time when the ship enters hyperspace.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan Departure;

    /// <summary>Whether the ship has crossed into hyperspace.</summary>
    [DataField, AutoNetworkedField]
    public bool Entered;

    /// <summary>Whether this is the snap out of hyperspace at the destination.</summary>
    [DataField, AutoNetworkedField]
    public bool Arriving;

    /// <summary>The jumping ship this docked grid rides with; null on that ship itself.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Lead;
}
