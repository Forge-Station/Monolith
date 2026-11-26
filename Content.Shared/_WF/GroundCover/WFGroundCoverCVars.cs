using Robust.Shared.Configuration;

namespace Content.Shared._WF.GroundCover;

/// <summary>Look of wet ground and puddles, set on the server and sent to every client so admins can tune it live.</summary>
[CVarDefs]
public sealed class WFGroundCoverCVars
{
    /// <summary>How much puddle water tints and darkens the ground under it, 0 (clear) to 1.</summary>
    public static readonly CVarDef<float> PuddleOpacity =
        CVarDef.Create("wf.ground_cover.puddle_opacity", 0.5f, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>How far in from a puddle's rim its water fades up to full, in tiles.</summary>
    public static readonly CVarDef<float> PuddleEdge =
        CVarDef.Create("wf.ground_cover.puddle_edge", 0.6f, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>Scales the puddles' water level: below 1 fewer, smaller puddles, above 1 more and larger.</summary>
    public static readonly CVarDef<float> PuddleCoverage =
        CVarDef.Create("wf.ground_cover.puddle_coverage", 1f, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>Scales how strongly puddles reflect the sky and what stands above them.</summary>
    public static readonly CVarDef<float> Reflection =
        CVarDef.Create("wf.ground_cover.reflection", 1f, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>Scales glints, twinkling specks and lit ripple crests.</summary>
    public static readonly CVarDef<float> Sparkle =
        CVarDef.Create("wf.ground_cover.sparkle", 1f, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>Scales how much rain darkens and saturates open ground.</summary>
    public static readonly CVarDef<float> WetDarkening =
        CVarDef.Create("wf.ground_cover.wet_darkening", 1f, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);
}
