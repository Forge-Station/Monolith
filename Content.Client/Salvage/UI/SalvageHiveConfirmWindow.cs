// Forge-Change-full: confirm dialog before claiming the extra-hard hive expedition.
using Content.Client.Stylesheets;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client.Salvage.UI;

/// <summary>
/// Shown before claiming the extra-hard hive expedition.
/// </summary>
public sealed class SalvageHiveConfirmWindow : DefaultWindow
{
    public event Action? Confirmed;

    public SalvageHiveConfirmWindow()
    {
        Title = Loc.GetString("salvage-expedition-hive-warning-title");
        MinWidth = 460;

        var body = new Label
        {
            Text = Loc.GetString("salvage-expedition-hive-warning"),
            FontColorOverride = StyleNano.ConcerningOrangeFore,
        };

        var accept = new Button
        {
            Text = Loc.GetString("salvage-expedition-hive-warning-accept"),
            HorizontalExpand = true,
        };
        accept.AddStyleClass(StyleBase.ButtonCaution);

        var cancel = new Button
        {
            Text = Loc.GetString("salvage-expedition-hive-warning-cancel"),
            HorizontalExpand = true,
        };

        accept.OnPressed += _ =>
        {
            Confirmed?.Invoke();
            Close();
        };
        cancel.OnPressed += _ => Close();

        Contents.AddChild(new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            Children =
            {
                body,
                new Control { MinHeight = 12 },
                new BoxContainer
                {
                    Orientation = LayoutOrientation.Horizontal,
                    SeparationOverride = 8,
                    Children = { accept, cancel },
                },
            },
        });
    }
}
