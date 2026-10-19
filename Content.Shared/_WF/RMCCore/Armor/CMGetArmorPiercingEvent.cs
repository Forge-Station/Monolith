namespace Content.Shared._WF.RMCCore.Armor;

[ByRefEvent]
public record struct CMGetArmorPiercingEvent(EntityUid Target, int Piercing = 0, bool IgnoreXenoArmor = false);
