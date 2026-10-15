namespace Content.Shared._Forge.KIAS;

[RegisterComponent]
public sealed partial class KiasLightFixtureComponent : Component;

[RegisterComponent]
public sealed partial class KiasLightGroupComponent : Component
{
    [DataField]
    public string Group = "CABIN";

    public EntityUid? IndexedGrid;
}

[RegisterComponent]
public sealed partial class KiasLightControllerComponent : Component
{
    [DataField]
    public string Group = "CABIN";
    [DataField] public string Color = "#FFFFFF";
    [DataField] public float Brightness = 0.8f;
}

[RegisterComponent]
public sealed partial class KiasSuppressionComponent : Component;

[RegisterComponent]
public sealed partial class KiasSuppressionCartridgeComponent : Component;

[ByRefEvent]
public readonly record struct KiasSetLightGroupEvent(EntityUid Grid, string Group, bool Enabled);
