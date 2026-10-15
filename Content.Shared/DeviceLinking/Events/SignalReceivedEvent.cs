using Content.Shared.DeviceNetwork;

namespace Content.Shared.DeviceLinking.Events;

[ByRefEvent]
// #Forge-change START (KIAS): Получатель знает порт источника без дублирования маршрутизации DeviceLink.
public readonly record struct SignalReceivedEvent(string Port, EntityUid? Trigger = null, NetworkPayload? Data = null, string? SourcePort = null);
// #Forge-change END (KIAS)
