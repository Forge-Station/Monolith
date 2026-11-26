using Content.Shared.Actions;
using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Life;

/// <summary>
/// Wolfmed's Succumb: offered only while Dying (M1a, plan §5.4). Its own type, because upstream's
/// <c>CritSuccumbEvent</c> is already handled by the upstream ghost path.
/// </summary>
public sealed partial class WolfmedSuccumbActionEvent : InstantActionEvent;

/// <summary>Wolfmed's Last Words: the Succumb dialog first, then a whisper after a yes (playtest 3).</summary>
public sealed partial class WolfmedLastWordsActionEvent : InstantActionEvent
{
    /// <summary>Longest whisper, in characters; the upstream limit.</summary>
    [DataField]
    public int MaxLength = 30;
}

/// <summary>A yes/no dialog with exact text: the Succumb and "left alive but empty" dialogs.</summary>
[Serializable, NetSerializable]
public sealed class WolfmedChoiceEuiState : EuiStateBase
{
    public readonly string Title;
    public readonly string Text;
    public readonly string Accept;
    public readonly string Deny;

    public WolfmedChoiceEuiState(string title, string text, string accept, string deny)
    {
        Title = title;
        Text = text;
        Accept = accept;
        Deny = deny;
    }
}

/// <summary>The player's answer to a <see cref="WolfmedChoiceEuiState"/> dialog.</summary>
[Serializable, NetSerializable]
public sealed class WolfmedChoiceMessage : EuiMessageBase
{
    public readonly bool Accepted;

    public WolfmedChoiceMessage(bool accepted)
    {
        Accepted = accepted;
    }
}
