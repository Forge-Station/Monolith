namespace Content.Shared._WF.RMCCore.StatusEffect;

[ByRefEvent]
public record struct RMCStatusEffectTimeEvent(string Key, TimeSpan Duration);
