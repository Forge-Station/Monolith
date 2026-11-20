namespace Content.Server._WF.WildSlimes;

/// <summary>When hurt, the mob digs in for a while: it takes less damage but barely moves.</summary>
[RegisterComponent]
public sealed partial class WFDigInComponent : Component
{
    /// <summary>How long it stays dug in after each hit.</summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(6);

    /// <summary>Damage taken while dug in is multiplied by this.</summary>
    [DataField]
    public float DamageCoefficient = 0.4f;

    /// <summary>Speed while dug in is multiplied by this.</summary>
    [DataField]
    public float SpeedModifier = 0.25f;

    /// <summary>Shown to those nearby as it digs in.</summary>
    [DataField]
    public LocId Popup = "wf-wild-slime-dig-in";

    [ViewVariables]
    public TimeSpan? DugInUntil;
}
