using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.Companies;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

// Player-run companies. The company system keeps them in memory and writes each change through.
public abstract partial class ServerDbBase
{
    public async Task<List<WFStoredCompany>> LoadCompaniesAsync(CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var ctx = db.DbContext;
        var companies = await ctx.WolfgateCompany.AsNoTracking().Include(c => c.Ranks).ToListAsync(cancel);
        var members = await ctx.WolfgateCompanyMember.AsNoTracking()
            .Select(m => new WFStoredMember(m.ProfileId, m.CompanyId, m.RankId, m.JoinedAt, m.UnpaidSeconds, m.WithdrawnToday,
                m.WithdrawDay, m.Profile.Preference.UserId, m.Profile.CharacterName, m.Profile.Slot))
            .ToListAsync(cancel);
        var ledgers = new Dictionary<int, List<WolfgateCompanyLedger>>();
        foreach (var company in companies)
        {
            ledgers[company.Id] = await ctx.WolfgateCompanyLedger.AsNoTracking()
                .Where(l => l.CompanyId == company.Id)
                .OrderByDescending(l => l.Id)
                .Take(WFStoredCompany.LedgerCache)
                .ToListAsync(cancel);
        }
        var licences = await ctx.WolfgateCompanyLicence.AsNoTracking().ToListAsync(cancel);
        var applications = await ctx.WolfgateCompanyApplication.AsNoTracking()
            .OrderBy(a => a.Id)
            .Select(a => new WFStoredApplication(a.CompanyId, a.ProfileId, a.RankId, a.Message, a.Profile.Preference.UserId,
                a.Profile.CharacterName, a.Profile.Slot))
            .ToListAsync(cancel);
        return companies.Select(c => new WFStoredCompany(c, members.Where(m => m.CompanyId == c.Id).ToList(), ledgers[c.Id],
            licences.Where(l => l.CompanyId == c.Id).ToList(), applications.Where(a => a.CompanyId == c.Id).ToList())).ToList();
    }

    public async Task<Dictionary<int, int>> GetProfileIdsAsync(Guid userId, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.Profile.AsNoTracking()
            .Where(p => p.Preference.UserId == userId)
            .ToDictionaryAsync(p => p.Slot, p => p.Id, cancel);
    }

    /// <summary>Adds a company with its ranks and owner. Null when the name or tag is taken or the owner is already a member.</summary>
    public async Task<WolfgateCompany?> CreateCompanyAsync(WolfgateCompany company, int ownerProfileId, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var ctx = db.DbContext;
        var name = company.Name.ToLower();
        if (await ctx.WolfgateCompany.AnyAsync(c => c.Name.ToLower() == name || c.Tag == company.Tag, cancel) ||
            await ctx.WolfgateCompanyMember.AnyAsync(m => m.ProfileId == ownerProfileId, cancel))
            return null;
        company.Members = new List<WolfgateCompanyMember>
        {
            new() { ProfileId = ownerProfileId, JoinedAt = company.CreatedAt, WithdrawDay = company.CreatedAt.Date },
        };
        ctx.WolfgateCompany.Add(company);
        await ctx.SaveChangesAsync(cancel);
        return company;
    }

    /// <summary>Whether another company already uses this name or tag.</summary>
    public async Task<bool> CompanyNameTakenAsync(int companyId, string name, string tag, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var lower = name.ToLower();
        return await db.DbContext.WolfgateCompany.AnyAsync(c => c.Id != companyId && (c.Name.ToLower() == lower || c.Tag == tag), cancel);
    }

