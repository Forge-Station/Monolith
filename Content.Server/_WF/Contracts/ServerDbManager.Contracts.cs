using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

/// <summary>A character's stored standing with one contractor.</summary>
public sealed record WFStoredContractStanding(string Contractor, int Points, int LastRound);

public partial interface IServerDbManager
{
    /// <summary>A character's standing with every contractor they have dealt with.</summary>
    Task<List<WFStoredContractStanding>> GetContractStandingAsync(Guid userId, string character, CancellationToken cancel = default);

    /// <summary>Stores a character's standing with one contractor.</summary>
    Task SetContractStandingAsync(Guid userId, string character, string contractor, int points, int round, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<List<WFStoredContractStanding>> GetContractStandingAsync(Guid userId, string character, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetContractStandingAsync(userId, character, cancel));
    }

    public Task SetContractStandingAsync(Guid userId, string character, string contractor, int points, int round, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SetContractStandingAsync(userId, character, contractor, points, round, cancel));
    }
}

public abstract partial class ServerDbBase
{
    public async Task<List<WFStoredContractStanding>> GetContractStandingAsync(Guid userId, string character, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.WolfgateContractStanding
            .Where(s => s.PlayerUserId == userId && s.CharacterName == character)
            .Select(s => new WFStoredContractStanding(s.Contractor, s.Points, s.LastRound))
            .ToListAsync(cancel);
    }

    public async Task SetContractStandingAsync(Guid userId, string character, string contractor, int points, int round, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var entry = await db.DbContext.WolfgateContractStanding
            .SingleOrDefaultAsync(s => s.PlayerUserId == userId && s.CharacterName == character && s.Contractor == contractor, cancel);

        if (entry == null)
        {
            entry = new WolfgateContractStanding { PlayerUserId = userId, CharacterName = character, Contractor = contractor };
            db.DbContext.WolfgateContractStanding.Add(entry);
        }

        entry.Points = points;
        entry.LastRound = round;
        entry.Updated = DateTime.UtcNow;
        await db.DbContext.SaveChangesAsync(cancel);
    }
}
