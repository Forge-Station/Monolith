using System.Linq;
using Content.Server.GameTicking;
using Content.Server.Voting;
using Content.Server.Voting.Managers;
using Content.Shared._WF.CCVar;
using Content.Shared.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>Holds a lobby vote each round for the storyteller's preset: how busy and how dangerous the sector is.</summary>
public sealed partial class WFEncounterVoteSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IVoteManager _votes = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;

    private bool _voteOnNextJoin;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => CallVote());
        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnPlayerJoinedLobby);
    }

    private void OnPlayerJoinedLobby(PlayerJoinedLobbyEvent args)
    {
        if (!_voteOnNextJoin)
            return;

        _voteOnNextJoin = false;
        CallVote();
    }

    /// <summary>Starts the preset vote, if encounters and the vote are on and there is more than one choice.</summary>
    public bool CallVote()
    {
        if (!_config.GetCVar(EncountersCVars.Enabled) || !_config.GetCVar(EncountersCVars.Vote))
            return false;

        var presets = _prototypes.EnumeratePrototypes<WFEncounterPresetPrototype>()
            .Where(preset => preset.Votable)
            .OrderBy(preset => preset.Budget)
            .ToList();
        if (presets.Count < 2)
            return false;

        if (_players.PlayerCount == 0)
        {
            _voteOnNextJoin = true;
            return false;
        }

        var options = new VoteOptions
        {
            Title = Loc.GetString("wf-encounter-vote-title"),
            InitiatorText = Loc.GetString("wf-encounter-vote-initiator"),
            // Runs as long as the game mode vote beside it.
            Duration = TimeSpan.FromSeconds(_config.GetCVar(CCVars.VoteTimerPreset)),
        };
        foreach (var preset in presets)
        {
            options.Options.Add((Loc.GetString(preset.Name), preset.ID));
        }

        var vote = _votes.CreateVote(options);
        vote.OnFinished += (_, args) =>
        {
            // A tie is settled at random; nobody voting keeps the configured preset.
            var winner = args.Winner as string ?? (args.Winners.Length > 0 && args.Votes.Sum() > 0 ? _random.Pick(args.Winners) as string : null);
            if (winner != null)
                SetPreset(winner);
        };
        return true;
    }

    /// <summary>Makes a preset the storyteller's for the round and says so in chat.</summary>
    public bool SetPreset(string id)
    {
        if (!_prototypes.TryIndex<WFEncounterPresetPrototype>(id, out var preset))
            return false;

        _config.SetCVar(EncountersCVars.Preset, id);
        // A vote result is out of character, so it reaches every world.
        EntityManager.System<Content.Server._WF.PlanetComms.WFPlanetCommsSystem>().Ungated(() =>
            EntityManager.System<Content.Server.Chat.Systems.ChatSystem>().DispatchGlobalAnnouncement(
                Loc.GetString("wf-encounter-vote-result", ("preset", Loc.GetString(preset.Name))),
                Loc.GetString("wf-encounter-vote-initiator"), playSound: false));
        return true;
    }
}
