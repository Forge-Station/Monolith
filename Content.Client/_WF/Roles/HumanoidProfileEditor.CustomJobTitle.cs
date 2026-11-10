using Content.Client._WF.Roles;
using Content.Shared._WF.Roles;
using Content.Shared.Clothing;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Lobby.UI;

/// <summary>The Custom Job button on job rows whose role allows a custom job title.</summary>
public sealed partial class HumanoidProfileEditor
{
    private CustomJobTitleWindow? _customJobTitleWindow;

    /// <summary>Adds the Custom Job button to a job row if the role has custom job title rules.</summary>
    private void AddCustomJobTitleButton(JobPrototype job, BoxContainer jobContainer)
    {
        var roleId = LoadoutSystem.GetJobPrototype(job.ID);
        if (!_prototypeManager.TryIndex<CustomJobTitlePrototype>(roleId, out var rules))
            return;

        var button = new Button
        {
            Text = Loc.GetString("custom-job-title-button"),
            ToolTip = Loc.GetString("custom-job-title-button-tooltip", ("job", job.LocalizedName)),
            HorizontalAlignment = HAlignment.Right,
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(3f, 3f, 0f, 0f),
        };
        button.OnPressed += _ => OpenCustomJobTitle(job, roleId, rules);
        jobContainer.AddChild(button);
    }

    private void OpenCustomJobTitle(JobPrototype job, string roleId, CustomJobTitlePrototype rules)
    {
        // The loadout window edits its own copy of the role loadout and would write back the old title.
        _loadoutWindow?.Close();
        CloseCustomJobTitle();

        RoleLoadout? current = null;
        Profile?.Loadouts.TryGetValue(roleId, out current);
        _customJobTitleWindow = new CustomJobTitleWindow(job.LocalizedName, current?.CustomJobTitle, rules);
        _customJobTitleWindow.OnTitleChanged += title => SetCustomJobTitle(roleId, title);
        _customJobTitleWindow.OnClose += () => _customJobTitleWindow = null;
        _customJobTitleWindow.OpenCentered();
    }

    private void SetCustomJobTitle(string roleId, string? title)
    {
        if (Profile == null)
            return;

        Profile.Loadouts.TryGetValue(roleId, out var loadout);
        loadout = loadout?.Clone();
        if (loadout == null)
        {
            loadout = new RoleLoadout(roleId);
            loadout.SetDefault(Profile, _playerManager.LocalSession, _prototypeManager);
        }

        if (loadout.CustomJobTitle == title)
            return;

        loadout.CustomJobTitle = title;
        Profile = Profile.WithLoadout(loadout);
        SetDirty();
    }

    private void CloseCustomJobTitle()
    {
        _customJobTitleWindow?.Close();
        _customJobTitleWindow = null;
    }
}
