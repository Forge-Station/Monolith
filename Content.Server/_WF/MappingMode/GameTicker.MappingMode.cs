using Content.Shared._WF.MappingMode;
using Robust.Shared.Player;

namespace Content.Server.GameTicking;

public sealed partial class ServerGameTicker
{
    /// <summary>
    /// Sends a player back to the lobby without a respawn log or respawn reset, for leaving a mapping session.
    /// </summary>
    public void WFMappingReturnToLobby(ICommonSession session)
    {
        _mind.WipeMind(session);
        PlayerJoinLobby(session);
    }

    /// <summary>
    /// True while mapping mode is on: no round starts and no game map or preset rule loads, since those build the
    /// whole sector. An announced refusal also pauses the lobby again so the countdown doesn't retry every tick.
    /// </summary>
    private bool WFMappingBlocksRound(bool announce)
    {
        if (!_cfg.GetCVar(WFMappingModeCVars.Enabled))
            return false;

        if (announce)
        {
            _sawmill.Info("Mapping mode is on; not starting a round");
            SendServerMessage(Loc.GetString("wf-mapping-mode-no-round"));
            PauseStart(true);
        }

        return true;
    }
}
