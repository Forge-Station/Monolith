using Content.Client.Overlays;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Wolfmed.Overlays;

/// <summary>
/// Flags a patient who called for help on medical HUDs until the call runs out (M1a, plan §5.3). A HUD flag
/// rather than a radio ping: the medic sees who is asking among everyone lying on the floor.
/// </summary>
public sealed class WolfmedCallForHelpIconSystem : EntitySystem
{
    private static readonly ProtoId<HealthIconPrototype> Icon = "WFHealthIconWolfmedCallForHelp";

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private ShowHealthIconsSystem _healthIcons = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedCallForHelpComponent, GetStatusIconsEvent>(OnGetStatusIcons);
    }

    private void OnGetStatusIcons(Entity<WolfmedCallForHelpComponent> ent, ref GetStatusIconsEvent args)
    {
        if (!_healthIcons.WolfmedHudActive || ent.Comp.FlagUntil <= _timing.CurTime ||
            !_prototypes.TryIndex(Icon, out var icon))
            return;

        args.StatusIcons.Add(icon);
    }
}
