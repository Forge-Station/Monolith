using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.CustomMarkings;
using Content.Shared._WF.CustomMarkings;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

// Custom marking art and each player's library of it.
public abstract partial class ServerDbBase
{
    /// <summary>How many art rows one statement of the cleanup names.</summary>
    private const int CustomMarkingPurgeBatch = 200;

    public async Task<List<WolfgateCustomMarking>> GetCustomMarkingsAsync(Guid userId, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        return await db.DbContext.WolfgateCustomMarking
            .Where(m => m.PlayerUserId == userId)
            .OrderBy(m => m.Id)
            .ToListAsync(cancel);
    }

    public async Task<CustomMarkingSaveResult> SaveCustomMarkingAsync(
        Guid userId,
        int id,
        string name,
        int placement,
        string? hash,
        CustomMarkingStoredArt? art,
        int limit,
        int dailyArtLimit = 0,
        CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var ctx = db.DbContext;

        WolfgateCustomMarking? entry = null;
        if (id != 0)
        {
            entry = await ctx.WolfgateCustomMarking.SingleOrDefaultAsync(m => m.Id == id && m.PlayerUserId == userId, cancel);
            if (entry == null)
                return CustomMarkingSaveResult.Fail("wf-custom-marking-error-missing");
        }
        else if (hash == null || art == null)
        {
            return CustomMarkingSaveResult.Fail("wf-custom-marking-error-invalid");
        }
        else if (await ctx.WolfgateCustomMarking.CountAsync(m => m.PlayerUserId == userId, cancel) >= limit)
        {
            return CustomMarkingSaveResult.Fail("wf-custom-marking-error-full");
        }

        string? previous = null;
        if (hash != null && art != null)
        {
            var blocked = await ctx.WolfgateCustomMarkingArt
                .Where(a => a.Hash == hash)
                .Select(a => (bool?) a.Blocked)
                .SingleOrDefaultAsync(cancel);

            if (blocked == true)
                return CustomMarkingSaveResult.Fail("wf-custom-marking-error-blocked");

            if (blocked == null)
            {
                // Art the server doesn't hold yet is a new row, and a player only gets so many of those a day.
                if (dailyArtLimit > 0)
                {
                    var since = DateTime.UtcNow - TimeSpan.FromDays(1);
                    var today = await ctx.WolfgateCustomMarkingArt
                        .CountAsync(a => a.UploaderUserId == userId && a.UploadedAt > since, cancel);

                    if (today >= dailyArtLimit)
                        return CustomMarkingSaveResult.Fail("wf-custom-marking-error-daily");
                }

                ctx.WolfgateCustomMarkingArt.Add(new WolfgateCustomMarkingArt
                {
                    Hash = hash,
                    Png = art.Png,
                    FrameTimes = art.FrameTimes,
                    Erase = art.Erase,
                    UploaderUserId = userId,
                    UploadedAt = DateTime.UtcNow,
                });
            }

            if (entry != null && entry.ArtHash != hash)
                previous = entry.ArtHash;
        }

        if (entry == null)
        {
            entry = new WolfgateCustomMarking { PlayerUserId = userId };
            ctx.WolfgateCustomMarking.Add(entry);
        }

        if (hash != null)
            entry.ArtHash = hash;

        entry.Name = name;
        entry.Placement = placement;
        entry.UpdatedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync(cancel);

        return new CustomMarkingSaveResult(entry, previous, null);
    }

    public async Task<bool> DeleteCustomMarkingAsync(Guid userId, int id, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var entry = await db.DbContext.WolfgateCustomMarking
            .SingleOrDefaultAsync(m => m.Id == id && m.PlayerUserId == userId, cancel);

        if (entry == null)
            return false;

        db.DbContext.WolfgateCustomMarking.Remove(entry);
        await db.DbContext.SaveChangesAsync(cancel);
        return true;
    }

    public async Task<CustomMarkingStoredArt?> GetCustomMarkingArtAsync(string hash, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var art = await db.DbContext.WolfgateCustomMarkingArt
            .Where(a => a.Hash == hash && !a.Blocked)
            .Select(a => new { a.Png, a.FrameTimes, a.Erase })
            .SingleOrDefaultAsync(cancel);

        return art == null ? null : new CustomMarkingStoredArt(art.Png, art.FrameTimes, art.Erase);
    }

    public async Task<(int Found, int Deleted)> PurgeUnusedCustomMarkingArtAsync(TimeSpan keep, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var ctx = db.DbContext;
        var now = DateTime.UtcNow;

        // Art a library holds is in use, whatever an earlier look found.
        await ctx.WolfgateCustomMarkingArt
            .Where(a => a.UnusedSince != null && ctx.WolfgateCustomMarking.Any(m => m.ArtHash == a.Hash))
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.UnusedSince, (DateTime?) null), cancel);

        var loose = await ctx.WolfgateCustomMarkingArt
            .Where(a => !a.Blocked && !ctx.WolfgateCustomMarking.Any(m => m.ArtHash == a.Hash))
            .Select(a => new { a.Hash, a.UnusedSince })
            .ToListAsync(cancel);

        if (loose.Count == 0)
            return (0, 0);

        // Art no library holds may still be worn: a saved character lists its art by hash.
        var worn = new HashSet<string>();
        var lists = await ctx.Profile
            .Where(p => p.CustomMarkings != "")
            .Select(p => p.CustomMarkings)
            .ToListAsync(cancel);

        foreach (var list in lists)
        {
            foreach (var marking in CustomMarkingRules.FromStored(list))
            {
                worn.Add(marking.Hash);
            }
        }

        var used = new List<string>();
        var found = new List<string>();
        var old = new List<string>();
        foreach (var art in loose)
        {
            if (worn.Contains(art.Hash))
            {
                if (art.UnusedSince != null)
                    used.Add(art.Hash);
            }
            else if (art.UnusedSince is not { } since)
            {
                found.Add(art.Hash);
            }
            else if (since <= now - keep)
            {
                old.Add(art.Hash);
            }
        }

        foreach (var batch in used.Chunk(CustomMarkingPurgeBatch))
        {
            await ctx.WolfgateCustomMarkingArt
                .Where(a => batch.Contains(a.Hash))
                .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.UnusedSince, (DateTime?) null), cancel);
        }

        foreach (var batch in found.Chunk(CustomMarkingPurgeBatch))
        {
            await ctx.WolfgateCustomMarkingArt
                .Where(a => batch.Contains(a.Hash))
                .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.UnusedSince, (DateTime?) now), cancel);
        }

        // Asked again as it deletes: a library may have taken a row up since the list was made.
        var deleted = 0;
        foreach (var batch in old.Chunk(CustomMarkingPurgeBatch))
        {
            deleted += await ctx.WolfgateCustomMarkingArt
                .Where(a => batch.Contains(a.Hash) && !a.Blocked && !ctx.WolfgateCustomMarking.Any(m => m.ArtHash == a.Hash))
                .ExecuteDeleteAsync(cancel);
        }

        return (found.Count, deleted);
    }

    public async Task<Guid?> SetCustomMarkingArtBlockedAsync(string hash, bool blocked, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var art = await db.DbContext.WolfgateCustomMarkingArt.SingleOrDefaultAsync(a => a.Hash == hash, cancel);
        if (art == null)
            return null;

        art.Blocked = blocked;
        await db.DbContext.SaveChangesAsync(cancel);
        return art.UploaderUserId;
    }
}
