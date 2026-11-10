using System.Diagnostics.CodeAnalysis;
using Content.Shared._WF.Vehicles.Grid;
using Content.Shared.Tag;
using Content.Shared.Whitelist;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Vehicles.Crushing;

/// <summary>Finds which <see cref="VehicleSmashRulePrototype"/> applies to an entity, once per entity prototype id.</summary>
public sealed partial class VehicleSmashRuleSystem : EntitySystem
{
    /// <summary>The components a prototype needs before any rule may match it.</summary>
    public static readonly string[] RequiredComponents = { "Damageable", "Destructible" };

    /// <summary>Prototypes that no rule matches, nor any prototype that inherits from them: walls, and what is built never to break.</summary>
    public static readonly string[] NeverParents = { "BaseWall", "BaseWallIndestructible", "BaseStructureIndestructible" };

    /// <summary>Prototypes a plow never breaks, nor any that inherit from them: what is built never to break, and the outposts' walls.</summary>
    public static readonly string[] PlowNeverParents =
    {
        "BaseWallIndestructible", "BaseStructureIndestructible", "WallPlastitaniumIndestructible", "BaseWallOutpost",
    };

    private const string GirderParent = "Girder";

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";

    private readonly Dictionary<string, (bool Plowable, bool Wall)> _plow = new();

