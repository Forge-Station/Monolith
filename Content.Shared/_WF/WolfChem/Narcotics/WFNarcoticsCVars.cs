using Robust.Shared.Configuration;

namespace Content.Shared._WF.WolfChem.Narcotics;

/// <summary>Narcotics market settings: seller heat, tips, warrants and the stock signature. Values are credits of street value.</summary>
[CVarDefs]
public sealed class WFNarcoticsCVars
{
    /// <summary>Heat up to which a seller is paid in full: the most one sale is worth to a seller who has cooled off.</summary>
    public static readonly CVarDef<float> HeatFree =
        CVarDef.Create("wf.narcotics.heat_free", 500000f, CVar.SERVERONLY);

    /// <summary>
    /// How far a seller's own line strays from <see cref="HeatFree"/> either way, as a share of it; 0 gives everyone
    /// the same line. Nobody is shown theirs: the buyer's manner and the console's price are the only read.
    /// </summary>
    public static readonly CVarDef<float> HeatFuzz =
        CVarDef.Create("wf.narcotics.heat_fuzz", 0.15f, CVar.SERVERONLY);

    /// <summary>Seconds a seller keeps from selling before their line is drawn anew.</summary>
    public static readonly CVarDef<float> HeatFuzzTime =
        CVarDef.Create("wf.narcotics.heat_fuzz_time", 600f, CVar.SERVERONLY);

    /// <summary>
    /// Heat above <see cref="HeatFree"/> over which the price falls by a factor of e. It is wide on purpose: selling
    /// past the line still pays, and what it costs is the tip and the warrant.
    /// </summary>
    public static readonly CVarDef<float> HeatScale =
        CVarDef.Create("wf.narcotics.heat_scale", 250000f, CVar.SERVERONLY);

    /// <summary>The least share of its value a buyer pays below <see cref="HeatCap"/>.</summary>
    public static readonly CVarDef<float> HeatFloor =
        CVarDef.Create("wf.narcotics.heat_floor", 0.15f, CVar.SERVERONLY);

    /// <summary>
    /// Heat lost each second, which is also the most a seller earns at full price over time. With
    /// <see cref="HeatFree"/> at 500,000, 150 is a full sale about every 55 minutes and some 3.5M over a six-hour round
    /// that starts with half an hour of cooking.
    /// </summary>
    public static readonly CVarDef<float> HeatCooling =
        CVarDef.Create("wf.narcotics.heat_cooling", 150f, CVar.SERVERONLY);

    /// <summary>
    /// The most heat a seller holds, and the heat past which the market is saturated and a buyer pays only
    /// <see cref="HeatGlutFloor"/>. The longest wait back to full price is this over the cooling.
    /// </summary>
    public static readonly CVarDef<float> HeatCap =
        CVarDef.Create("wf.narcotics.heat_cap", 1000000f, CVar.SERVERONLY);

    /// <summary>
    /// The share of its value a buyer pays for what is sold past <see cref="HeatCap"/>. At 0.02 a cold dump of 145M
    /// street value fetches about 3.6M, near what careful selling earns over a whole round.
    /// </summary>
    public static readonly CVarDef<float> HeatGlutFloor =
        CVarDef.Create("wf.narcotics.heat_glut_floor", 0.02f, CVar.SERVERONLY);

    /// <summary>Heat past which a tip describes the seller and customs puts a warrant out on them.</summary>
    public static readonly CVarDef<float> HeatHot =
        CVarDef.Create("wf.narcotics.heat_hot", 650000f, CVar.SERVERONLY);

    /// <summary>Seconds before security hears of the same seller again, unless they have grown hotter.</summary>
    public static readonly CVarDef<float> TipCooldown =
        CVarDef.Create("wf.narcotics.tip_cooldown", 180f, CVar.SERVERONLY);

    /// <summary>
    /// The share of what a seller sells past <see cref="HeatHot"/> that goes onto their warrant; 0 posts none. The
    /// warrant pays out, so keep it under what the buyer holds back there (45% at the hot line with these defaults), or
    /// a seller and a friend can farm it.
    /// </summary>
    public static readonly CVarDef<float> WarrantShare =
        CVarDef.Create("wf.narcotics.warrant_share", 0.4f, CVar.SERVERONLY);

    /// <summary>The most selling puts on a seller's warrant.</summary>
    public static readonly CVarDef<float> WarrantCap =
        CVarDef.Create("wf.narcotics.warrant_cap", 300000f, CVar.SERVERONLY);

    /// <summary>Street value aboard a ship at which its stock shows on radar; 0 turns the signature off.</summary>
    public static readonly CVarDef<float> StockSignature =
        CVarDef.Create("wf.narcotics.stock_signature", 200000f, CVar.SERVERONLY);

    /// <summary>
    /// Street value that adds one point to a narcotic's contraband hand-in reward; 0 leaves the flat reward. A point
    /// cashes for 2,000, so 15,000 pays about 13% of the value: keep it under <see cref="HeatFloor"/>, or handing in
    /// beats selling hot.
    /// </summary>
    public static readonly CVarDef<float> TurnInValue =
        CVarDef.Create("wf.narcotics.turn_in_value", 15000f, CVar.SERVERONLY);
}
