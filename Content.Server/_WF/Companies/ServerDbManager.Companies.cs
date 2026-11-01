using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.Companies;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    /// <summary>Every company with its ranks, members and newest ledger rows.</summary>
    Task<List<WFStoredCompany>> LoadCompaniesAsync(CancellationToken cancel = default);

    /// <summary>A player's character profile ids by slot.</summary>
    Task<Dictionary<int, int>> GetProfileIdsAsync(Guid userId, CancellationToken cancel = default);

    Task<WolfgateCompany?> CreateCompanyAsync(WolfgateCompany company, int ownerProfileId, CancellationToken cancel = default);
    Task<bool> CompanyNameTakenAsync(int companyId, string name, string tag, CancellationToken cancel = default);
    Task UpdateCompanyAsync(int id, string name, string tag, string description, string colour, int owner, int cut,
        bool recruiting, bool frozen, CancellationToken cancel = default);
    Task DeleteCompanyAsync(int id, CancellationToken cancel = default);
    Task SetCompanyOwnerRankAsync(int id, string title, string job, byte[]? icon, int version, string loadout, CancellationToken cancel = default);
    Task SetCompanyOpenRankAsync(int id, int? rankId, CancellationToken cancel = default);
    Task SetCompanyPrivateAsync(int id, bool hidden, CancellationToken cancel = default);
    Task SaveCompanyApplicationAsync(int companyId, int profileId, int? rankId, string message, CancellationToken cancel = default);
    Task DeleteCompanyApplicationsAsync(int profileId, int? companyId, CancellationToken cancel = default);
    Task<int> AddCompanyLicenceAsync(WolfgateCompanyLicence licence, CancellationToken cancel = default);
    Task DeleteCompanyLicenceAsync(int id, CancellationToken cancel = default);
    Task SetCompanyLicenceNameAsync(int id, string name, CancellationToken cancel = default);
    Task SetCompanyLaunchFeeAsync(int id, int fee, CancellationToken cancel = default);
    Task SetCompanyArtAsync(int id, byte[]? logo, int version, bool locked, CancellationToken cancel = default);
    Task<int> SaveCompanyRankAsync(WolfgateCompanyRank rank, CancellationToken cancel = default);
    Task DeleteCompanyRankAsync(int rankId, CancellationToken cancel = default);
    Task<bool> AddCompanyMemberAsync(WolfgateCompanyMember member, CancellationToken cancel = default);
    Task UpdateCompanyMemberAsync(int profileId, int? rankId, int unpaidSeconds, int withdrawnToday, DateTime withdrawDay,
        CancellationToken cancel = default);
    Task RemoveCompanyMemberAsync(int profileId, CancellationToken cancel = default);
    Task RecordCompanyTransactionAsync(WolfgateCompanyLedger entry, CancellationToken cancel = default);
    Task<List<WolfgateCompanyLedger>> GetCompanyLedgerAsync(int companyId, int count, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<List<WFStoredCompany>> LoadCompaniesAsync(CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.LoadCompaniesAsync(cancel));
    }

    public Task<Dictionary<int, int>> GetProfileIdsAsync(Guid userId, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetProfileIdsAsync(userId, cancel));
    }

    public Task<WolfgateCompany?> CreateCompanyAsync(WolfgateCompany company, int ownerProfileId, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.CreateCompanyAsync(company, ownerProfileId, cancel));
    }

    public Task<bool> CompanyNameTakenAsync(int companyId, string name, string tag, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.CompanyNameTakenAsync(companyId, name, tag, cancel));
    }

    public Task UpdateCompanyAsync(int id, string name, string tag, string description, string colour, int owner, int cut,
        bool recruiting, bool frozen, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.UpdateCompanyAsync(id, name, tag, description, colour, owner, cut, recruiting, frozen, cancel));
    }

    public Task SetCompanyArtAsync(int id, byte[]? logo, int version, bool locked, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SetCompanyArtAsync(id, logo, version, locked, cancel));
    }

    public Task<int> AddCompanyLicenceAsync(WolfgateCompanyLicence licence, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.AddCompanyLicenceAsync(licence, cancel));
    }

    public Task DeleteCompanyLicenceAsync(int id, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.DeleteCompanyLicenceAsync(id, cancel));
    }

    public Task SetCompanyLicenceNameAsync(int id, string name, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SetCompanyLicenceNameAsync(id, name, cancel));
    }

    public Task SetCompanyLaunchFeeAsync(int id, int fee, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SetCompanyLaunchFeeAsync(id, fee, cancel));
    }

    public Task SetCompanyOwnerRankAsync(int id, string title, string job, byte[]? icon, int version, string loadout,
        CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SetCompanyOwnerRankAsync(id, title, job, icon, version, loadout, cancel));
    }

    public Task SetCompanyOpenRankAsync(int id, int? rankId, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SetCompanyOpenRankAsync(id, rankId, cancel));
    }

    public Task SetCompanyPrivateAsync(int id, bool hidden, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SetCompanyPrivateAsync(id, hidden, cancel));
    }

    public Task SaveCompanyApplicationAsync(int companyId, int profileId, int? rankId, string message, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SaveCompanyApplicationAsync(companyId, profileId, rankId, message, cancel));
    }

    public Task DeleteCompanyApplicationsAsync(int profileId, int? companyId, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.DeleteCompanyApplicationsAsync(profileId, companyId, cancel));
    }

    public Task DeleteCompanyAsync(int id, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.DeleteCompanyAsync(id, cancel));
    }

    public Task<int> SaveCompanyRankAsync(WolfgateCompanyRank rank, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SaveCompanyRankAsync(rank, cancel));
    }

    public Task DeleteCompanyRankAsync(int rankId, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.DeleteCompanyRankAsync(rankId, cancel));
    }

    public Task<bool> AddCompanyMemberAsync(WolfgateCompanyMember member, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.AddCompanyMemberAsync(member, cancel));
    }

    public Task UpdateCompanyMemberAsync(int profileId, int? rankId, int unpaidSeconds, int withdrawnToday, DateTime withdrawDay,
        CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.UpdateCompanyMemberAsync(profileId, rankId, unpaidSeconds, withdrawnToday, withdrawDay, cancel));
    }

    public Task RemoveCompanyMemberAsync(int profileId, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.RemoveCompanyMemberAsync(profileId, cancel));
    }

    public Task RecordCompanyTransactionAsync(WolfgateCompanyLedger entry, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.RecordCompanyTransactionAsync(entry, cancel));
    }

    public Task<List<WolfgateCompanyLedger>> GetCompanyLedgerAsync(int companyId, int count, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetCompanyLedgerAsync(companyId, count, cancel));
    }
}
