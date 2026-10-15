namespace Content.Shared._Forge.KIAS;

[RegisterComponent]
public sealed partial class KiasHorizonComponent : Component
{
    [DataField]
    public float Range = 2000f;
}

public enum KiasContactDisposition : byte { Unknown, Friendly, Neutral, Hostile }

[ByRefEvent]
public readonly record struct KiasBluespaceDisturbanceEvent(EntityUid Grid, EntityUid Contact, float Distance, double Bearing, KiasContactDisposition Disposition);

[ByRefEvent]
public readonly record struct KiasAutopilotArrivedEvent(EntityUid Grid, EntityUid Console);
