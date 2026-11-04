using Content.Shared.Movement.Systems;
using Robust.Shared.Configuration;

namespace Content.Shared._WF.Warmth;

/// <summary>The slowdown the cold and heat warnings bring, and how a body feel reads and colours.</summary>
public abstract class SharedWFWarmthSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    protected IConfigurationManager Cfg => _cfg;
    protected MovementSpeedModifierSystem Movement => _movement;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFWarmthComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnRefreshSpeed(Entity<WFWarmthComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        var modifier = ent.Comp.Strain switch
        {
            WFWarmthStrain.Cold => _cfg.GetCVar(WFWarmthCVars.ColdSlowdown),
            WFWarmthStrain.Hot => _cfg.GetCVar(WFWarmthCVars.HeatSlowdown),
            _ => 1f,
        };

        if (modifier < 1f)
            args.ModifySpeed(modifier, modifier);
    }

    /// <summary>The Fluent id for a body feel.</summary>
    public static string FeelLoc(WFBodyFeel feel) => feel switch
    {
        WFBodyFeel.Freezing => "wf-warmth-feel-freezing",
        WFBodyFeel.Cold => "wf-warmth-feel-cold",
        WFBodyFeel.Chilly => "wf-warmth-feel-chilly",
        WFBodyFeel.Warm => "wf-warmth-feel-warm",
        WFBodyFeel.Hot => "wf-warmth-feel-hot",
        WFBodyFeel.Overheating => "wf-warmth-feel-overheating",
        _ => "wf-warmth-feel-comfortable",
    };

    /// <summary>A body feel's colour by danger: green comfortable, pale mild, amber warning, red harmful.</summary>
    public static Color FeelColor(WFBodyFeel feel) => feel switch
    {
        WFBodyFeel.Freezing or WFBodyFeel.Overheating => Color.FromHex("#FF5A5A"),
        WFBodyFeel.Cold or WFBodyFeel.Hot => Color.FromHex("#FFA040"),
        WFBodyFeel.Chilly or WFBodyFeel.Warm => Color.FromHex("#E6DC96"),
        _ => Color.FromHex("#8FD694"),
    };

    /// <summary>The "Here" reading: the tile's air temperature, or vacuum.</summary>
    public static string HereText(WFWarmthComponent comp) => comp.HereCelsius is { } celsius
        ? Robust.Shared.Localization.Loc.GetString("wf-warmth-here", ("celsius", celsius))
        : Robust.Shared.Localization.Loc.GetString("wf-warmth-here-vacuum");
}
