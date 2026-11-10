using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._WF.GameHub;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    /// <summary>Keeps a player's best score on one level of a game.</summary>
    Task SaveGameLevelScoreAsync(Guid userId, string name, string game, int level, int score, CancellationToken cancel = default);

    /// <summary>The top players of a game by the sum of their best level scores.</summary>
    Task<List<WFGameLeaderboardEntry>> GetGameLeaderboardAsync(string game, int count, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task SaveGameLevelScoreAsync(Guid userId, string name, string game, int level, int score, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SaveGameLevelScoreAsync(userId, name, game, level, score, cancel));
    }

    public Task<List<WFGameLeaderboardEntry>> GetGameLeaderboardAsync(string game, int count, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetGameLeaderboardAsync(game, count, cancel));
    }
}

public abstract partial class ServerDbBase
{
    public async Task SaveGameLevelScoreAsync(Guid userId, string name, string game, int level, int score, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var entry = await db.DbContext.WolfgateGameScore
            .SingleOrDefaultAsync(s => s.PlayerUserId == userId && s.Game == game && s.Level == level, cancel);

        if (entry == null)
        {
            entry = new WolfgateGameScore { PlayerUserId = userId, Game = game, Level = level };
            db.DbContext.WolfgateGameScore.Add(entry);
        }
        else if (entry.Score >= score)
        {
            entry.PlayerName = name;
            await db.DbContext.SaveChangesAsync(cancel);
            return;
        }

        entry.PlayerName = name;
        entry.Score = score;
        entry.Time = DateTime.UtcNow;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task<List<WFGameLeaderboardEntry>> GetGameLeaderboardAsync(string game, int count, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var totals = await db.DbContext.WolfgateGameScore
            .Where(s => s.Game == game)
            .GroupBy(s => s.PlayerUserId)
            .Select(g => new { Player = g.Key, Score = g.Sum(s => s.Score), Levels = g.Count(), Name = g.Max(s => s.PlayerName) })
            .OrderByDescending(t => t.Score)
            .ThenBy(t => t.Player)
            .Take(count)
            .ToListAsync(cancel);

        return totals.Select(t => new WFGameLeaderboardEntry(t.Name ?? string.Empty, t.Score, t.Levels)).ToList();
    }
}
