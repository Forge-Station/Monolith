// #Forge-change START (KIAS): Штатный DeviceLink публикует фактически вызванный порт для адаптеров.
using Content.Shared.DeviceNetwork;
using Robust.Shared.GameObjects;

namespace Content.Shared.DeviceLinking.Events;

[ByRefEvent]
public readonly record struct DeviceLinkPortInvokedEvent(string Port, NetworkPayload? Data);
// #Forge-change END (KIAS)
