using System.Numerics;
using Content.Shared._WF.Roles;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Roles;

/// <summary>Edits the custom job title of one role, checked live against the shared rules.</summary>
public sealed partial class CustomJobTitleWindow : DefaultWindow
{
    [Dependency] private IPrototypeManager _prototype = default!;

    private readonly CustomJobTitlePrototype _rules;
    private readonly string _jobName;
    private readonly LineEdit _edit;
    private readonly Label _status;

    /// <summary>Raised on every edit with the cleaned title, or null when empty or rejected.</summary>
    public Action<string?>? OnTitleChanged;

    public CustomJobTitleWindow(string jobName, string? title, CustomJobTitlePrototype rules)
    {
        IoCManager.InjectDependencies(this);
        _rules = rules;
        _jobName = jobName;

        Title = Loc.GetString("custom-job-title-window-title", ("job", jobName));
        MinSize = new Vector2(420, 0);

        var hint = new RichTextLabel { HorizontalExpand = true };
        hint.SetMessage(Loc.GetString("custom-job-title-window-hint", ("job", jobName)));

        _edit = new LineEdit
        {
            HorizontalExpand = true,
            Text = title ?? string.Empty,
            PlaceHolder = jobName,
            IsValid = text => text.Length <= rules.MaxLength,
        };
        _edit.OnTextChanged += _ => OnEdited();

        _status = new Label { ClipText = true };

        Contents.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new Thickness(8),
            SeparationOverride = 6,
            Children = { hint, _edit, _status },
        });

        UpdateStatus();
    }

    private void OnEdited()
    {
        OnTitleChanged?.Invoke(UpdateStatus());
    }

    /// <summary>Shows how the title will read, or why it was rejected. Returns the title to store.</summary>
    private string? UpdateStatus()
    {
        var title = CustomJobTitleRules.Clean(_edit.Text);
        _status.StyleClasses.Remove("Danger");

        if (title.Length == 0)
        {
            _status.Text = Loc.GetString("custom-job-title-status-default", ("job", _jobName));
            return null;
        }

        if (!CustomJobTitleRules.IsValid(title, _rules, _prototype, out var reason))
        {
            _status.Text = Loc.GetString("custom-job-title-status-rejected", ("reason", reason));
            _status.StyleClasses.Add("Danger");
            return null;
        }

        _status.Text = Loc.GetString("custom-job-title-status-valid", ("title", title));
        return title;
    }
}
