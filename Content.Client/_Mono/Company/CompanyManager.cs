using System.Collections.Generic;
using System.Linq;
using Content.Shared._Forge.Company;
using Content.Shared._Mono.CCVar;
using Content.Shared._Mono.Company;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Client._Mono.Company;

public sealed partial class CompanyManager
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private HashSet<ProtoId<CompanyPrototype>> _whitelist = new();
    private HashSet<ProtoId<CompanyPrototype>> _recruitingOpen = new();
    private readonly Dictionary<string, List<(string OtherId, CompanyRelationKind Kind)>> _relations = new();

    public event Action? CatalogUpdated;

    public void Initialize()
    {
        _net.RegisterNetMessage<MsgCompanyWhitelist>(OnWhitelistMsg);
    }

    private void OnWhitelistMsg(MsgCompanyWhitelist msg)
    {
        _whitelist = msg.Whitelist;
        _recruitingOpen = msg.RecruitingOpen;
        _relations.Clear();
        foreach (var (a, b, kind) in msg.Relations)
        {
            AddRelation(a, b, kind);
            AddRelation(b, a, kind);
        }

        CatalogUpdated?.Invoke();
    }

    private void AddRelation(string company, string other, CompanyRelationKind kind)
    {
        if (!_relations.TryGetValue(company, out var list))
        {
            list = new List<(string, CompanyRelationKind)>();
            _relations[company] = list;
        }

        list.Add((other, kind));
    }

    public bool IsPlayerWhitelisted(ProtoId<CompanyPrototype> company)
    {
        return _whitelist.Contains(company);
    }

    public bool IsRecruitingOpen(ProtoId<CompanyPrototype> company)
    {
        return _recruitingOpen.Contains(company);
    }

    public bool IsAllowed(ProtoId<CompanyPrototype> company)
    {
        if (!_config.GetCVar(MonoCVars.CompanyWhitelist))
            return true;

        if (!_proto.Resolve(company, out var companyPrototype) ||
            !companyPrototype.Whitelisted)
        {
            return true;
        }

        if (IsRecruitingOpen(company))
            return true;

        return IsPlayerWhitelisted(company);
    }

    /// <summary>Whether the player may set this company on their profile (join / stay).</summary>
    public bool CanJoin(ProtoId<CompanyPrototype> company)
    {
        if (company == "None")
            return true;

        return IsAllowed(company);
    }

    public IReadOnlyList<(string OtherId, CompanyRelationKind Kind)> GetRelations(ProtoId<CompanyPrototype> company)
    {
        return _relations.TryGetValue(company.Id, out var list)
            ? list
            : Array.Empty<(string, CompanyRelationKind)>();
    }

    public string FormatDiplomacy(ProtoId<CompanyPrototype> company)
    {
        if (company == "None")
            return string.Empty;

        var wars = new List<string>();
        var allies = new List<string>();
        foreach (var (otherId, kind) in GetRelations(company))
        {
            if (!_proto.TryIndex<CompanyPrototype>(otherId, out var other) || other.Hidden)
                continue;

            if (kind == CompanyRelationKind.War)
                wars.Add(other.Name);
            else if (kind == CompanyRelationKind.Alliance)
                allies.Add(other.Name);
        }

        wars.Sort(StringComparer.Ordinal);
        allies.Sort(StringComparer.Ordinal);

        if (wars.Count == 0 && allies.Count == 0)
            return $"[color=#9aa0a6]{Loc.GetString("company-catalog-diplomacy-none")}[/color]";

        var parts = new List<string>();
        if (wars.Count > 0)
        {
            parts.Add(
                $"[color=#e06060][bold]{Loc.GetString("company-catalog-diplomacy-wars", ("list", string.Join(", ", wars)))}[/bold][/color]");
        }

        if (allies.Count > 0)
        {
            parts.Add(
                $"[color=#5dba8c][bold]{Loc.GetString("company-catalog-diplomacy-allies", ("list", string.Join(", ", allies)))}[/bold][/color]");
        }

        return string.Join("\n", parts);
    }
}