    /// <summary>The one of <see cref="RequiredComponents"/> that only the server has; a client's copy of a prototype leaves it out.</summary>
    private const string ServerOnlyComponent = "Destructible";

    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private List<VehicleSmashRulePrototype>? _rules;
    private readonly Dictionary<string, VehicleSmashSettings?> _resolved = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        _rules = null;
        _resolved.Clear();
        _plow.Clear();
    }

    /// <summary>
    /// Whether a plow damages the entity: a wall, a girder or anything a rule names (which some vehicle may smash), as
    /// long as it can be broken at all and is not one of <see cref="PlowNeverParents"/>. Machines, safes and the rest of
    /// what no rule names are no business of a plow.
    /// </summary>
    public bool IsPlowable(EntityUid uid)
    {
        return TryGetPlowInfo(uid, out var info) && info.Plowable;
    }

    /// <summary>Whether the entity is a wall, breakable or not.</summary>
    public bool IsWall(EntityUid uid)
    {
        return TryGetPlowInfo(uid, out var info) && info.Wall;
    }

    private bool TryGetPlowInfo(EntityUid uid, out (bool Plowable, bool Wall) info)
    {
        info = default;
        if (!TryComp(uid, out MetaDataComponent? meta) || meta.EntityPrototype is not { } proto)
            return false;

        if (_plow.TryGetValue(proto.ID, out info))
            return true;

        var wall = TryGetTags(proto, out var tags) && tags.Contains(WallTag);
        var breakable = true;
        foreach (var name in RequiredComponents)
        {
            breakable &= proto.Components.ContainsKey(name);
        }

        var girder = false;
        foreach (var (id, _) in _proto.EnumerateAllParents<EntityPrototype>(proto.ID, includeSelf: true))
        {
            girder |= id == GirderParent;
            foreach (var never in PlowNeverParents)
            {
                breakable &= never != id;
            }
        }

        info = (breakable && (wall || girder || TryGetSettings(proto, out _)), wall);
        _plow[proto.ID] = info;
        return true;
    }

    /// <summary>Finds the smash settings the rules give an entity; false when it matches none or has no prototype.</summary>
    public bool TryGetSettings(EntityUid uid, out VehicleSmashSettings settings)
    {
        settings = default;

        if (!TryComp(uid, out MetaDataComponent? meta) || meta.EntityPrototype is not { } proto)
            return false;

        return TryGetSettings(proto, out settings);
    }

    /// <summary>Finds the smash settings the rules give an entity prototype; false when it matches none.</summary>
    public bool TryGetSettings(EntityPrototype proto, out VehicleSmashSettings settings)
    {
        if (!_resolved.TryGetValue(proto.ID, out var resolved))
        {
            resolved = null;
            foreach (var rule in MatchingRules(proto))
            {
                resolved = rule.ToSettings();
                break;
            }

            _resolved[proto.ID] = resolved;
        }

        settings = resolved ?? default;
        return resolved != null;
    }

    /// <summary>Every rule that matches a prototype, the one that wins first.</summary>
    public IEnumerable<VehicleSmashRulePrototype> MatchingRules(EntityPrototype proto)
    {
        if (!IsSmashable(proto))
            yield break;

        foreach (var rule in GetRules())
        {
            if (Matches(rule, proto))
                yield return rule;
        }
    }

    private List<VehicleSmashRulePrototype> GetRules()
    {
        if (_rules != null)
            return _rules;

        _rules = new List<VehicleSmashRulePrototype>(_proto.EnumeratePrototypes<VehicleSmashRulePrototype>());
        _rules.Sort(static (a, b) =>
        {
            var byPriority = b.Priority.CompareTo(a.Priority);
            return byPriority != 0 ? byPriority : string.CompareOrdinal(a.ID, b.ID);
        });

        return _rules;
    }

    /// <summary>Whether any rule may match the prototype at all: it can be broken, and it is no wall.</summary>
    private bool IsSmashable(EntityPrototype proto)
    {
        foreach (var name in RequiredComponents)
        {
            // The client predicts the smash, and would be stopped by everything the server drives through if it asked for this.
            if (_net.IsClient && name == ServerOnlyComponent)
                continue;

            if (!proto.Components.ContainsKey(name))
                return false;
        }

        if (TryGetTags(proto, out var tags) && tags.Contains(WallTag))
            return false;

        foreach (var (id, _) in _proto.EnumerateAllParents<EntityPrototype>(proto.ID, includeSelf: true))
        {
            foreach (var never in NeverParents)
            {
                if (never == id)
                    return false;
            }
        }

        return true;
    }

    private bool Matches(VehicleSmashRulePrototype rule, EntityPrototype proto)
    {
        // A rule with no selector would match everything.
        if (rule.Parents.Count == 0 && rule.Whitelist == null)
            return false;

        if (rule.Parents.Count > 0 && !HasAnyParent(proto, rule.Parents))
            return false;

        if (rule.Whitelist != null && !Passes(rule.Whitelist, proto))
            return false;

        if (rule.ExcludeParents.Count > 0 && HasAnyParent(proto, rule.ExcludeParents))
            return false;

        if (rule.Blacklist != null && Passes(rule.Blacklist, proto))
            return false;

        return true;
    }

    /// <summary>Whether the prototype is, or inherits from, one of the listed prototypes; abstract parents count.</summary>
    private bool HasAnyParent(EntityPrototype proto, List<EntProtoId> parents)
    {
        foreach (var (id, _) in _proto.EnumerateAllParents<EntityPrototype>(proto.ID, includeSelf: true))
        {
            foreach (var parent in parents)
            {
                if (parent.Id == id)
                    return true;
            }
        }

        return false;
    }

    /// <summary>The components and tags part of <see cref="EntityWhitelistSystem.IsValid(EntityWhitelist,EntityUid)"/>, read off a prototype.</summary>
    private static bool Passes(EntityWhitelist list, EntityPrototype proto)
    {
        if (list.Components != null)
        {
            foreach (var name in list.Components)
            {
                if (proto.Components.ContainsKey(name))
                {
                    if (!list.RequireAll)
                        return true;
                }
                else if (list.RequireAll)
                {
                    return false;
                }
            }
        }

        if (list.Tags != null)
        {
            TryGetTags(proto, out var tags);

            foreach (var tag in list.Tags)
            {
                var has = tags != null && tags.Contains(tag);
                if (has)
                {
                    if (!list.RequireAll)
                        return true;
                }
                else if (list.RequireAll)
                {
                    return false;
                }
            }
        }

        return list.RequireAll;
    }

    /// <summary>The tags a prototype carries, with the ones it inherits.</summary>
    public static bool TryGetTags(EntityPrototype proto, [NotNullWhen(true)] out HashSet<ProtoId<TagPrototype>>? tags)
    {
        if (proto.Components.TryGetValue("Tag", out var entry) && entry.Component is TagComponent tag)
        {
            tags = tag.Tags;
            return true;
        }

        tags = null;
        return false;
    }
}
