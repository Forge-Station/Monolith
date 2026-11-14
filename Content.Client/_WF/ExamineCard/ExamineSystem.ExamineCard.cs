using Robust.Client.UserInterface.Controls;

namespace Content.Client.Examine;

public sealed partial class ExamineSystem
{
    /// <summary>The open tooltip's rows, so the examine card can replace the client's own guess at once.</summary>
    public BoxContainer? TooltipBox => _examineTooltipOpen?.GetChild(0).GetChild(0) as BoxContainer;
}
