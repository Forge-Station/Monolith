using Content.Client._WF.Stylesheets;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.CustomMarkings.UI;

/// <summary>Asks what to do with a marking's unsaved edits when its editor is closed.</summary>
public sealed class CustomMarkingCloseWindow : CustomMarkingWindow
{
    /// <summary>Save the marking, then close the editor once the server confirms.</summary>
    public event Action? OnSave;

    /// <summary>Close the editor and drop the edits.</summary>
    public event Action? OnDiscard;

    public CustomMarkingCloseWindow()
    {
        Title = Loc.GetString("wf-custom-marking-close-title");
        Resizable = false;

        var save = new Button { Text = Loc.GetString("wf-custom-marking-close-save"), StyleClasses = { StyleWolfgate.StyleClassCreatorPrimary } };
        save.OnPressed += _ => OnSave?.Invoke();
        var discard = new Button { Text = Loc.GetString("wf-custom-marking-close-discard"), StyleClasses = { "Caution" } };
        discard.OnPressed += _ => OnDiscard?.Invoke();
        var keep = new Button { Text = Loc.GetString("wf-custom-marking-close-keep") };
        keep.OnPressed += _ => Close();

        Contents.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 12,
            Margin = new Thickness(8),
            Children =
            {
                new Label { Text = Loc.GetString("wf-custom-marking-close-text") },
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    SeparationOverride = 8,
                    HorizontalAlignment = HAlignment.Right,
                    Children = { keep, discard, save },
                },
            },
        });
    }
}
