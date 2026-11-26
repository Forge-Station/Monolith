using Content.Client.Resources;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.UserInterface.WindowPopout;

/// <summary>
/// Title bar button that pops a window out into its own OS window, or docks it back.
/// </summary>
public sealed partial class WolfgatePopoutButton : TextureButton
{
    public const string StyleClassWindowPopoutButton = "windowPopoutButton";

    private const string PopOutTexture = "/Textures/_WF/UserInterface/Interface/Window/popout.png";
    private const string DockTexture = "/Textures/_WF/UserInterface/Interface/Window/dock.png";

    [Dependency] private IResourceCache _cache = default!;

    private bool _poppedOut;

    public WolfgatePopoutButton()
    {
        IoCManager.InjectDependencies(this);
        AddStyleClass(StyleClassWindowPopoutButton);
        VerticalAlignment = VAlignment.Center;
        Margin = new Thickness(0, 0, 4, 0);
        PoppedOut = false;
    }

    public bool PoppedOut
    {
        get => _poppedOut;
        set
        {
            _poppedOut = value;
            TextureNormal = _cache.GetTexture(value ? DockTexture : PopOutTexture);
            ToolTip = Loc.GetString(value ? "wf-window-popout-dock" : "wf-window-popout-pop-out");
        }
    }
}
