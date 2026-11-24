namespace Content.Server._WF.Chimera;

/// <summary>
/// A body that just fell out of a dead fleshbeast. Letoferol can't turn it again until <see cref="ReadyAt"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class ChimeraTurnCooldownComponent : Component
{
    /// <summary>When Letoferol can turn the body again.</summary>
    [DataField, AutoPausedField]
    public TimeSpan ReadyAt;
}
