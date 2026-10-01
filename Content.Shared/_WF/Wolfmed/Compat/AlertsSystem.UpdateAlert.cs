using Robust.Shared.Prototypes;

namespace Content.Shared.Alert;

/// <summary>Port of Onyx's AlertsSystem.UpdateAlert: keeps the cooldown bar's start time when the end time shrinks.</summary>
public abstract partial class AlertsSystem
{
    /// <summary>Shows an alert, preserving an existing cooldown's start time when the new end time is not longer.</summary>
    public void UpdateAlert(EntityUid euid,
        ProtoId<AlertPrototype> alertType,
        short? severity = null,
        TimeSpan? cooldown = null,
        bool autoRemove = false,
        bool showCooldown = true)
    {
        if (_timing.ApplyingState)
            return;

        if (!TryGet(alertType, out var alert))
            return;

        if (cooldown == null)
        {
            ShowAlert(euid, alertType, severity, null, autoRemove, showCooldown);
            return;
        }

        TryGetAlertState(euid, alert.AlertKey, out var alertState);
        var start = alertState.Cooldown is { } existing && existing.Item2 >= cooldown.Value
            ? existing.Item1
            : _timing.CurTime;

        ShowAlert(euid, alertType, severity, (start, cooldown.Value), autoRemove, showCooldown);
    }
}
