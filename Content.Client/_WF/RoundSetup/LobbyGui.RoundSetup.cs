using Content.Client._WF.RoundSetup;
using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Lobby.UI;

public sealed partial class LobbyGui
{
    /// <summary>Keeps the repository hero and menu, with a single character-edit action.</summary>
    private void InitializeRoundSetupHome()
    {
        CharacterPreview.CharacterSetupButton.Visible = false;
        foreach (var button in new[] { ReadyButton, RoundSetupButton, CustomizeButton })
            button.Label.ClipText = false;
        foreach (var banner in new[] { LinkBanner, (BoxContainer) DevInfoBanner })
        {
            foreach (var row in banner.Children)
            {
                foreach (var child in row.Children)
                {
                    if (child is Button button)
                        button.Label.ClipText = false;
                }
            }
        }
    }

    /// <summary>Explains readiness without adding another panel to the repository lobby.</summary>
    public void SetRoundSetupJoinText(string caption, string help)
    {
        ReadyButton.Text = caption;
        ReadyButton.ToolTip = help;
    }

    /// <summary>Clears crew readiness before editing the character in the main lobby.</summary>
    private void ClearRoundSetupReadiness()
    {
        var setup = IoCManager.Resolve<IEntityManager>().System<WFRoundSetupClientSystem>();
        if (setup.State?.OwnLobby is { } lobby)
            setup.Send(new WFRoundSetupRequest(WFRoundSetupAction.SetReady) { LobbyId = lobby, Ready = false });
    }
}
