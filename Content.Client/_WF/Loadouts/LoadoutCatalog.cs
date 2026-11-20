using System.Linq;
using System.Text;
using Content.Client._WF.Gags;
using Content.Shared.Clothing;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WF.Loadouts;

/// <summary>One loadout as the window lists it inside a group.</summary>
public sealed class LoadoutEntry
{
    public readonly LoadoutGroupView Group;
    public readonly LoadoutPrototype Proto;

    public string Name = string.Empty;
    public string Description = string.Empty;

    /// <summary>Entity drawn as the icon, null for loadouts with nothing to show such as ranks.</summary>
    public EntProtoId? Icon;

    /// <summary>Lower-case text a search is matched against: <see cref="NameText"/> plus the description.</summary>
    public string SearchText = string.Empty;

    /// <summary>Lower-case name, group and category; a match here ranks above one in the description.</summary>
    public string NameText = string.Empty;

    /// <summary>The character can't take this loadout; <see cref="Reason"/> says why.</summary>
    public bool Locked;
    public FormattedMessage? Reason;

    public bool Selected;

    /// <summary>The item it puts on can be recoloured.</summary>
    public bool Colorable;

    /// <summary>Selected, but the server will not issue it at the current balance.</summary>
    public bool Unaffordable;

    public LoadoutEntry(LoadoutGroupView group, LoadoutPrototype proto)
    {
        Group = group;
        Proto = proto;
    }

    public ProtoId<LoadoutPrototype> Id => Proto.ID;
    public int Price => Proto.Price;
}

/// <summary>One loadout group of a role, with the entries this character may see.</summary>
public sealed class LoadoutGroupView
{
    public readonly LoadoutGroupPrototype Proto;

    public readonly LoadoutCategory Category;
    public readonly string Label;

    /// <summary>Listed entries, selectable ones first.</summary>
    public readonly List<LoadoutEntry> Entries = new();

    /// <summary>Selected entries in pick order.</summary>
    public readonly List<LoadoutEntry> Picks = new();

    /// <summary>Saved picks that are not listed any more; validation drops them on save.</summary>
    public int Stale;

    /// <summary>What the server issues in place of unaffordable picks, null if nothing.</summary>
    public string? Replacement;

    public LoadoutGroupView(LoadoutGroupPrototype proto, LoadoutCategory category, string label)
    {
        Proto = proto;
        Category = category;
        Label = label;
    }

    public ProtoId<LoadoutGroupPrototype> Id => Proto.ID;
    public int Min => Proto.MinLimit;
    public int Max => Math.Max(1, Proto.MaxLimit);
    public bool Single => Max == 1;
    public bool Optional => Min <= 0;

    /// <summary>Single optional groups list a "Nothing" tile, so clearing the slot is a visible choice.</summary>
    public bool HasNothingTile => Single && Optional;

    public bool AllLocked => Entries.All(e => e.Locked);

    /// <summary>A required group that offers exactly one thing: there is no choice to make.</summary>
    public bool Fixed => !Optional && Entries.Count(e => !e.Locked) == 1 && Picks.Count >= Min;

    /// <summary>Below its minimum while something could still be picked.</summary>
    public bool NeedsPick => Picks.Count + Stale < Min && Entries.Any(e => !e.Locked && !e.Selected);

    public int UnaffordableCount => Picks.Count(e => e.Unaffordable);
}

/// <summary>
/// View-model of the loadout window: the role's visible groups in rail order and their entries, built once.
/// <see cref="Sync"/> then only re-reads which entries are selected and what the selection costs.
/// </summary>
public sealed class LoadoutCatalog
{
    private readonly HumanoidCharacterProfile _profile;
    private readonly RoleLoadoutPrototype _role;
    private readonly ICommonSession _session;
    private readonly IDependencyCollection _collection;
    private readonly IPrototypeManager _protoManager;
    private readonly LoadoutSystem _loadoutSystem;
    private readonly WFGagsSystem _gags;

    /// <summary>Groups with at least one listed entry, by category and then by the role's order.</summary>
    public readonly List<LoadoutGroupView> Groups = new();

    /// <summary>Price of everything selected, hidden groups included.</summary>
    public long Cost { get; private set; }

    public long Balance { get; private set; }

