using Content.Shared._Goobstation.Clothing.Systems;
using Content.Shared.Actions;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Robust.Shared.Network;

namespace Content.Shared._WF.Stealth;

/// <summary>
/// Switches the stealth a worn item grants on and off from an action, and keeps it in that state on re-equip.
/// Switching off fades the wearer back into view; switching on fades them out from fully visible.
/// </summary>
public sealed class WFStealthToggleSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStealthSystem _stealth = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFStealthToggleComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<WFStealthToggleComponent, WFToggleStealthEvent>(OnToggle);
        SubscribeLocalEvent<WFStealthToggleComponent, GotEquippedEvent>(OnEquipped, after: [typeof(ClothingGrantingSystem)]);
        SubscribeLocalEvent<WFStealthToggleComponent, GotUnequippedEvent>(OnUnequipped);
    }

    private void OnGetActions(Entity<WFStealthToggleComponent> ent, ref GetItemActionsEvent args)
    {
        if (!TryComp<ClothingComponent>(ent, out var clothing) || args.SlotFlags is not { } slot ||
            (clothing.Slots & slot) == 0)
            return;

        args.AddAction(ref ent.Comp.ActionEntity, ent.Comp.Action);
        _actions.SetToggled(ent.Comp.ActionEntity, ent.Comp.Enabled);
    }

    private void OnToggle(Entity<WFStealthToggleComponent> ent, ref WFToggleStealthEvent args)
    {
        if (args.Handled || !_inventory.TryGetContainingEntity(ent.Owner, out var wearer))
            return;

        args.Handled = true;

        // The server owns the wearer's stealth and markers; the client only sees the result through state.
        if (!_net.IsServer)
            return;

        ent.Comp.Enabled = !ent.Comp.Enabled;
        Dirty(ent);
        Apply(ent, wearer.Value, fade: true);
        _popup.PopupEntity(Loc.GetString(ent.Comp.Enabled ? "wf-stealth-toggle-on" : "wf-stealth-toggle-off"),
            wearer.Value, args.Performer);
    }

    private void OnEquipped(Entity<WFStealthToggleComponent> ent, ref GotEquippedEvent args)
    {
        if (!_net.IsServer)
            return;

        if (ent.Comp.GlowStrength > 0f)
        {
            EnsureComp<WFStealthGlowComponent>(args.Equipee).Strength = ent.Comp.GlowStrength;
            Dirty(args.Equipee, Comp<WFStealthGlowComponent>(args.Equipee));
        }

        Apply(ent, args.Equipee, fade: false);
    }

    private void OnUnequipped(Entity<WFStealthToggleComponent> ent, ref GotUnequippedEvent args)
    {
        if (!_net.IsServer)
            return;

        RemComp<WFStealthSuppressedComponent>(args.Equipee);
        RemComp<WFStealthFadingOutComponent>(args.Equipee);
        RemComp<WFStealthGlowComponent>(args.Equipee);
    }

    /// <summary>Puts the wearer's stealth, the suppression marker and the action icon in the item's state.</summary>
    private void Apply(Entity<WFStealthToggleComponent> ent, EntityUid wearer, bool fade)
    {
        _actions.SetToggled(ent.Comp.ActionEntity, ent.Comp.Enabled);
        TryComp<StealthComponent>(wearer, out var stealth);

        if (ent.Comp.Enabled)
        {
            RemComp<WFStealthSuppressedComponent>(wearer);
            RemComp<WFStealthFadingOutComponent>(wearer);
            // A downed wearer stays visible if the stealth is set to drop in crit or on death.
            if (stealth is { Enabled: false } &&
                !(_mobState.IsCritical(wearer) && !stealth.EnabledOnCrit) &&
                !(_mobState.IsDead(wearer) && !stealth.EnabledOnDeath))
            {
                // Start fully visible, so the field fades in instead of snapping to wherever it was left.
                _stealth.SetVisibility(wearer, 1f, stealth);
                _stealth.SetEnabled(wearer, true, stealth);
            }

            return;
        }

        EnsureComp<WFStealthSuppressedComponent>(wearer);
        if (stealth is not { Enabled: true })
            return;

        if (fade && ent.Comp.FadeOutRate > 0f)
            EnsureComp<WFStealthFadingOutComponent>(wearer).Rate = ent.Comp.FadeOutRate;
        else
            _stealth.SetEnabled(wearer, false, stealth);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_net.IsServer)
            return;

        var query = EntityQueryEnumerator<WFStealthFadingOutComponent, StealthComponent>();
        while (query.MoveNext(out var uid, out var fading, out var stealth))
        {
            _stealth.ModifyVisibility(uid, fading.Rate * frameTime, stealth);
            if (_stealth.GetVisibility(uid, stealth) < 1f)
                continue;

            _stealth.SetEnabled(uid, false, stealth);
            RemCompDeferred<WFStealthFadingOutComponent>(uid);
        }
    }
}
