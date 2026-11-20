using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Forge.Company;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Forge.Company;

/// <summary>
/// Company management outside the PDA. The cartridge only launches this window.
/// </summary>
public sealed class CompanyManagementWindow : FancyWindow
{
    public static readonly Vector2 PreferredSize = new(1080, 680);
    public static readonly Vector2 PreferredMinSize = new(860, 540);

    public CompanyManagementUiFragment Fragment { get; }

    public CompanyManagementWindow(CompanyManagementUiFragment fragment)
    {
        Fragment = fragment;
        Title = Loc.GetString("company-management-program-name");
        Resizable = true;

        var ui = IoCManager.Resolve<IUserInterfaceManager>();
        var root = ui.WindowRoot.Size;
        var available = root.X > 64 && root.Y > 64
            ? Vector2.Max(root - new Vector2(48, 48), new Vector2(640, 420))
            : PreferredSize;

        MinSize = Vector2.Min(PreferredMinSize, available);
        SetSize = Vector2.Min(PreferredSize, available);

        fragment.HorizontalExpand = true;
        fragment.VerticalExpand = true;
        ContentsContainer.AddChild(fragment);
    }
}

/// <summary>
/// What the PDA program page shows while the real UI is the window above.
/// </summary>
public sealed class CompanyManagementPdaNotice : BoxContainer
{
    public Action? OnDisposed;

    public CompanyManagementPdaNotice(Action show)
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;

        AddChild(new Label
        {
            Text = Loc.GetString("company-pda-external-window"),
            HorizontalAlignment = HAlignment.Center,
            Align = Label.AlignMode.Center,
            Margin = new Thickness(16, 28, 16, 8),
        });

        var showButton = new Button
        {
            Text = Loc.GetString("company-pda-external-window-show"),
            HorizontalAlignment = HAlignment.Center,
            MinWidth = 180,
        };
        showButton.OnPressed += _ => show();
        AddChild(showButton);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            OnDisposed?.Invoke();
    }
}
