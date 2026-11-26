using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     URL of the Discord webhook that receives a one-way copy of dead chat. If empty, disabled.
    /// </summary>
    public static readonly CVarDef<string> DeadChatWebhook =
        CVarDef.Create("wf.discord.dead_chat_webhook", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    ///     URL of the Discord webhook that receives a one-way copy of LOOC. If empty, disabled.
    /// </summary>
    public static readonly CVarDef<string> LoocWebhook =
        CVarDef.Create("wf.discord.looc_webhook", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);
}
