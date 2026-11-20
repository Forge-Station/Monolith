using Content.Shared._HL.Silicons.Synths.Battery;

namespace Content.Server._HL.Silicons.Synths.Battery;

public sealed partial class SynthBatteryAlertSystem : EntitySystem
{
    [Dependency] private readonly SharedSynthBatteryAlertSystem _alerts = default!;
    [Dependency] private readonly SynthBatterySystem _synthBattery = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SynthBatteryComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SynthBatteryComponent, SynthBatteryAlertUpdateRequestEvent>(OnUpdateRequest);
    }

    private void OnMapInit(Entity<SynthBatteryComponent> ent, ref MapInitEvent args)
    {
        UpdateBatteryAlert(ent);
    }

    public void UpdateBatteryAlert(Entity<SynthBatteryComponent> ent)
    {
        if (!_synthBattery.TryGetBattery(ent.Owner, out var battery, ent.Comp))
        {
            _alerts.ShowNoBatteryAlert(ent);
            return;
        }

        _alerts.ShowBatteryAlert(ent, battery.Value.Comp.CurrentCharge, battery.Value.Comp.MaxCharge);
    }

    private void OnUpdateRequest(Entity<SynthBatteryComponent> ent, ref SynthBatteryAlertUpdateRequestEvent args)
    {
        UpdateBatteryAlert(ent);
    }
}
