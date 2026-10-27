using Content.Shared._WF.Newbie;
using Content.Shared.Mobs.Systems;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Newbie;

/// <summary>
/// Draws the learner mark over a new player's head.
/// </summary>
public sealed partial class WFNewbieIconSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFNewbieComponent, GetStatusIconsEvent>(OnGetStatusIcons);
    }

    private void OnGetStatusIcons(Entity<WFNewbieComponent> ent, ref GetStatusIconsEvent args)
    {
        if (_mobState.IsDead(ent) || !_prototypes.TryIndex(ent.Comp.Icon, out var icon))
            return;

        args.StatusIcons.Add(icon);
    }
}
