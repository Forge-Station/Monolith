using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._Forge.Company;
using Content.Shared._Mono.Company;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Company;

public sealed partial class CompanyManager
{
    private readonly List<CompanyJobRecord> _jobs = new();
    private readonly SemaphoreSlim _jobGate = new(1, 1);

    public IReadOnlyList<CompanyJobData> VisibleJobs(string companyId)
    {
        var list = new List<CompanyJobData>();
        foreach (var job in _jobs)
        {
            var takenByUs = job.TakerId == companyId;
            if (job.TakerId != null && !takenByUs)
                continue;

            list.Add(ToData(job, companyId));
        }

        return list;
    }

    public async Task<string?> TryPostCreditJob(ProtoId<CompanyPrototype> company, string? description, int amount, NetUserId actor)
    {
        description = CleanJobDescription(description);
        if (description == null)
            return "company-job-desc";
        if (amount < CompanyOrgLimits.MinJobCredit || amount > CompanyOrgLimits.MaxBankTransaction)
            return "company-job-amount";

        await _jobGate.WaitAsync();
        try
        {
            if (OpenJobCount(company) >= CompanyOrgLimits.MaxOpenJobsPerCompany)
                return "company-job-limit";
            if (!await TryWithdrawBank(company, amount, actor))
                return "company-job-funds";

            var job = new CompanyJobRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                PosterId = company.Id,
                Description = description,
                RewardKind = CompanyJobReward.Credits,
                RewardAmount = amount,
            };
            _jobs.Add(job);
            await _db.UpsertCompanyJob(job);
            await AddLog(company, CompanyLogType.ContractPosted, $"Posted credit job {amount}: {description}", actor);
            return null;
        }
        finally
        {
            _jobGate.Release();
        }
    }

    public async Task<string?> TryPostItemJob(
        ProtoId<CompanyPrototype> company,
        string? description,
        string protoId,
        int count,
        NetUserId actor)
    {
        description = CleanJobDescription(description);
        if (description == null)
            return "company-job-desc";
        if (string.IsNullOrWhiteSpace(protoId) || count < 1 || count > CompanyOrgLimits.MaxJobItemCount)
            return "company-job-item";

        await _jobGate.WaitAsync();
        try
        {
            if (OpenJobCount(company) >= CompanyOrgLimits.MaxOpenJobsPerCompany)
                return "company-job-limit";

            var job = new CompanyJobRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                PosterId = company.Id,
                Description = description,
                RewardKind = CompanyJobReward.Items,
                RewardAmount = count,
                RewardProto = protoId,
            };
            _jobs.Add(job);
            await _db.UpsertCompanyJob(job);
            await AddLog(company, CompanyLogType.ContractPosted, $"Posted item job {protoId} x{count}: {description}", actor);
            return null;
        }
        finally
        {
            _jobGate.Release();
        }
    }

    public async Task<string?> TryAcceptJob(ProtoId<CompanyPrototype> company, string? jobId, NetUserId actor)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return "company-job-gone";

        await _jobGate.WaitAsync();
        try
        {
            var job = FindJob(jobId);
            if (job == null || job.TakerId != null)
                return "company-job-gone";
            if (job.PosterId == company.Id)
                return "company-job-own";
            if (_jobs.Any(other => other.TakerId == company.Id))
                return "company-job-busy";

            job.TakerId = company.Id;
            await _db.UpsertCompanyJob(job);
            await AddLog(company, CompanyLogType.ContractPosted, $"Accepted job {job.Id} from {job.PosterId}", actor);
            return null;
        }
        finally
        {
            _jobGate.Release();
        }
    }

    public async Task<string?> TryAbandonJob(ProtoId<CompanyPrototype> company, NetUserId actor)
    {
        await _jobGate.WaitAsync();
        try
        {
            var job = _jobs.FirstOrDefault(other => other.TakerId == company.Id);
            if (job == null)
                return "company-job-gone";

            job.TakerId = null;
            await _db.UpsertCompanyJob(job);
            await AddLog(company, CompanyLogType.ContractPosted, $"Abandoned job {job.Id}", actor);
            return null;
        }
        finally
        {
            _jobGate.Release();
        }
    }

    /// <summary>
    /// Cancels an open job posted by this company and returns it so an item reward can be spawned back.
    /// </summary>
    public async Task<(string? Error, CompanyJobRecord? Refund)> TryCancelJob(
        ProtoId<CompanyPrototype> company,
        string? jobId,
        NetUserId actor)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return ("company-job-gone", null);

        await _jobGate.WaitAsync();
        try
        {
            var job = FindJob(jobId);
            if (job == null || job.PosterId != company.Id)
                return ("company-job-gone", null);
            if (job.TakerId != null)
                return ("company-job-already", null);

            _jobs.Remove(job);
            await _db.DeleteCompanyJob(job.Id);
            if (job.RewardKind == CompanyJobReward.Credits && job.RewardAmount > 0)
                await TryDepositBank(company, job.RewardAmount, actor);

            await AddLog(company, CompanyLogType.ContractPosted, $"Cancelled job {job.Id}", actor);
            return (null, job.RewardKind == CompanyJobReward.Items ? job : null);
        }
        finally
        {
            _jobGate.Release();
        }
    }

    /// <summary>
    /// Removes the job this company has taken. The caller pays the reward.
    /// </summary>
    public async Task<CompanyJobRecord?> TryFinishJob(ProtoId<CompanyPrototype> company, NetUserId actor)
    {
        await _jobGate.WaitAsync();
        try
        {
            var job = _jobs.FirstOrDefault(other => other.TakerId == company.Id);
            if (job == null)
                return null;

            _jobs.Remove(job);
            await _db.DeleteCompanyJob(job.Id);
            await AddLog(company, CompanyLogType.ContractCompleted, $"Finished job {job.Id} from {job.PosterId}", actor);
            return job;
        }
        finally
        {
            _jobGate.Release();
        }
    }

    private async Task LoadJobs()
    {
        await _db.EnsureCompanyJobTable();
        _jobs.Clear();
        _jobs.AddRange(await _db.GetCompanyJobs());
    }

    private CompanyJobRecord? FindJob(string jobId)
    {
        return _jobs.FirstOrDefault(job => job.Id == jobId);
    }

    private int OpenJobCount(ProtoId<CompanyPrototype> company)
    {
        var count = 0;
        foreach (var job in _jobs)
        {
            if (job.PosterId == company.Id && job.TakerId == null)
                count++;
        }

        return count;
    }

    private static string? CleanJobDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        var text = description.Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (text.Length > CompanyOrgLimits.MaxJobDescriptionLength)
            text = text[..CompanyOrgLimits.MaxJobDescriptionLength].Trim();

        return text.Length == 0 ? null : text;
    }

    private CompanyJobData ToData(CompanyJobRecord job, string companyId)
    {
        var name = _proto.TryIndex<CompanyPrototype>(job.PosterId, out var proto) ? proto.Name : job.PosterId;
        return new CompanyJobData
        {
            Id = job.Id,
            PosterId = job.PosterId,
            PosterName = name,
            Description = job.Description,
            RewardKind = job.RewardKind,
            RewardAmount = job.RewardAmount,
            RewardProto = job.RewardProto,
            Ours = job.PosterId == companyId,
            TakenByUs = job.TakerId == companyId,
        };
    }
}
