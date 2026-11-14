using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.RoundSetup;

/// <summary>Colour-coded scenario difficulty for cards, details and crew rows.</summary>
public static class WFScenarioDifficultyLabel
{
    /// <summary>Blue to red, easiest first.</summary>
    public static Color Color(WFScenarioDifficulty difficulty) => difficulty switch
    {
        WFScenarioDifficulty.Easy => Robust.Shared.Maths.Color.FromHex("#4fb3ff"),
        WFScenarioDifficulty.Normal => Robust.Shared.Maths.Color.FromHex("#6fd36f"),
        WFScenarioDifficulty.Hard => Robust.Shared.Maths.Color.FromHex("#ffc83d"),
        WFScenarioDifficulty.VeryHard => Robust.Shared.Maths.Color.FromHex("#ff7a2f"),
        _ => Robust.Shared.Maths.Color.FromHex("#e02424"),
    };

    /// <summary>The level's name alone, such as "Very Hard".</summary>
    public static string Name(WFScenarioDifficulty difficulty) =>
        Loc.GetString("wf-round-setup-difficulty-" + difficulty.ToString().ToLowerInvariant());

    /// <summary>"Difficulty: Normal", coloured by level.</summary>
    public static Label Create(WFRoundScenario scenario)
    {
        var difficulty = WFRoundSetupPolicy.Difficulty(scenario);
        return new Label
        {
            Text = Loc.GetString("wf-round-setup-difficulty", ("level", Name(difficulty))),
            FontColorOverride = Color(difficulty),
        };
    }
}