    /// <summary>Writes a company's own fields, not its balance (see <see cref="RecordCompanyTransactionAsync"/>).</summary>
    public async Task UpdateCompanyAsync(int id, string name, string tag, string description, string colour, int owner, int cut,
        bool recruiting, bool frozen, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var company = await db.DbContext.WolfgateCompany.SingleOrDefaultAsync(c => c.Id == id, cancel);
        if (company == null)
            return;
        company.Name = name;
        company.Tag = tag;
        company.Description = description;
        company.Colour = colour;
        company.OwnerProfileId = owner;
        company.Cut = cut;
        company.Recruiting = recruiting;
        company.Frozen = frozen;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    /// <summary>Writes the owner's own rank.</summary>
    public async Task SetCompanyOwnerRankAsync(int id, string title, string job, byte[]? icon, int version, string loadout,
        CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var company = await db.DbContext.WolfgateCompany.SingleOrDefaultAsync(c => c.Id == id, cancel);
        if (company == null)
            return;
        company.OwnerTitle = title;
        company.OwnerJob = job;
        company.OwnerIcon = icon;
        company.OwnerIconVersion = version;
        company.OwnerLoadout = loadout;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task SetCompanyOpenRankAsync(int id, int? rankId, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var company = await db.DbContext.WolfgateCompany.SingleOrDefaultAsync(c => c.Id == id, cancel);
        if (company == null)
            return;
        company.OpenRankId = rankId;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task SetCompanyPrivateAsync(int id, bool hidden, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var company = await db.DbContext.WolfgateCompany.SingleOrDefaultAsync(c => c.Id == id, cancel);
        if (company == null)
            return;
        company.Private = hidden;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    /// <summary>Adds or replaces a character's application to a company.</summary>
    public async Task SaveCompanyApplicationAsync(int companyId, int profileId, int? rankId, string message, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var ctx = db.DbContext;
        if (!await ctx.WolfgateCompany.AnyAsync(c => c.Id == companyId, cancel) || !await ctx.Profile.AnyAsync(p => p.Id == profileId, cancel))
            return;
        var row = await ctx.WolfgateCompanyApplication.SingleOrDefaultAsync(a => a.CompanyId == companyId && a.ProfileId == profileId, cancel);
        if (row == null)
        {
            row = new WolfgateCompanyApplication { CompanyId = companyId, ProfileId = profileId, CreatedAt = DateTime.UtcNow };
            ctx.WolfgateCompanyApplication.Add(row);
        }
        row.RankId = rankId;
        row.Message = message;
        await ctx.SaveChangesAsync(cancel);
    }

    /// <summary>Removes a character's application to one company, or to every company when none is given.</summary>
    public async Task DeleteCompanyApplicationsAsync(int profileId, int? companyId, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var ctx = db.DbContext;
        var rows = await ctx.WolfgateCompanyApplication
            .Where(a => a.ProfileId == profileId && (companyId == null || a.CompanyId == companyId))
            .ToListAsync(cancel);
        if (rows.Count == 0)
            return;
        ctx.WolfgateCompanyApplication.RemoveRange(rows);
        await ctx.SaveChangesAsync(cancel);
    }

    /// <summary>Writes a company's logo and art lock.</summary>
    public async Task SetCompanyArtAsync(int id, byte[]? logo, int version, bool locked, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var company = await db.DbContext.WolfgateCompany.SingleOrDefaultAsync(c => c.Id == id, cancel);
        if (company == null)
            return;
        company.Logo = logo;
        company.LogoVersion = version;
        company.ArtLocked = locked;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    /// <summary>Adds a licence and returns its id.</summary>
    public async Task<int> AddCompanyLicenceAsync(WolfgateCompanyLicence licence, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        db.DbContext.WolfgateCompanyLicence.Add(licence);
        await db.DbContext.SaveChangesAsync(cancel);
        return licence.Id;
    }

    public async Task DeleteCompanyLicenceAsync(int id, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var licence = await db.DbContext.WolfgateCompanyLicence.SingleOrDefaultAsync(l => l.Id == id, cancel);
        if (licence == null)
            return;
        db.DbContext.WolfgateCompanyLicence.Remove(licence);
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task SetCompanyLicenceNameAsync(int id, string name, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var licence = await db.DbContext.WolfgateCompanyLicence.SingleOrDefaultAsync(l => l.Id == id, cancel);
        if (licence == null)
            return;
        licence.ShipName = name;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task SetCompanyLaunchFeeAsync(int id, int fee, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var company = await db.DbContext.WolfgateCompany.SingleOrDefaultAsync(c => c.Id == id, cancel);
        if (company == null)
            return;
        company.LaunchFee = fee;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task DeleteCompanyAsync(int id, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var company = await db.DbContext.WolfgateCompany.SingleOrDefaultAsync(c => c.Id == id, cancel);
        if (company == null)
            return;
        db.DbContext.WolfgateCompany.Remove(company);
        await db.DbContext.SaveChangesAsync(cancel);
    }

    /// <summary>Adds or changes a rank; returns its id.</summary>
    public async Task<int> SaveCompanyRankAsync(WolfgateCompanyRank rank, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var ctx = db.DbContext;
        if (rank.Id == 0)
            ctx.WolfgateCompanyRank.Add(rank);
        else
            ctx.WolfgateCompanyRank.Update(rank);
        await ctx.SaveChangesAsync(cancel);
        return rank.Id;
    }

    public async Task DeleteCompanyRankAsync(int rankId, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var rank = await db.DbContext.WolfgateCompanyRank.SingleOrDefaultAsync(r => r.Id == rankId, cancel);
        if (rank == null)
            return;
        db.DbContext.WolfgateCompanyRank.Remove(rank);
        await db.DbContext.SaveChangesAsync(cancel);
    }

    /// <summary>Adds a member. False when the character already belongs to a company or no longer exists.</summary>
    public async Task<bool> AddCompanyMemberAsync(WolfgateCompanyMember member, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var ctx = db.DbContext;
        if (await ctx.WolfgateCompanyMember.AnyAsync(m => m.ProfileId == member.ProfileId, cancel) ||
            !await ctx.Profile.AnyAsync(p => p.Id == member.ProfileId, cancel))
            return false;
        ctx.WolfgateCompanyMember.Add(member);
        await ctx.SaveChangesAsync(cancel);
        return true;
    }

    public async Task UpdateCompanyMemberAsync(int profileId, int? rankId, int unpaidSeconds, int withdrawnToday, DateTime withdrawDay,
        CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var member = await db.DbContext.WolfgateCompanyMember.SingleOrDefaultAsync(m => m.ProfileId == profileId, cancel);
        if (member == null)
            return;
        member.RankId = rankId;
        member.UnpaidSeconds = unpaidSeconds;
        member.WithdrawnToday = withdrawnToday;
        member.WithdrawDay = withdrawDay;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task RemoveCompanyMemberAsync(int profileId, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var member = await db.DbContext.WolfgateCompanyMember.SingleOrDefaultAsync(m => m.ProfileId == profileId, cancel);
        if (member == null)
            return;
        db.DbContext.WolfgateCompanyMember.Remove(member);
        await db.DbContext.SaveChangesAsync(cancel);
    }

    /// <summary>Sets the balance and appends the ledger row that explains it, in one save.</summary>
    public async Task RecordCompanyTransactionAsync(WolfgateCompanyLedger entry, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var ctx = db.DbContext;
        var company = await ctx.WolfgateCompany.SingleOrDefaultAsync(c => c.Id == entry.CompanyId, cancel);
        if (company == null)
            return;
        company.Balance = entry.BalanceAfter;
        ctx.WolfgateCompanyLedger.Add(entry);
        await ctx.SaveChangesAsync(cancel);
    }

    public async Task<List<WolfgateCompanyLedger>> GetCompanyLedgerAsync(int companyId, int count, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.WolfgateCompanyLedger.AsNoTracking()
            .Where(l => l.CompanyId == companyId)
            .OrderByDescending(l => l.Id)
            .Take(count)
            .ToListAsync(cancel);
    }
}
