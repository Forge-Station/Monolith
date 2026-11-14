using System.Numerics;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._WF.ShipShields;

/// <summary>Hosts the shared ship-local shield controls outside the helm.</summary>
public sealed class WFShipShieldGeneratorWindow : DefaultWindow
{
    /// <summary>The shared shield controls shown in this panel.</summary>
    public WFShipShieldShuntScreen ShieldPanel { get; } = new();

    /// <summary>Creates a generator panel with independent preview and settings scrolling.</summary>
    public WFShipShieldGeneratorWindow()
    {
        Title = Loc.GetString("wf-shield-generator-title");
        MinSize = new Vector2(760f, 540f);
        SetSize = new Vector2(900f, 700f);
        Contents.AddChild(ShieldPanel);
    }
}