    public LoadoutCatalog(
        HumanoidCharacterProfile profile,
        RoleLoadout loadout,
        RoleLoadoutPrototype role,
        ICommonSession session,
        IDependencyCollection collection)
    {
        _profile = profile;
        _role = role;
        _session = session;
        _collection = collection;
        _protoManager = collection.Resolve<IPrototypeManager>();
        _loadoutSystem = collection.Resolve<IEntityManager>().System<LoadoutSystem>();
        _gags = collection.Resolve<IEntityManager>().System<WFGagsSystem>();

        foreach (var groupId in role.Groups)
        {
            if (!_protoManager.TryIndex(groupId, out var groupProto) || groupProto.Hidden)
                continue;

            var label = Loc.GetString("wf-loadout-group-label", ("name", Loc.GetString(groupProto.Name)));
            var group = new LoadoutGroupView(groupProto, LoadoutCategorizer.Categorize(groupProto, _protoManager), label);
            BuildEntries(group, loadout);

            if (group.Entries.Count > 0)
                Groups.Add(group);
        }

        // Stable, so groups keep the role's order inside a category.
        var ordered = Groups.OrderBy(g => g.Category).ToList();
        Groups.Clear();
        Groups.AddRange(ordered);
    }

    private void BuildEntries(LoadoutGroupView group, RoleLoadout loadout)
    {
        var categoryLabel = Loc.GetString(LoadoutCategorizer.NameKey(group.Category));
        var locked = new List<LoadoutEntry>();

        foreach (var id in LoadoutCategorizer.Items(group.Proto, _protoManager))
        {
            if (!_protoManager.TryIndex(id, out var proto) || loadout.IsHidden(_profile, _session, id, _collection))
                continue;

            var entry = new LoadoutEntry(group, proto)
            {
                Name = string.IsNullOrEmpty(proto.Name) ? _loadoutSystem.GetName(proto) : proto.Name,
                Icon = string.IsNullOrEmpty(proto.PreviewEntity?.Id) ? _loadoutSystem.GetFirstOrNull(proto) : proto.PreviewEntity,
                Colorable = _gags.ColorableItem(proto) != null,
            };

            entry.Description = !string.IsNullOrEmpty(proto.Description)
                ? proto.Description
                : entry.Icon != null && _protoManager.TryIndex<EntityPrototype>(entry.Icon, out var entity)
                    ? entity.Description
                    : string.Empty;

            if (!loadout.IsValid(_profile, _session, id, _collection, out var reason))
            {
                entry.Locked = true;
                entry.Reason = ReadableReason(reason);
                locked.Add(entry);
                continue;
            }

            group.Entries.Add(entry);
        }

        group.Entries.AddRange(locked);
        NameVariants(group.Entries);

        foreach (var entry in group.Entries)
        {
            entry.NameText = $"{entry.Name} {group.Label} {categoryLabel}".ToLowerInvariant();
            entry.SearchText = $"{entry.NameText} {entry.Description.ToLowerInvariant()}";
        }
    }

    /// <summary>A lock reason can come back empty or as a bare locale key; neither tells the player anything.</summary>
    private static FormattedMessage ReadableReason(FormattedMessage? reason)
    {
        var text = reason?.ToString().Trim() ?? string.Empty;
        if (text.Length == 0)
            return FormattedMessage.FromUnformatted(Loc.GetString("wf-loadout-locked-generic"));

        if (!text.Contains(' ') && IoCManager.Resolve<ILocalizationManager>().TryGetString(text, out var localized))
            return FormattedMessage.FromUnformatted(localized);

        return reason!;
    }

    /// <summary>
    /// Items that share a name in one group get the part of their entity id the others lack put in front, such
    /// as "Security headset", so twenty headsets are not twenty identical tiles. In front, because a tile cuts
    /// a long name off at its end.
    /// </summary>
    private static void NameVariants(List<LoadoutEntry> entries)
    {
        foreach (var clash in entries.GroupBy(e => e.Name).Where(g => g.Count() > 1))
        {
            var words = clash.Select(e => IdWords(e.Icon?.Id ?? e.Proto.ID)).ToList();
            var shared = new HashSet<string>(words[0]);
            foreach (var list in words)
            {
                shared.IntersectWith(list);
            }

            var index = 0;
            foreach (var entry in clash)
            {
                var variant = string.Join(' ', words[index++].Where(w => !shared.Contains(w)));
                if (variant.Length > 0)
                    entry.Name = Loc.GetString("wf-loadout-entry-variant", ("name", entry.Name), ("variant", variant));
            }
        }
    }

