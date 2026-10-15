namespace Content.Shared._Forge.KIAS;

[RegisterComponent]
public sealed partial class KiasIffReceiverComponent : Component;

[RegisterComponent]
public sealed partial class KiasDockingSensorComponent : Component;

[RegisterComponent]
public sealed partial class KiasDeviceAdapterComponent : Component
{
    [DataField] public bool State;
    [DataField] public bool Emergency;
}

[RegisterComponent]
public sealed partial class KiasWirelessComponent : Component
{
    [DataField] public string Channel = "KIAS";
    [DataField] public float Range = 200;
    [DataField] public List<EntityUid> TrustedTransmitters = new();
}

[RegisterComponent]
public sealed partial class KiasKeySwitchComponent : Component;

[RegisterComponent]
public sealed partial class KiasIntegrationKitComponent : Component;

[RegisterComponent]
public sealed partial class KiasIntegratedComponent : Component
{
    [DataField] public EntityUid? Scanner;
    [DataField] public bool Direct = true;
}

[RegisterComponent]
public sealed partial class KiasMaydayAntennaComponent : Component;

[RegisterComponent]
public sealed partial class KiasDecoyLauncherComponent : Component;

[RegisterComponent]
public sealed partial class KiasRotaryComponent : Component
{
    [DataField] public int Position;
    [DataField] public int Positions = 4;
    [DataField] public List<int> Signals = new() { 0, 1, 2, 3 };
}

[RegisterComponent]
public sealed partial class KiasResourceMonitorComponent : Component
{
    [DataField] public List<EntityUid> Targets = new();
}
