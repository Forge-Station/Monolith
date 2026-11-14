using System.Diagnostics.CodeAnalysis;
using Content.Shared.Power.Components; // WOLFGATE - battery moved to shared here
using Content.Shared._HL.Silicons.Synths.Battery;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Robust.Shared.Containers;

namespace Content.Server._HL.Silicons.Synths.Battery;

public sealed partial class SynthBatterySystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _container = default!;

    public bool TryGetBattery(
        EntityUid uid,
        [NotNullWhen(true)] out Entity<BatteryComponent>? battery,
        SynthBatteryComponent? synthBattery = null,
        BodyComponent? body = null)
    {
        battery = null;

        if (!Resolve(uid, ref synthBattery, false))
            return false;

        return TryGetOrganBattery((uid, synthBattery, body), out battery);
    }

    private bool TryGetOrganBattery(
        Entity<SynthBatteryComponent, BodyComponent?> ent,
        [NotNullWhen(true)] out Entity<BatteryComponent>? battery)
    {
        battery = null;

        if (!TryGetBatteryContainer(ent.Owner, ent.Comp1.OrganSlot, out _, out var container, ent.Comp2))
            return false;

        foreach (var contained in container.ContainedEntities)
        {
            if (!TryComp(contained, out BatteryComponent? batteryComponent))
                continue;

            battery = (contained, batteryComponent);
            return true;
        }

        return false;
    }

    public bool TryGetBatteryContainer(
        EntityUid uid,
        string slot,
        [NotNullWhen(true)] out Entity<BodyPartComponent>? part,
        [NotNullWhen(true)] out BaseContainer? container,
        BodyComponent? body = null)
    {
        part = null;
        container = null;

        if (!Resolve(uid, ref body, false))
            return false;

        foreach (var (partUid, partComponent) in _body.GetBodyChildren(uid, body))
        {
            if (!_body.CanInsertOrgan(partUid, slot, partComponent) &&
                partComponent.PartType == BodyPartType.Torso)
            {
                _body.TryCreateOrganSlot(partUid, slot, out _, partComponent);
            }

            if (!_body.CanInsertOrgan(partUid, slot, partComponent))
                continue;

            if (!_container.TryGetContainer(partUid, SharedBodySystem.GetOrganContainerId(slot), out container))
                return false;

            part = (partUid, partComponent);
            return true;
        }

        return false;
    }
}
