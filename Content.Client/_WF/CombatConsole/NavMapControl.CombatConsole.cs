using Robust.Client.UserInterface.Controls;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client.Pinpointer.UI;

public partial class NavMapControl
{
    /// <summary>Moves the original map controls out of their fixed-width overlay into an instrument bank.</summary>
    public BoxContainer WfDetachInstrumentToolbar()
    {
        var controls = Column(_beacons, Row(_zoom, _recenter));
        _beacons.Name = "DepartmentToggle";
        _zoom.Margin = _beacons.Margin = _recenter.Margin = new Thickness(0);
        _zoom.VerticalAlignment = VAlignment.Center;
        _beacons.HorizontalAlignment = HAlignment.Stretch;
        _recenter.HorizontalExpand = false;
        _recenter.MinWidth = 90;
        DisposeAllChildren();
        return controls;
    }
}