    /// <summary>Splits a PascalCase prototype id into its words.</summary>
    private static List<string> IdWords(string id)
    {
        var words = new List<string>();
        var word = new StringBuilder();

        foreach (var c in id)
        {
            if ((char.IsUpper(c) || !char.IsLetterOrDigit(c)) && word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }

            if (char.IsLetterOrDigit(c))
                word.Append(c);
        }

        if (word.Length > 0)
            words.Add(word.ToString());

        return words;
    }

    /// <summary>Re-reads the selection from the loadout and works out what it costs and what can't be paid for.</summary>
    public void Sync(RoleLoadout loadout, long balance)
    {
        Balance = balance;

        foreach (var group in Groups)
        {
            group.Picks.Clear();
            group.Stale = 0;
            group.Replacement = null;

            foreach (var entry in group.Entries)
            {
                entry.Selected = false;
                entry.Unaffordable = false;
            }

            if (!loadout.SelectedLoadouts.TryGetValue(group.Id, out var selected))
                continue;

            foreach (var pick in selected)
            {
                var entry = group.Entries.FirstOrDefault(e => e.Id == pick.Prototype);
                if (entry == null)
                {
                    group.Stale++;
                    continue;
                }

                entry.Selected = true;
                group.Picks.Add(entry);
            }
        }

        Budget(loadout);
    }

    private void Budget(RoleLoadout loadout)
    {
        // The server pays in the order of the role's groups, then pick order, and that includes hidden groups.
        var picks = new List<(ProtoId<LoadoutGroupPrototype> Group, ProtoId<LoadoutPrototype> Loadout)>();
        var prices = new List<int>();

        foreach (var (groupId, selected) in loadout.SelectedLoadouts.OrderBy(x => _role.Groups.IndexOf(x.Key)))
        {
            foreach (var pick in selected)
            {
                if (!_protoManager.TryIndex(pick.Prototype, out var proto))
                    continue;

                picks.Add((groupId, pick.Prototype));
                prices.Add(proto.Price);
            }
        }

        var dropped = LoadoutBudget.Unaffordable(prices, Balance, out var cost);
        Cost = cost;

        foreach (var index in dropped)
        {
            var (groupId, loadoutId) = picks[index];
            var entry = Groups.FirstOrDefault(g => g.Id == groupId)?.Picks.FirstOrDefault(e => e.Id == loadoutId);
            if (entry != null)
                entry.Unaffordable = true;
        }

        foreach (var group in Groups)
        {
            if (group.UnaffordableCount > 0)
                group.Replacement = Replacement(group, loadout);
        }
    }

    /// <summary>Names the fallbacks the server equips when unaffordable picks leave a group below its minimum.</summary>
    private string? Replacement(LoadoutGroupView group, RoleLoadout loadout)
    {
        var issued = group.Picks.Where(e => !e.Unaffordable).Select(e => e.Id).ToList();
        var names = new List<string>();

        foreach (var fallback in group.Proto.Fallbacks)
        {
            if (issued.Count >= group.Min)
                break;

            if (issued.Contains(fallback) ||
                !_protoManager.TryIndex(fallback, out var proto) ||
                !loadout.IsValid(_profile, _session, fallback, _collection, out _))
            {
                continue;
            }

            issued.Add(fallback);
            names.Add(string.IsNullOrEmpty(proto.Name) ? _loadoutSystem.GetName(proto) : proto.Name);
        }

        return names.Count > 0 ? string.Join(", ", names) : null;
    }

    /// <summary>
    /// Entries of every group matching all words of the query. Matches on the name or group come before ones
    /// that only match the description, and within each, selectable entries before locked ones.
    /// </summary>
    public List<LoadoutEntry> Search(string query)
    {
        var tokens = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var ranks = new[] { new List<LoadoutEntry>(), new List<LoadoutEntry>(), new List<LoadoutEntry>(), new List<LoadoutEntry>() };

        foreach (var group in Groups)
        {
            foreach (var entry in group.Entries)
            {
                if (!tokens.All(entry.SearchText.Contains))
                    continue;

                var rank = tokens.All(entry.NameText.Contains) ? 0 : 2;
                ranks[entry.Locked ? rank + 1 : rank].Add(entry);
            }
        }

        return ranks.SelectMany(r => r).ToList();
    }
}
