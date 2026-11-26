using Content.Shared._WF.CCVar;
using Robust.Shared.Configuration;

namespace Content.Client.Shuttles.UI;

// Every radar screen starts with the display choices the player last made on any of them; see Content.Server/_WF/Shuttles.
public partial class ShuttleNavControl
{
    [Dependency] private IConfigurationManager _wfSettingsCfg = default!;

    private bool _wfSavedIff;
    private bool _wfSavedIffDetailed;
    private bool _wfSavedDocks;
    private RadarAzimuthMode _wfSavedAzimuth;
    private bool _wfDocksPinned;

    /// <summary>Starts the radar with the player's saved contact labels, dock markers and bearing ring.</summary>
    private void WfLoadDisplaySettings()
    {
        ShowIFF = _wfSavedIff = _wfSettingsCfg.GetCVar(WolfgateCVars.RadarShowIff);
        ShowIFFDetailed = _wfSavedIffDetailed = _wfSettingsCfg.GetCVar(WolfgateCVars.RadarShowIffDetailed);
        ShowDocks = _wfSavedDocks = _wfSettingsCfg.GetCVar(WolfgateCVars.RadarShowDocks);
        _azimuthMode = _wfSavedAzimuth = _wfSettingsCfg.GetCVar(WolfgateCVars.RadarAzimuth) switch
        {
            1 => RadarAzimuthMode.Ring,
            2 => RadarAzimuthMode.Off,
            _ => RadarAzimuthMode.Edge,
        };
    }

    /// <summary>For a radar screen with no docks toggle: docks always show and its state is never saved.</summary>
    public void WfPinDocks()
    {
        _wfDocksPinned = true;
        ShowDocks = true;
    }

    /// <summary>Saves a display choice when the player changes it.</summary>
    private void WfSaveDisplaySettings()
    {
        var docks = _wfDocksPinned ? _wfSavedDocks : ShowDocks;
        if (ShowIFF == _wfSavedIff && ShowIFFDetailed == _wfSavedIffDetailed && docks == _wfSavedDocks &&
            _azimuthMode == _wfSavedAzimuth)
            return;

        _wfSettingsCfg.SetCVar(WolfgateCVars.RadarShowIff, _wfSavedIff = ShowIFF);
        _wfSettingsCfg.SetCVar(WolfgateCVars.RadarShowIffDetailed, _wfSavedIffDetailed = ShowIFFDetailed);
        _wfSettingsCfg.SetCVar(WolfgateCVars.RadarShowDocks, _wfSavedDocks = docks);
        _wfSettingsCfg.SetCVar(WolfgateCVars.RadarAzimuth, (int) (_wfSavedAzimuth = _azimuthMode));
        _wfSettingsCfg.SaveToFile();
    }
}
