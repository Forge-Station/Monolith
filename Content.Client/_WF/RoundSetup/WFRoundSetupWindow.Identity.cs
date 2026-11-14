using System.Numerics;
using Content.Client.Lobby;
using Content.Shared._WF.RoundSetup;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    /// <summary>Gives a crew its leader's visible identity and existing profession icon.</summary>
    private BoxContainer CrewIdentity(WFRoundSetupLobby lobby)
    {
        var identity = Column(4);
        identity.AddChild(PlainDescription(LobbyTitle(lobby)));
        if (lobby.Scenario == WFRoundScenario.FactionShip)
            identity.AddChild(Description("faction-" + lobby.Faction.ToString().ToLowerInvariant()));
        identity.AddChild(PlainDescription(Text("crew-leader", ("name", lobby.Portrait?.Name ?? lobby.HostName))));
        identity.AddChild(Row(WFRoundSetupCrewRow.JobIcon(lobby.HostJob, lobby.HostRankId),
            PlainDescription(lobby.HostTitle.Length > 0 ? lobby.HostTitle : JobName(lobby.HostJob))));
        return Row(new LeaderPortrait(lobby.Portrait, lobby.HostJob), identity);
    }

    /// <summary>Owns a preview dummy only while its portrait is attached to the visible UI.</summary>
    private sealed class LeaderPortrait : SpriteView
    {
        private readonly WFCrewPortrait? _portrait;
        private readonly string? _job;
        private EntityUid? _dummy;

        public LeaderPortrait(WFCrewPortrait? portrait, string? job)
        {
            _portrait = portrait;
            _job = job;
            SetSize = new Vector2(80);
            Scale = new Vector2(2.5f);
            Stretch = StretchMode.Fill;
            OverrideDirection = Direction.South;
            MouseFilter = MouseFilterMode.Ignore;
        }

        protected override void EnteredTree()
        {
            base.EnteredTree();
            if (_portrait == null)
                return;
            var profile = HumanoidCharacterProfile.DefaultWithSpecies(_portrait.Species)
                .WithName(_portrait.Name).WithSex(_portrait.Sex).WithCharacterAppearance(_portrait.Appearance);
            var prototypes = IoCManager.Resolve<Robust.Shared.Prototypes.IPrototypeManager>();
            prototypes.TryIndex<JobPrototype>(_job ?? "Contractor", out var job);
            _dummy = UserInterfaceManager.GetUIController<LobbyUIController>().LoadProfileEntity(profile, job, job != null);
            SetEntity(_dummy);
        }

        protected override void ExitedTree()
        {
            SetEntity((EntityUid?) null);
            if (_dummy != null)
                IoCManager.Resolve<IEntityManager>().DeleteEntity(_dummy);
            _dummy = null;
            base.ExitedTree();
        }
    }
}
