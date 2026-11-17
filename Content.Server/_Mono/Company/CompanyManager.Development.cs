using System.Linq;
using System.Threading.Tasks;
using Content.Shared._Forge.Company;
using Content.Shared._Mono.Company;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Company;

public sealed partial class CompanyManager
{
    private readonly Dictionary<string, HashSet<string>> _projects = new();
    private readonly Dictionary<string, (string Id, bool Funded)> _contracts = new();
    private readonly Dictionary<string, long> _research = new();

    public bool HasProject(ProtoId<CompanyPrototype> company, string projectId)
    {
        return _projects.TryGetValue(company, out var owned) && owned.Contains(projectId);
    }

    public int MaxMembers(ProtoId<CompanyPrototype> company)
    {
        var extra = ResearchAmount(company, CompanyProjectEffect.MemberSlots);
        if (_projects.TryGetValue(company, out var owned))
        {
            foreach (var id in owned)
            {
                if (_proto.TryIndex<CompanyProjectPrototype>(id, out var project) &&
                    project.Effect == CompanyProjectEffect.MemberSlots)
                    extra += project.Amount;
            }
        }

        return CompanyOrgLimits.MaxMembersPerCompany + extra;
    }

    public int RelationCooldownSeconds(ProtoId<CompanyPrototype> company)
    {
        var seconds = CompanyOrgLimits.RelationCooldownSeconds;
        if (_projects.TryGetValue(company, out var owned))
        {
            foreach (var id in owned)
            {
                if (_proto.TryIndex<CompanyProjectPrototype>(id, out var project) &&
                    project.Effect == CompanyProjectEffect.RelationCooldown)
                    seconds = Math.Max(1, project.Amount);
            }
        }

        seconds -= ResearchAmount(company, CompanyProjectEffect.RelationCooldown);
        return Math.Max(CompanyResearch.MinRelationCooldown, seconds);
    }

    public long GetResearchArchive(ProtoId<CompanyPrototype> company)
    {
        return _research.GetValueOrDefault(company.Id);
    }

    public int CreditResearch(ProtoId<CompanyPrototype> company, int technologyCost)
    {
        if (technologyCost <= 0 || company == "None")
            return 0;

        var gain = Math.Max(1, technologyCost * CompanyResearch.ArchivePercent / 100);
        _research[company.Id] = GetResearchArchive(company) + gain;
        _ = _db.AddCompanyResearchPoints(company.Id, gain);
        return gain;
    }

    public int ScaleDiplomacy(ProtoId<CompanyPrototype> company, int cost)
    {
        var percent = Math.Clamp(ResearchAmount(company, CompanyProjectEffect.DiplomacyDiscount), 0, CompanyResearch.MaxDiscountPercent);
        if (percent <= 0)
            return cost;
        return Math.Max(1, cost * (100 - percent) / 100);
    }

    private int ResearchAmount(ProtoId<CompanyPrototype> company, CompanyProjectEffect effect)
    {
        var points = GetResearchArchive(company);
        var total = 0;
        foreach (var tier in CompanyResearch.Tiers)
        {
            if (points >= tier.Threshold && tier.Effect == effect)
                total += tier.Amount;
        }

        return total;
    }

    public int Priced(ProtoId<CompanyPrototype> company, int cost, CompanyProjectEffect discount)
    {
        if (cost <= 0)
            return cost;

        var percent = ResearchAmount(company, discount);
        if (_projects.TryGetValue(company, out var owned))
        {
            foreach (var id in owned)
            {
                if (_proto.TryIndex<CompanyProjectPrototype>(id, out var project) && project.Effect == discount)
                    percent += Math.Clamp(project.Amount, 0, 90);
            }
        }

        percent = Math.Clamp(percent, 0, CompanyResearch.MaxDiscountPercent);
        if (percent <= 0)
            return cost;

        return Math.Max(1, cost * (100 - percent) / 100);
    }

    public bool TryGetContract(ProtoId<CompanyPrototype> company, out string contractId, out bool funded)
    {
        if (_contracts.TryGetValue(company, out var contract))
        {
            contractId = contract.Id;
            funded = contract.Funded;
            return true;
        }

        contractId = string.Empty;
        funded = false;
        return false;
    }

    public async Task<bool> TryUnlockProject(ProtoId<CompanyPrototype> company, string projectId, NetUserId? actor)
    {
        if (!_proto.TryIndex<CompanyProjectPrototype>(projectId, out var project))
            return false;
        if (HasProject(company, projectId))
            return false;
        if (!await TryWithdrawBank(company, project.Cost, actor))
            return false;

        if (!_projects.TryGetValue(company, out var owned))
        {
            owned = new HashSet<string>();
            _projects[company] = owned;
        }

        owned.Add(projectId);
        await SaveDevelopment(company);
        await AddLog(company, CompanyLogType.ProjectUnlocked, $"Unlocked {projectId} for {project.Cost}", actor);
        return true;
    }

    public async Task<bool> TrySetContract(ProtoId<CompanyPrototype> company, string contractId, bool funded, int escrow, NetUserId? actor)
    {
        if (TryGetContract(company, out _, out _))
            return false;
        if (!_proto.TryIndex<CompanyContractPrototype>(contractId, out var contract))
            return false;

        if (funded)
        {
            if (escrow != contract.Reward)
                return false;
            if (!await TryWithdrawBank(company, contract.Reward, actor))
                return false;
        }

        _contracts[company] = (contractId, funded);
        await SaveDevelopment(company);
        await AddLog(company, CompanyLogType.ContractPosted, funded
            ? $"Funded contract {contractId} for {contract.Reward}"
            : $"Accepted market contract {contractId}", actor);
        return true;
    }

    public async Task<bool> TryClearContract(ProtoId<CompanyPrototype> company, bool refund, NetUserId? actor)
    {
        if (!TryGetContract(company, out var contractId, out var funded))
            return false;

        _contracts.Remove(company);
        await SaveDevelopment(company);

        if (refund && funded && _proto.TryIndex<CompanyContractPrototype>(contractId, out var contract))
            await TryDepositBank(company, contract.Reward, actor);

        return true;
    }

    public void ClearContractMemory(ProtoId<CompanyPrototype> company)
    {
        _contracts.Remove(company);
    }

    public async Task SaveDevelopment(ProtoId<CompanyPrototype> company)
    {
        var projects = _projects.TryGetValue(company, out var owned)
            ? string.Join(',', owned.OrderBy(id => id))
            : string.Empty;
        string? contractId = null;
        var funded = false;
        if (_contracts.TryGetValue(company, out var contract))
        {
            contractId = contract.Id;
            funded = contract.Funded;
        }

        await _db.SetCompanyDevelopment(company, projects, contractId, funded);
    }

    private async Task LoadDevelopment()
    {
        await _db.EnsureCompanyDevelopmentTable();
        foreach (var proto in _proto.EnumeratePrototypes<CompanyPrototype>())
        {
            if (proto.ID == "None")
                continue;

            var record = await _db.GetCompanyDevelopment(proto.ID);
            var owned = new HashSet<string>();
            if (!string.IsNullOrWhiteSpace(record.Projects))
            {
                foreach (var id in record.Projects.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (_proto.HasIndex<CompanyProjectPrototype>(id))
                        owned.Add(id);
                }
            }

            _projects[proto.ID] = owned;
            _research[proto.ID] = Math.Max(0, record.ResearchPoints);
            if (!string.IsNullOrWhiteSpace(record.ContractId) &&
                _proto.HasIndex<CompanyContractPrototype>(record.ContractId))
                _contracts[proto.ID] = (record.ContractId, record.ContractFunded);
        }
    }
}
