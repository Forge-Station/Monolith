using Content.Server.Administration.Logs;
using Content.Server.Popups;
using Content.Shared.Database;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared._Forge.Guard.Components;
using Robust.Shared.Containers;
using Content.Shared.StatusIcon;

namespace Content.Server._Forge.Guard;

/// <summary>
/// System used for adding or removing components with a overlord implant
/// </summary>
public sealed class GuardSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLogManager = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GuardImplantComponent, ImplantImplantedEvent>(OnImplantImplanted);
        SubscribeLocalEvent<GuardImplantComponent, EntGotRemovedFromContainerMessage>(OnImplantDraw);
    }

    private void OnImplantImplanted(Entity<GuardImplantComponent> ent, ref ImplantImplantedEvent ev)
    {
        var mob = ev.Implanted;
        if (mob == null)
            return;

        EnsureComp<GuardComponent>(mob.Value);
        _adminLogManager.Add(LogType.Mind, LogImpact.Medium, $"{ToPrettyString(mob.Value)} was converted using OverlordImplant.");
///        GuardRemovalCheck(mob.Value, ev.Implant);
    }

///   private void GuardRemovalCheck(EntityUid implanted, EntityUid implant)
///   {
///        if (_container.TryGetContainer(implanted, ImplanterComponent.ImplantSlotId, out var implantContainer))
///        {
///           foreach (var ent in implantContainer.ContainedEntities)
///            {
///                if (HasComp<SubdermalImplantComponent>(ent))
///                {
///                    _popupSystem.PopupEntity(Loc.GetString("guard-break-"), implanted);
///                    PredictedQueueDel(ent);
///                    break;
///               }
///            }
///        }
///   }

    private void OnImplantDraw(Entity<GuardImplantComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        RemComp<GuardComponent>(args.Container.Owner);
    }
}
