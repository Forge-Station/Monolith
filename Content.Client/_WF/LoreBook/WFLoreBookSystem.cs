using Content.Client.Guidebook;
using Content.Client.Guidebook.Components;
using Content.Shared._WF.LoreBook;
using Content.Shared.Guidebook;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WF.LoreBook;

/// <summary>
/// Knows which guide entries are lore book pages and which entities have one, and adds the lore button to their
/// examine verbs.
/// </summary>
public sealed partial class WFLoreBookSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private TagSystem _tags = default!;

    /// <summary>The book icon, loaded from the resource cache where entity systems may not be up yet.</summary>
    public static readonly ResPath IconPath = new("/Textures/_WF/LoreBook/lorebook.svg.192dpi.png");

    public static readonly SpriteSpecifier Icon = new SpriteSpecifier.Texture(IconPath);

    private readonly HashSet<string> _lore = new();
    private readonly Dictionary<string, ProtoId<GuideEntryPrototype>> _links = new();
    private readonly Dictionary<string, ProtoId<GuideEntryPrototype>?> _entityLore = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GetVerbsEvent<ExamineVerb>>(OnGetVerbs);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        Rebuild();
    }

    /// <summary>Whether this guide entry belongs to the lore book.</summary>
    public bool IsLore(ProtoId<GuideEntryPrototype> id)
    {
        return _lore.Contains(id);
    }

    /// <summary>The lore page for an entity: a lore link on its prototype or a parent, or a lore guide on its help.</summary>
    public bool TryGetEntityLore(EntityUid uid, out ProtoId<GuideEntryPrototype> entry)
    {
        entry = default;
        if (MetaData(uid).EntityPrototype is { } proto && ResolveEntity(proto.ID) is { } linked)
        {
            entry = linked;
            return true;
        }

        if (TryComp<GuideHelpComponent>(uid, out var help))
        {
            foreach (var guide in help.Guides)
            {
                if (!IsLore(guide))
                    continue;

                entry = guide;
                return true;
            }
        }

        return false;
    }

    private void OnGetVerbs(GetVerbsEvent<ExamineVerb> args)
    {
        if (_tags.HasTag(args.Target, GuidebookSystem.GuideEmbedTag) || !TryGetEntityLore(args.Target, out var entry))
            return;

        args.Verbs.Add(new ExamineVerb
        {
            Text = Loc.GetString("wf-lore-book-verb"),
            Icon = Icon,
            Act = () => _ui.GetUIController<WFLoreBookUIController>().Open(entry),
            ClientExclusive = true,
            CloseMenu = true,
        });
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<GuideEntryPrototype>()
            || args.WasModified<WFLoreLinkPrototype>()
            || args.WasModified<EntityPrototype>())
            Rebuild();
    }

    private void Rebuild()
    {
        _lore.Clear();
        _links.Clear();
        _entityLore.Clear();

        foreach (var entry in _proto.EnumeratePrototypes<GuideEntryPrototype>())
        {
            if (entry.Lore)
                AddLore(entry.ID);
        }

        foreach (var link in _proto.EnumeratePrototypes<WFLoreLinkPrototype>())
        {
            foreach (var entity in link.Entities)
                _links[entity] = link.Entry;
        }
    }

    private void AddLore(string id)
    {
        if (!_lore.Add(id) || !_proto.TryIndex<GuideEntryPrototype>(id, out var entry))
            return;

        foreach (var child in entry.Children)
            AddLore(child);
    }

    /// <summary>Finds the closest lore link on a prototype or its parents, remembering the answer.</summary>
    private ProtoId<GuideEntryPrototype>? ResolveEntity(string id)
    {
        if (_entityLore.TryGetValue(id, out var cached))
            return cached;

        // Mark it first so a parent loop ends here.
        _entityLore[id] = null;

        ProtoId<GuideEntryPrototype>? found = null;
        if (_links.TryGetValue(id, out var direct))
            found = direct;
        else if (_proto.TryIndex<EntityPrototype>(id, out var proto) && proto.Parents is { } parents)
        {
            foreach (var parent in parents)
            {
                if (ResolveEntity(parent) is not { } inherited)
                    continue;

                found = inherited;
                break;
            }
        }

        _entityLore[id] = found;
        return found;
    }
}
