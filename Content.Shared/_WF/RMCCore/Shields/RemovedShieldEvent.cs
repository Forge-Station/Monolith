namespace Content.Shared._WF.RMCCore.Shields;

[ByRefEvent]
public readonly record struct RemovedShieldEvent(XenoShieldSystem.ShieldType Type);
