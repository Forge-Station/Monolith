namespace Content.Server._WF.Hailing;

/// <summary>
/// An NPC that mans a shuttle console and answers hails there, greeting the caller and offering its grid's hail services.
/// </summary>
[RegisterComponent]
public sealed partial class HailResponderComponent : Component
{
    /// <summary>Greetings spoken on answering, picked at random. Given $name (the responder), $location (its grid) and $ship (the caller's).</summary>
    [DataField]
    public List<LocId> Greetings = new();

    /// <summary>Delay before a ringing hail is answered.</summary>
    [DataField]
    public float AnswerDelay = 2f;

    /// <summary>Range to the console it mans, in metres.</summary>
    [DataField]
    public float ConsoleRange = 1.5f;

    /// <summary>The console it mans, once found.</summary>
    [ViewVariables]
    public EntityUid? Console;

    /// <summary>Whether the current caller has been greeted.</summary>
    [ViewVariables]
    public bool Greeted;
}
