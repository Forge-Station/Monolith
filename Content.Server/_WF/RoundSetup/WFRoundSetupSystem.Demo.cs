using System.Linq;
using Content.Server.GameTicking;
using Content.Shared.GameTicking;
using Content.Shared._WF.RoundSetup;
using Robust.Shared.Configuration;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFRoundSetupSystem
{
    [Dependency] private IConfigurationManager _demoConfig = default!;

    private bool _demoEnabled;
    private bool _demoSeeded;
    private string _demoHost = string.Empty;

    private void InitializeDemo()
    {
        Subs.CVar(_demoConfig, WFRoundSetupCVars.DemoEnabled, value => _demoEnabled = value, true);
        Subs.CVar(_demoConfig, WFRoundSetupCVars.DemoHost, value => _demoHost = value, true);
    }

    /// <summary>Seeds only an explicitly enabled playtest, after its real host has loaded ordinary player data.</summary>
    private void UpdateDemo()
    {
        if (!_demoEnabled || _demoSeeded || _ticker.RunLevel != GameRunLevel.PreRoundLobby || !_crash.IsAvailable)
            return;

        var host = _players.Sessions.FirstOrDefault(session => session.Name == _demoHost && CanEnter(session));
        if (host == null)
            return;
        if (FindLobby(host.UserId) != null)
        {
            _demoSeeded = true;
            return;
        }
        if (!IsJobAllowed(host, "Contractor") || _lobbies.Count >= 64)
            return;

        var lobby = new Lobby
        {
            Id = ++_nextId,
            Host = host.UserId,
            Scenario = WFRoundScenario.Crashlanded,
            Access = WFLobbyAccess.Public,
            AllowLateJoin = true,
            Code = NewCode(),
        };
        _lobbies.Add(lobby.Id, lobby);
        RegisterMember(lobby, host, new Member { Name = host.Name });
        if (!SelectRoleByJob(lobby, host, "Contractor", null)
            || !UpsertRole(lobby, host, new WFRoundSetupRequest(WFRoundSetupAction.UpsertRole)
            {
                LobbyId = lobby.Id,
                Job = "Contractor",
                Capacity = 3,
            }))
        {
            RemoveMember(lobby, host.UserId);
            return;
        }

        _ticker.ToggleReady(host, true);
        _demoSeeded = true;
        SendState(host);
        Broadcast();
        Log.Info($"Prepared public Round Setup playtest lobby {lobby.Id} for {host.Name} with three crew openings.");
    }

    private void ResetDemo()
    {
        _demoSeeded = false;
    }
}
