using Content.Client.Overlays;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Wolfmed.Overlays;

/// <summary>
/// M2 (OD8 (b)): a body stable but helpless for long enough is flagged in distress on medical HUDs, with the Call for
/// help flag's icon (placeholder art). A body already flagged by its own call shows it once.
/// </summary>
public sealed class WolfmedDistressIconSystem : EntitySystem
{
    private static readonly ProtoId<HealthIconPrototype> Icon = "WFHealthIconWolfmedCallForHelp";

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private ShowHealthIconsSystem _healthIcons = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedDormantComponent, GetStatusIconsEvent>(OnGetStatusIcons);
    }

    private void OnGetStatusIcons(Entity<WolfmedDormantComponent> ent, ref GetStatusIconsEvent args)
    {
        if (!ent.Comp.Distress || !_healthIcons.WolfmedHudActive ||
            TryComp(ent, out WolfmedCallForHelpComponent? called) && called.FlagUntil > _timing.CurTime ||
            !_prototypes.TryIndex(Icon, out var icon))
            return;

        args.StatusIcons.Add(icon);
    }
}
