using Content.Shared.FixedPoint;
using Content.Shared.Speech; // Forge-Change
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Forge.Cybernetics.Components;

[RegisterComponent]
public sealed partial class ForgeSubdermalArmorComponent : Component
{
    [DataField]
    public float MechanicalMultiplier = 1f;

    [DataField]
    public FixedPoint2 MechanicalFlat;

    [DataField]
    public float HeatMultiplier = 1f;

    [DataField]
    public FixedPoint2 HeatFlat;

    [DataField]
    public float ColdMultiplier = 1f;

    [DataField]
    public FixedPoint2 ColdFlat;
}

[RegisterComponent]
public sealed partial class ForgeEmpDamageComponent : Component
{
    [DataField(required: true)]
    public string DamageType = "Blunt";

    [DataField(required: true)]
    public FixedPoint2 Amount;
}

[RegisterComponent]
public sealed partial class ForgeSurgicalArmorImplantComponent : Component;

[RegisterComponent]
public sealed partial class ForgeUnarmedDamageComponent : Component
{
    [DataField]
    public string DamageType = "Blunt";

    [DataField]
    public FixedPoint2 Amount;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan DisabledUntil;
}

[RegisterComponent]
public sealed partial class ForgeInjectOnUnarmedHitComponent : Component
{
    [DataField]
    public string DamageType = "Poison";

    [DataField]
    public FixedPoint2 Amount = FixedPoint2.New(2);
}

[RegisterComponent]
public sealed partial class ForgeBloodstreamCleanerComponent : Component
{
    [DataField]
    public FixedPoint2 Amount = FixedPoint2.New(1);

    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    [DataField]
    public HashSet<string> MetabolismGroups = new() { "Poison", "Toxins" };

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextUpdate;
}

[RegisterComponent]
public sealed partial class ForgeRadiationCleanerComponent : Component
{
    [DataField]
    public FixedPoint2 Amount = FixedPoint2.New(0.5);

    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextUpdate;
}

[RegisterComponent]
public sealed partial class ForgeVoiceModuleImplantComponent : Component
{
    [DataField]
    public string VoiceId = "male1";

    [DataField]
    public ProtoId<SpeechVerbPrototype>? SpeechVerbId; // Forge-Change

    [DataField]
    public EntityUid? AppliedEntity;
}

[RegisterComponent]
public sealed partial class ForgeVoiceOverrideComponent : Component
{
    [DataField]
    public string VoiceId = "male1";

    [DataField]
    public ProtoId<SpeechVerbPrototype>? SpeechVerbId; // Forge-Change

    [DataField]
    public ProtoId<SpeechVerbPrototype>? OriginalSpeechVerbId;
}
