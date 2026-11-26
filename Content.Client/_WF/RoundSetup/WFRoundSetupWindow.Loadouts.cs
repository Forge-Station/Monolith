using Content.Client._WF.Loadouts;
using Content.Shared.Clothing;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared._WF.RoundSetup;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    private WolfgateLoadoutWindow? _jobLoadout;

    /// <summary>Offers the normal saved job loadout even for professions hidden from legacy job preferences.</summary>
    private void BuildJobLoadout(WFRoundSetupLobby lobby, WFRoundSetupMember member)
    {
        var available = member.Job != null && _prototypes.HasIndex<RoleLoadoutPrototype>(LoadoutSystem.GetJobPrototype(member.Job));
        if (member.Job != null)
            _body.AddChild(JobLoadoutLabel(member.Job));
        _body.AddChild(Button("job-loadout", () => OpenJobLoadout(lobby, member), member.Ready || !available,
            member.Ready ? "unready-edit" : "ready-needs-job"));
    }

    private Robust.Client.UserInterface.Controls.RichTextLabel JobLoadoutLabel(string job)
    {
        var label = PlainDescription(JobLoadoutSummary(job));
        label.ToolTip = Text("job-loadout-help");
        return label;
    }

    private string JobLoadoutSummary(string job) => Text("job-loadout-summary", ("job", JobName(job)),
        ("character", _preferences.Preferences?.SelectedCharacter?.Name ?? Text("unknown-character")));

    private void OpenJobLoadout(WFRoundSetupLobby lobby, WFRoundSetupMember member)
    {
        if (_jobLoadout is { IsOpen: true })
        {
            _jobLoadout.MoveToFront();
            return;
        }
        if (_players.LocalSession is not { } session || _preferences.Preferences?.SelectedCharacter is not HumanoidCharacterProfile profile ||
            !_prototypes.TryIndex<RoleLoadoutPrototype>(LoadoutSystem.GetJobPrototype(member.Job), out var prototype))
            return;
        var slot = _preferences.Preferences.SelectedCharacterIndex;
        var loadout = profile.GetLoadoutOrDefault(prototype.ID, session, profile.Species, _entities, _prototypes).Clone();
        var collection = IoCManager.Instance!;
        var editor = new WolfgateLoadoutWindow(profile, loadout, prototype, session, collection)
        {
            Title = Text("job-loadout-title", ("job", JobName(member.Job))),
        };
        _jobLoadout = editor;
        var changed = false;
        editor.OnNameChanged += name => { loadout.EntityName = name; changed = true; };
        editor.OnLoadoutPressed += (group, item) =>
        {
            changed |= loadout.AddLoadout(group, item, _prototypes);
            editor.RefreshLoadouts(loadout, session, collection);
        };
        editor.OnLoadoutUnpressed += (group, item) =>
        {
            changed |= loadout.RemoveLoadout(group, item, _prototypes);
            editor.RefreshLoadouts(loadout, session, collection);
        };
        editor.OnClose += () =>
        {
            OnClose -= editor.Close;
            _jobLoadout = null;
            Visible = true;
            if (!changed || _preferences.Preferences?.Characters.TryGetValue(slot, out var current) != true || current is not HumanoidCharacterProfile latest)
                return;
            _preferences.UpdateCharacter(latest.WithLoadout(loadout), slot);
            SetReady(lobby.Id, false);
        };
        OnClose += editor.Close;
        editor.RefreshLoadouts(loadout, session, collection);
        Visible = false;
        editor.OpenCentered();
    }
}
