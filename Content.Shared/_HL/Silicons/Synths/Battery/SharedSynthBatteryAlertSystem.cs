using Content.Shared._HL.UI;
using Content.Shared.Alert;
using Content.Shared.Mobs;

namespace Content.Shared._HL.Silicons.Synths.Battery;

public sealed partial class SharedSynthBatteryAlertSystem : EntitySystem
{
    [Dependency] private readonly AlertsSystem _alerts = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SynthBatteryComponent, SynthBatteryShutdownEvent>(OnShutdown);
        SubscribeLocalEvent<SynthBatteryComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    public void ShowBatteryAlert(Entity<SynthBatteryComponent> ent, float currentCharge, float maxCharge)
    {
        if (!TryComp<BatteryAlertComponent>(ent.Owner, out var alert))
            return;

        var chargePercent = GetChargeSeverity(currentCharge, maxCharge);

        _alerts.ClearAlert(ent.Owner, alert.NoBatteryAlert);
        _alerts.ShowAlert(ent.Owner, alert.BatteryAlert, chargePercent);
    }

    public void ShowNoBatteryAlert(Entity<SynthBatteryComponent> ent)
    {
        if (!TryComp<BatteryAlertComponent>(ent.Owner, out var alert))
            return;

        _alerts.ClearAlert(ent.Owner, alert.BatteryAlert);
        _alerts.ShowAlert(ent.Owner, alert.NoBatteryAlert);
    }

    public void ClearBatteryAlerts(Entity<SynthBatteryComponent> ent)
    {
        if (!TryComp<BatteryAlertComponent>(ent.Owner, out var alert))
            return;

        _alerts.ClearAlert(ent.Owner, alert.BatteryAlert);
        _alerts.ClearAlert(ent.Owner, alert.NoBatteryAlert);
    }

    private static short GetChargeSeverity(float currentCharge, float maxCharge)
    {
        if (maxCharge <= 0)
            return 0;

        var chargePercent = (short) MathF.Round(currentCharge / maxCharge * 10f);

        if (chargePercent == 0 && currentCharge > 0f)
            chargePercent = 1;

        return (short) Math.Clamp((int) chargePercent, 0, 10);
    }

    private void OnShutdown(Entity<SynthBatteryComponent> ent, ref SynthBatteryShutdownEvent args)
    {
        ClearBatteryAlerts(ent);
    }

    private void OnMobStateChanged(Entity<SynthBatteryComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
        {
            ClearBatteryAlerts(ent);
            return;
        }

        var ev = new SynthBatteryAlertUpdateRequestEvent();
        RaiseLocalEvent(ent.Owner, ref ev);
    }
}

[ByRefEvent]
public readonly record struct SynthBatteryAlertUpdateRequestEvent;
