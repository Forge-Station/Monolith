using Content.Client._WF.Shuttles.UI;
using Content.Shared._WF.Shuttles;
using Content.Shared.Shuttles.Systems;

namespace Content.Client.Shuttles.UI;

public sealed partial class MapScreen
{
    private readonly WFFtlDriveNotice _wfDriveNotice = new();
    private WFFtlDriveStatus _wfDrive = WFFtlDriveStatus.Ready;

    /// <summary>The server's last report of the ship's FTL drive; ready until it reports.</summary>
    public WFFtlDriveStatus WfFtlDrive => _wfDrive;

    /// <summary>The notice that shows on the page while the ship cannot jump.</summary>
    public WFFtlDriveNotice WfDriveNotice => _wfDriveNotice;

    /// <summary>True while the ship has no powered FTL drive.</summary>
    private bool WfFtlDriveBlocked => _wfDrive != WFFtlDriveStatus.Ready;

    /// <summary>Applies the server's drive status to the notice, the FTL button's tooltip and the state readout.</summary>
    private void WfUpdateFtlDrive(WFFtlDriveStatus status)
    {
        _wfDrive = status;
        _wfDriveNotice.SetStatus(status);
        MapFTLButton.ToolTip = WFFtlDriveNotice.Reason(status);
        if (WfFtlDriveBlocked && _state == FTLState.Available)
            MapFTLState.Text = Loc.GetString("wf-ftl-state-offline");
    }

    /// <summary>Disables the FTL button and ends an FTL preview, leaving autopilot targeting alone.</summary>
    private void WfBlockFtl()
    {
        if (MapFTLButton.Pressed)
        {
            MapFTLButton.Pressed = false;
            MapRadar.FtlMode = false;
        }
        MapRadar.ShowFTLRangeOnly = false;
        MapFTLButton.Disabled = true;
    }
}
