using System.Linq;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared.GameTicking;
using Content.Shared._WF.Contracts;
using Robust.Shared.Utility;

namespace Content.Server._WF.Contracts;

/// <summary>Saves standing per character and drifts it toward neutral once per round played.</summary>
public sealed partial class WFContractSystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private GameTicker _ticker = default!;

    /// <summary>Characters whose stored standing has been read this round.</summary>
    private readonly HashSet<WFContractHolder> _loaded = new();

    /// <summary>Saves run in order, so two changes to a new row cannot both insert it.</summary>
    private Task _saves = Task.CompletedTask;

    /// <summary>Bumped each round, so a load that finishes after a restart is dropped.</summary>
    private int _generation;

    private void InitializePersistence()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
    }

    private void OnPlayerSpawned(PlayerSpawnCompleteEvent ev)
    {
        _ = LoadStanding(new WFContractHolder(ev.Player.UserId, ev.Profile.Name));
    }

    /// <summary>Loads, drifts and merges a character's stored standing, once per round.</summary>
    public async Task LoadStanding(WFContractHolder holder)
    {
        if (!_loaded.Add(holder))
            return;

        var generation = _generation;
        List<WFStoredContractStanding> stored;
        try
        {
            stored = await _db.GetContractStandingAsync(holder.User, holder.Character);
        }
        catch (Exception e)
        {
            Log.Error($"Could not load contract standing for {holder.Character} ({holder.User}): {e}");
            // Unread standing must not overwrite the stored row.
            if (generation == _generation)
                _loaded.Remove(holder);
            return;
        }

        if (generation != _generation)
            return;

        var round = _ticker.RoundId;
        var standing = _standing.GetOrNew(holder);
        var changed = standing.Where(pair => pair.Value != 0).Select(pair => pair.Key).ToHashSet();
        foreach (var row in stored)
        {
            var points = row.Points;
            if (row.LastRound != round && _proto.TryIndex<WFContractorPrototype>(row.Contractor, out var contractor))
                points = Drifted(points, contractor.Drift);

            if (points != row.Points || row.LastRound != round)
                changed.Add(row.Contractor);

            standing[row.Contractor] = Math.Clamp(points + standing.GetValueOrDefault(row.Contractor), MinStanding, MaxStanding);
        }

        foreach (var contractor in changed)
        {
            SaveStanding(holder, contractor);
        }

        _dirty = true;
    }

    /// <summary>Standing moved toward neutral by up to <paramref name="drift"/>.</summary>
    public static int Drifted(int points, int drift) =>
        points > 0 ? Math.Max(0, points - drift) : Math.Min(0, points + drift);

    /// <summary>Stores one standing, once the character's stored standing has been read.</summary>
    private void SaveStanding(WFContractHolder holder, string contractor)
    {
        if (!_loaded.Contains(holder))
            return;

        var points = Standing(holder, contractor);
        var round = _ticker.RoundId;
        var previous = _saves;
        _saves = Save();

        async Task Save()
        {
            await previous;
            try
            {
                await _db.SetContractStandingAsync(holder.User, holder.Character, contractor, points, round);
            }
            catch (Exception e)
            {
                Log.Error($"Could not save contract standing for {holder.Character} ({holder.User}) with {contractor}: {e}");
            }
        }
    }
}
