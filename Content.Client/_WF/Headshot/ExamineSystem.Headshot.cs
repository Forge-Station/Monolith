using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client.Examine;

public sealed partial class ExamineSystem
{
    /// <summary>Raises <see cref="ExamineServerInfoShownEvent"/> for the open tooltip.</summary>
    private void RaiseServerInfoShown(EntityUid target, FormattedMessage message, bool knowTarget)
    {
        if (_examineTooltipOpen?.GetChild(0).GetChild(0) is BoxContainer box)
            RaiseLocalEvent(new ExamineServerInfoShownEvent(target, box, message, knowTarget));
    }
}

/// <summary>The server's examine info has filled the tooltip. Broadcast, so other systems can add rows to it.</summary>
public sealed class ExamineServerInfoShownEvent(EntityUid target, BoxContainer box, FormattedMessage message, bool knowTarget) : EntityEventArgs
{
    public readonly EntityUid Target = target;

    /// <summary>The tooltip's rows: the name header first, then the examine text and the verb buttons.</summary>
    public readonly BoxContainer Box = box;

    /// <summary>The server's examine text the tooltip was filled from.</summary>
    public readonly FormattedMessage Message = message;

    /// <summary>False when the server refused the examine, such as when the target is out of sight.</summary>
    public readonly bool KnowTarget = knowTarget;
}
