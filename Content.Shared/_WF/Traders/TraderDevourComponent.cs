using Robust.Shared.Map;

namespace Content.Shared._WF.Traders;

/// <summary>
/// A trader who eats the people left dead or dying near his post: he walks over and devours them (his
/// <c>Devourer</c>), and gives them up again when he dies.
/// </summary>
[RegisterComponent]
public sealed partial class TraderDevourComponent : Component
{
    /// <summary>Blackboard key his HTN walks to.</summary>
    public const string TargetKey = "WFTraderMealTarget";

    /// <summary>How far from where he started, in metres, a body counts as left in his place.</summary>
    [DataField]
    public float Radius = 12f;

    /// <summary>How close he has to be to start eating.</summary>
    [DataField]
    public float Reach = 1.4f;

    /// <summary>How long he looks before giving up on a body he can't reach.</summary>
    [DataField]
    public TimeSpan GiveUp = TimeSpan.FromSeconds(30);

    /// <summary>Where he started: his post.</summary>
    [ViewVariables]
    public MapCoordinates? Home;

    /// <summary>The body he is going for.</summary>
    [ViewVariables]
    public EntityUid? Meal;

    /// <summary>When he set off for it.</summary>
    [ViewVariables]
    public TimeSpan MealSince;

    /// <summary>When he next looks round.</summary>
    [ViewVariables]
    public TimeSpan NextThink;
}
