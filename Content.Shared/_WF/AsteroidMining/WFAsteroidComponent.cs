using System.Numerics;
using Content.Shared.Mining;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WF.AsteroidMining;

/// <summary>
/// A whole asteroid as one rock. A mining beam heats it inside its window until it cracks into smaller rocks and ore;
/// weapons can shatter it for a smaller yield.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFAsteroidComponent : Component
{
    /// <summary>Rock radius in metres; also the radius of the grid's tile disc.</summary>
    [DataField, AutoNetworkedField] public float Radius = 3f;

    [DataField] public float MinMass = 20f;
    [DataField] public float MaxMass = 30f;

    /// <summary>The asteroid grid each fragment spawns as, or null to crack straight into ore.</summary>
    [DataField] public EntProtoId? FragmentPrototype;
    [DataField] public int FragmentCount = 2;

    /// <summary>Share of the mass a clean crack passes on to fragments; the rest drops as ore.</summary>
    [DataField] public float FragmentShare = 0.5f;

    /// <summary>Haulable chunk that part of the ore drops as, for hand mining.</summary>
    [DataField] public EntProtoId? BoulderPrototype;

    /// <summary>Share of the dropped ore that comes out as boulders instead of loose ore.</summary>
    [DataField] public float BoulderShare = 0.25f;
    [DataField] public int MaxBoulders = 6;

    /// <summary>Weapon damage that shatters the rock without a beam.</summary>
    [DataField] public float ShatterDamage = 18000f;

    /// <summary>Share of the mass a weapon shatter keeps as ore.</summary>
    [DataField] public float ShatterYield = 0.1f;

    /// <summary>Share of the mass an overcharge blast keeps as ore and fragments.</summary>
    [DataField] public float OverchargeYield = 0.35f;

    /// <summary>Ore each vein of the rock drops, relative to a vein rock of the same ore.</summary>
    [DataField] public float OreYield = 1f;

    /// <summary>Seconds in the window to crack a massless rock, plus <see cref="CrackTimePerMass"/> per unit.</summary>
    [DataField] public float CrackTimeBase = 1.5f;
    [DataField] public float CrackTimePerMass = 0.035f;

    /// <summary>Added to the composition's resistance; bigger rocks resist more.</summary>
    [DataField] public float ResistanceBonus;

    /// <summary>Scales the composition's resistance, before the bonus; boulders take none of it.</summary>
    [DataField] public float ResistanceScale = 1f;

    /// <summary>A fixed cracking window in place of a rolled one, for loose chunks any warm beam breaks.</summary>
    [DataField] public Vector2? FixedWindow;

    /// <summary>Number of sprite variants in the rock's RSI.</summary>
    [DataField] public int Variants = 3;

    /// <summary>Irregular silhouettes by sprite variant; the rest are round.</summary>
    [DataField] public Dictionary<int, WFAsteroidShape> Shapes = new();

    /// <summary>Alternative RSIs by look, such as metallic, with the same states and silhouettes.</summary>
    [DataField] public Dictionary<string, ResPath> Looks = new();

    // What the rock rolled is saved, so a boulder aboard a saved ship keeps its ore and look.
    [DataField, AutoNetworkedField] public int Variant;

    /// <summary>The composition's look, picking an RSI from <see cref="Looks"/>.</summary>
    [DataField, AutoNetworkedField] public string? Look;
    [DataField, AutoNetworkedField] public Color VeinColor = Color.White;

    /// <summary>Tint of the rock body, from its composition.</summary>
    [DataField, AutoNetworkedField] public Color RockColor = Color.White;

    /// <summary>Displayed heat; <see cref="WFAsteroidHeat.OverchargeLine"/> is where it starts to overcharge.</summary>
    [AutoNetworkedField] public float Charge;
    [AutoNetworkedField] public bool Overcharging;

    /// <summary>Crack progress from zero to one, for the rock's shake as it nears breaking.</summary>
    [AutoNetworkedField] public float Progress;

    /// <summary>Client-side heat, eased toward <see cref="Charge"/>.</summary>
    public float DisplayCharge;

    /// <summary>The look and variant the client last set the sprite to.</summary>
    public string? AppliedLook;
    public int AppliedVariant = -1;

    // Composition, resistance and window are networked so the cockpit scanner can read a rock in view.
    /// <summary>Ore veins this rock holds, inert rock included.</summary>
    [DataField, AutoNetworkedField] public float Mass;
    [DataField] public ProtoId<WFAsteroidCompositionPrototype>? CompositionId;
    [DataField, AutoNetworkedField] public List<WFAsteroidOre> Ores = new();
    [DataField, AutoNetworkedField] public float Resistance;
    /// <summary>The composition's rolled resistance, before this rock's scale and bonus; fragments inherit it.</summary>
    [DataField] public float BaseResistance;
    [DataField, AutoNetworkedField] public float Instability;
    [DataField, AutoNetworkedField] public float WindowCenter = 0.55f;
    [DataField, AutoNetworkedField] public float WindowHalfWidth = 0.1f;
    public float BaseCharge;
    /// <summary>The progress last sent to clients, so it is only resent as it moves.</summary>
    public float SentProgress;
    /// <summary>The part of <see cref="Progress"/> made in the hot band, which costs ore.</summary>
    public float HotProgress;
    public float OverchargeTime;
    public float IncomingPower;
    /// <summary>Summed crack rate of the beams on the rock this tick.</summary>
    public float CrackRate;
    /// <summary>Rolled mass scale of the rock's silhouette.</summary>
    [DataField] public float MassScale = 1f;
    [DataField] public float NoiseSeed;
    public bool Cracking;
}

/// <summary>One ore in a rock and the share of its mass it makes up.</summary>
[DataRecord, Serializable, NetSerializable]
public partial record struct WFAsteroidOre(ProtoId<OrePrototype> Ore, float Fraction, Color Color, float Volatility);

[Serializable, NetSerializable]
public enum WFAsteroidVisualLayers : byte
{
    Rock,
    Veins,
    Heat,
}
