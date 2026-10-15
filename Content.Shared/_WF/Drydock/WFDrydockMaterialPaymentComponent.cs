using Content.Shared.Materials;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Drydock;

/// <summary>
/// A drydock console that charges materials from its cranes' storage and their linked silos instead of credits.
/// </summary>
[RegisterComponent]
public sealed partial class WFDrydockMaterialPaymentComponent : Component
{
    /// <summary>Material units charged per credit of work that has no material bill of its own.</summary>
    [DataField]
    public Dictionary<ProtoId<MaterialPrototype>, float> Rates = new();

    /// <summary>
    /// Share of each construction material the rates don't charge, billed in kind on top of the rates to rebuild an
    /// entity (and that share of it to patch one), so a plastitanium wall takes a little plastitanium.
    /// </summary>
    [DataField]
    public float InKindFactor = 0.01f;

    /// <summary>
    /// Most units of each material one repair of a <see cref="ReferenceValue"/> ship may bill, scaled by the ship's
    /// price. Refits are not limited.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<MaterialPrototype>, int> MaxBill = new();

    /// <summary>Ship price the <see cref="MaxBill"/> limits are set for.</summary>
    [DataField]
    public int ReferenceValue = 250000;

    /// <summary>Most units one repair may bill of a material <see cref="MaxBill"/> doesn't list.</summary>
    [DataField]
    public int DefaultMaxBill = 500;

    /// <summary>
    /// Share of every construction material a hull break recovers from what it strips, unless <see cref="BreakYield"/>
    /// lists the material. With neither set, the console can't break hulls.
    /// </summary>
    [DataField]
    public float BreakDefaultYield;

    /// <summary>Share a hull break recovers of particular construction materials, in place of the default.</summary>
    [DataField]
    public Dictionary<ProtoId<MaterialPrototype>, float> BreakYield = new();

    /// <summary>Material units a hull break recovers from each tile it lifts.</summary>
    [DataField]
    public Dictionary<ProtoId<MaterialPrototype>, int> BreakTileYield = new();

    /// <summary>Material units a hull break pays per tile it lifts on top of the hull's own, such as scrap; rebuilding the tile never bills them.</summary>
    [DataField]
    public Dictionary<ProtoId<MaterialPrototype>, int> BreakTileSalvage = new();

    /// <summary>
    /// Most units of each material a hull break may pay per tile it lifts. Yield past the caps of the tiles lifted so far
    /// waits for later tiles, so a hull broken whole or in parts pays the same. A material not listed has no cap.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<MaterialPrototype>, float> BreakCap = new();
}
