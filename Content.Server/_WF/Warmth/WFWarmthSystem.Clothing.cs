using Content.Server.Temperature.Components;
using Content.Shared.Examine;
using Content.Shared.Temperature;

namespace Content.Server._WF.Warmth;

public sealed partial class WFWarmthSystem
{
    /// <summary>
    /// Kelvin an unclothed human's skin settles above still, cold air once it shivers at full effort (measured in
    /// WarmthTest: 257 K air holds the skin near 278 K). Clothing that lets through a share c of the heat stretches
    /// this to NakedMargin / c.
    /// </summary>
    public const float NakedMargin = 21f;

    /// <summary>The body temperature where a human's cold warnings start: 10 K under normal.</summary>
    public const float WarningSkin = 300f;

    /// <summary>Colder than this, the examine text gives no number.</summary>
    private const float DeepColdCelsius = -100f;

    private void InitializeClothing()
    {
        SubscribeLocalEvent<TemperatureProtectionComponent, ExaminedEvent>(OnClothingExamined);
    }

    /// <summary>
    /// The coldest still air this alone keeps an otherwise unclothed human out of the cold warnings in: the skin then
    /// settles at the warning line. Wind and wet clothes lower it.
    /// </summary>
    public static float WarmToCelsius(float coolingCoefficient)
    {
        var share = MathF.Max(coolingCoefficient, 0.0001f);
        return TemperatureHelpers.KelvinToCelsius(WarningSkin - NakedMargin / share);
    }

    private void OnClothingExamined(Entity<TemperatureProtectionComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var cooling = ent.Comp.CoolingCoefficient;
        if (cooling < 0.95f)
        {
            var celsius = WarmToCelsius(cooling);
            args.PushMarkup(celsius <= DeepColdCelsius
                ? Loc.GetString("wf-warmth-clothing-deep-cold")
                : Loc.GetString("wf-warmth-clothing-warm-to", ("celsius", (int) (MathF.Round(celsius / 5f) * 5f))));
        }

        var heating = ent.Comp.HeatingCoefficient;
        if (heating <= 0.5f)
            args.PushMarkup(Loc.GetString("wf-warmth-clothing-heat-shield"));
        else if (heating > 1f)
            args.PushMarkup(Loc.GetString("wf-warmth-clothing-heat-trap"));
    }
}
