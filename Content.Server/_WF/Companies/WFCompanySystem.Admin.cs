using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Content.Shared._WF.Companies;

namespace Content.Server._WF.Companies;

public sealed partial class WFCompanySystem
{
    /// <summary>Companies by id or by name or tag, ignoring case; for admin commands.</summary>
    public int? FindCompany(string query)
    {
        if (int.TryParse(query, out var id) && _companies.ContainsKey(id))
            return id;
        return _companies.Values.FirstOrDefault(company => string.Equals(company.Name, query, StringComparison.OrdinalIgnoreCase) ||
                                                           string.Equals(company.Tag, query, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    /// <summary>Every company's id, tag and name.</summary>
    public IEnumerable<(int Id, string Tag, string Name)> ListCompanies() =>
        _companies.Values.OrderBy(company => company.Id).Select(company => (company.Id, company.Tag, company.Name));

    /// <summary>The company's account balance, or null when there is no such company.</summary>
    public long? GetBalance(int id) => _companies.TryGetValue(id, out var company) ? company.Balance : null;

    /// <summary>The company the character belongs to, or null.</summary>
    public int? GetCompanyOf(int profileId) => CompanyOf(profileId)?.Id;

    /// <summary>A plain-text summary for admins.</summary>
    public string Describe(int id)
    {
        if (!_companies.TryGetValue(id, out var company))
            return string.Empty;
        var text = new StringBuilder();
        text.AppendLine(Loc.GetString("cmd-companyinfo-header", ("name", company.Name), ("tag", company.Tag), ("id", company.Id),
            ("balance", company.Balance), ("cut", company.Cut), ("frozen", company.Frozen.ToString())));
        foreach (var member in company.Members.Values.OrderBy(member => RankOrder(company, member) ?? -1))
        {
            var rank = TitleOf(company, member);
            text.AppendLine(Loc.GetString("cmd-companyinfo-member", ("profile", member.Profile), ("name", member.Name),
                ("user", member.User.ToString()), ("rank", rank)));
        }
        return text.ToString().TrimEnd();
    }

    /// <summary>Newest ledger rows as text, read from the database.</summary>
    public async Task<string> DescribeLedger(int id, int count)
    {
        var rows = await _db.GetCompanyLedgerAsync(id, count);
        var text = new StringBuilder();
        foreach (var row in rows)
        {
            text.AppendLine(Loc.GetString("cmd-companyledger-row", ("time", row.Time.ToString("u")),
                ("kind", ((WFCompanyLedgerKind) row.Kind).ToString()), ("amount", row.Amount), ("balance", row.BalanceAfter),
                ("actor", row.ActorName), ("note", row.Note)));
        }
        return text.ToString().TrimEnd();
    }

    public bool SetFrozen(int id, bool frozen)
    {
        if (!_companies.TryGetValue(id, out var company))
            return false;
        company.Frozen = frozen;
        PersistCompany(company);
        Touch(company);
        return true;
    }

    /// <summary>An admin correction to the balance; it can't overdraw and works while frozen.</summary>
    public bool AdminAdjust(int id, long amount, string admin, string reason)
    {
        if (!_companies.TryGetValue(id, out var company) ||
            !Transact(company, WFCompanyLedgerKind.AdminAdjust, amount, null, admin, reason, ignoreFrozen: true))
            return false;
        Touch(company);
        return true;
    }

    /// <summary>Renames a company; false if the name or tag is invalid or taken.</summary>
    public bool AdminRename(int id, string name, string tag)
    {
        if (!_companies.TryGetValue(id, out var company) || !WFCompanyPolicy.TryNormalizeName(name, out var cleanName) ||
            !WFCompanyPolicy.TryNormalizeTag(tag, out var cleanTag) ||
            _companies.Values.Any(other => other != company &&
                (string.Equals(other.Name, cleanName, StringComparison.OrdinalIgnoreCase) || other.Tag == cleanTag)))
            return false;
        company.Name = cleanName;
        company.Tag = cleanTag;
        PersistCompany(company);
        Touch(company);
        return true;
    }

    /// <summary>Hands the company to one of its members.</summary>
    public bool AdminTransfer(int id, int profile)
    {
        if (!_companies.TryGetValue(id, out var company) || !company.Members.TryGetValue(profile, out var heir) ||
            !company.Members.TryGetValue(company.Owner, out var owner))
            return false;
        if (heir != owner)
        {
            owner.Rank = company.Ranks[0].Id;
            heir.Rank = null;
            company.Owner = profile;
            PersistMember(owner);
            PersistMember(heir);
            PersistCompany(company);
        }
        Touch(company);
        return true;
    }

    /// <summary>Replaces or clears the logo and sets the art lock; every client refetches on the new version.</summary>
    private void SetLogo(Company company, byte[]? logo, bool locked)
    {
        company.Logo = logo;
        company.LogoVersion++;
        company.ArtLocked = locked;
        Track(_db.SetCompanyArtAsync(company.Id, logo, company.LogoVersion, locked));
        foreach (var session in _players.Sessions)
            SendState(session);
    }

    /// <summary>Wipes a company's logo, or locks or unlocks its art.</summary>
    public bool AdminArt(int id, bool wipe, bool? locked)
    {
        if (!_companies.TryGetValue(id, out var company))
            return false;
        SetLogo(company, wipe ? null : company.Logo, locked ?? company.ArtLocked);
        return true;
    }

    public bool AdminDisband(int id)
    {
        if (!_companies.TryGetValue(id, out var company))
            return false;
        var users = company.Members.Values.Select(member => member.User).ToArray();
        RemoveCompany(company);
        Touch(null, users);
        return true;
    }
}
