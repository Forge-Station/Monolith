using System.Collections.Generic;
using Content.Server.Power.Components;
using Content.Shared._WF.Lattice;
using Content.Shared.Inventory.Events;
using Content.Shared.Power;

namespace Content.Server._WF.Lattice;

/// <summary>
/// Drift only looks for shelter on the ship and on the person. The stabiliser and the charm carry the numbers;
/// this copies them onto the grid while the machine is anchored and powered, and onto the wearer while the charm is on.
/// </summary>
public sealed class WFLatticeShelterRelaySystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<WFLatticeShelterComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WFLatticeShelterComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WFLatticeShelterComponent, PowerChangedEvent>(OnPower);
        SubscribeLocalEvent<WFLatticeShelterComponent, AnchorStateChangedEvent>(OnAnchor);
        SubscribeLocalEvent<WFLatticeShelterComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<WFLatticeShelterComponent, GotUnequippedEvent>(OnUnequipped);
    }

    private void OnStartup(EntityUid uid, WFLatticeShelterComponent component, ComponentStartup args)
    {
        if (HasComp<ApcPowerReceiverComponent>(uid))
            RefreshMachine(uid);
    }

    private void OnShutdown(EntityUid uid, WFLatticeShelterComponent component, ComponentShutdown args)
    {
        Clear(uid);
    }

    private void OnPower(EntityUid uid, WFLatticeShelterComponent component, ref PowerChangedEvent args)
    {
        RefreshMachine(uid);
    }

    private void OnAnchor(EntityUid uid, WFLatticeShelterComponent component, ref AnchorStateChangedEvent args)
    {
        if (HasComp<ApcPowerReceiverComponent>(uid))
            RefreshMachine(uid);
    }

    private void OnEquipped(EntityUid uid, WFLatticeShelterComponent component, GotEquippedEvent args)
    {
        if (!HasComp<ApcPowerReceiverComponent>(uid))
            SetTarget(uid, args.Equipee);
    }

    private void OnUnequipped(EntityUid uid, WFLatticeShelterComponent component, GotUnequippedEvent args)
    {
        if (!HasComp<ApcPowerReceiverComponent>(uid))
            Clear(uid);
    }

    private void RefreshMachine(EntityUid uid)
    {
        if (!TryComp<ApcPowerReceiverComponent>(uid, out var power))
            return;

        var xform = Transform(uid);
        var on = power.Powered && xform.Anchored && xform.GridUid is { } grid && grid != uid;
        SetTarget(uid, on ? xform.GridUid : null);
    }

    private void Clear(EntityUid source)
    {
        SetTarget(source, null);
    }

    private void SetTarget(EntityUid source, EntityUid? target)
    {
        if (!TryComp<WFLatticeShelterLinkComponent>(source, out var link))
        {
            if (target is not { } first)
                return;

            link = AddComp<WFLatticeShelterLinkComponent>(source);
            link.Target = first;
            Register(source, first);
            return;
        }

        if (link.Target == target)
            return;

        if (link.Target is { } old && !TerminatingOrDeleted(old))
            Unregister(source, old);

        if (target is not { } next)
        {
            RemComp<WFLatticeShelterLinkComponent>(source);
            return;
        }

        link.Target = next;
        Register(source, next);
    }

    private void Register(EntityUid source, EntityUid target)
    {
        var sources = EnsureComp<WFLatticeShelterSourcesComponent>(target);
        sources.Sources.Add(source);
        Recompute(target, sources);
    }

    private void Unregister(EntityUid source, EntityUid target)
    {
        if (!TryComp<WFLatticeShelterSourcesComponent>(target, out var sources))
            return;

        if (!sources.Sources.Remove(source))
            return;

        Recompute(target, sources);
    }

    /// <summary>
    /// The strongest working source wins. When the last one switches off, the copied shelter goes with it.
    /// </summary>
    private void Recompute(EntityUid target, WFLatticeShelterSourcesComponent sources)
    {
        sources.Sources.RemoveWhere(source => TerminatingOrDeleted(source) || !HasComp<WFLatticeShelterComponent>(source));
        if (sources.Sources.Count == 0)
        {
            RemComp<WFLatticeShelterComponent>(target);
            RemComp<WFLatticeShelterSourcesComponent>(target);
            return;
        }

        var shelter = 0f;
        var stops = false;
        foreach (var source in sources.Sources)
        {
            if (!TryComp<WFLatticeShelterComponent>(source, out var comp))
                continue;

            shelter = Math.Max(shelter, comp.Shelter);
            stops |= comp.StopsTaking;
        }

        var applied = EnsureComp<WFLatticeShelterComponent>(target);
        applied.Shelter = shelter;
        applied.StopsTaking = stops;
        Dirty(target, applied);
    }
}

/// <summary>On the stabiliser or charm: which ship or person currently carries its shelter.</summary>
[RegisterComponent]
public sealed partial class WFLatticeShelterLinkComponent : Component
{
    public EntityUid? Target;
}

/// <summary>On the ship or person: the machines and charms whose shelter was copied here.</summary>
[RegisterComponent]
public sealed partial class WFLatticeShelterSourcesComponent : Component
{
    public HashSet<EntityUid> Sources = new();
}
