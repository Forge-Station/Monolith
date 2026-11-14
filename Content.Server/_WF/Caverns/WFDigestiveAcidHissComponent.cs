namespace Content.Server._WF.Caverns;

/// <summary>On whoever digestive acid is burning: the hiss looping on them while it does.</summary>
[RegisterComponent, Access(typeof(WFDigestiveAcidHissSystem))]
public sealed partial class WFDigestiveAcidHissComponent : Component
{
    /// <summary>The looping stream attached to the victim.</summary>
    [ViewVariables]
    public EntityUid? Stream;
}
