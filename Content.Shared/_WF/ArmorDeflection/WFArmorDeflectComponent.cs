using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.ArmorDeflection;

/// <summary>
/// Worn armour that light rounds ricochet off: a projectile whose damage of one type is at or under the threshold
/// bounces away unharmed and can hit someone else. Armour-piercing rounds always get through.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFArmorDeflectComponent : Component
{
    /// <summary>Damage type that decides whether a round is light enough to bounce.</summary>
    [DataField]
    public ProtoId<DamageTypePrototype> DamageType = "Piercing";

    /// <summary>Highest effective damage of that type, per projectile, that ricochets.</summary>
    [DataField]
    public FixedPoint2 Threshold = 24;

    /// <summary>A round with at least this much armour penetration always gets through.</summary>
    [DataField]
    public float PenetratingArmorPenetration = 0.3f;

    /// <summary>Spread of the ricochet around the direction the round came from.</summary>
    [DataField]
    public Angle Spread = Angle.FromDegrees(120);

    [DataField]
    public SoundSpecifier? Sound = new SoundCollectionSpecifier("WFRicochet");
}

/// <summary>On a wearer of <see cref="WFArmorDeflectComponent"/> armour, so projectiles that hit them can ricochet.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFArmorDeflectWearerComponent : Component;
