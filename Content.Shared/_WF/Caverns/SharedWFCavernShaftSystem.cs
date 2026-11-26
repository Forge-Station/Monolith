using Content.Shared.Examine;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Caverns;

/// <summary>Tells an examiner where a shaft goes, what its air is like and how hard the landing is.</summary>
public sealed partial class SharedWFCavernShaftSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;

    /// <summary>Below this landing multiplier the bottom reads as soft; at zero it is water.</summary>
    public const float SoftLandingBelow = 0.6f;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFCavernShaftComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<WFCavernShaftComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Cavern is not { } cavernId || !_proto.TryIndex(cavernId, out var cavern))
            return;

        using (args.PushGroup(nameof(WFCavernShaftComponent)))
        {
            args.PushMarkup(Loc.GetString("wf-cavern-shaft-examine", ("cavern", Loc.GetString(cavern.Name))));
            args.PushMarkup(Loc.GetString(AirLine(ent.Comp.Air)));
            args.PushMarkup(Loc.GetString(LandingLine(ent.Comp.LandingMultiplier)));
        }
    }

    /// <summary>The examine key for a shaft's air.</summary>
    public static string AirLine(WFCavernAir air)
    {
        return air switch
        {
            WFCavernAir.Foul => "wf-cavern-shaft-air-foul",
            WFCavernAir.Thin => "wf-cavern-shaft-air-thin",
            WFCavernAir.Toxic => "wf-cavern-shaft-air-toxic",
            WFCavernAir.Scalding => "wf-cavern-shaft-air-scalding",
            WFCavernAir.Freezing => "wf-cavern-shaft-air-freezing",
            _ => "wf-cavern-shaft-air-breathable",
        };
    }

    /// <summary>The examine key for a shaft's landing: water at zero, soft below 0.6, hard otherwise.</summary>
    public static string LandingLine(float multiplier)
    {
        if (multiplier <= 0f)
            return "wf-cavern-shaft-landing-water";

        return multiplier < SoftLandingBelow ? "wf-cavern-shaft-landing-soft" : "wf-cavern-shaft-landing-hard";
    }
}
