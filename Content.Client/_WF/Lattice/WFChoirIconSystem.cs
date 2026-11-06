using Content.Shared._WF.Lattice;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Lattice;

/// <summary>Puts the Choir's mark over a member's head; the icon's own rules decide who sees it.</summary>
public sealed class WFChoirIconSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFChoirMemberComponent, GetStatusIconsEvent>(OnGetIcons);
    }

    private void OnGetIcons(Entity<WFChoirMemberComponent> ent, ref GetStatusIconsEvent args)
    {
        if (_proto.TryIndex(ent.Comp.StatusIcon, out var icon))
            args.StatusIcons.Add(icon);
    }
}
