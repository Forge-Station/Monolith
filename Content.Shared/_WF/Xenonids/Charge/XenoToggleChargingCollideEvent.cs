namespace Content.Shared._WF.Xenonids.Charge;

[ByRefEvent]
public record struct XenoToggleChargingCollideEvent(Entity<ActiveXenoToggleChargingComponent> Charger, bool Handled = false);
