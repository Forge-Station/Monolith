using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Damage;
using Content.Shared.Mobs;

namespace Content.Server._WF.Remade.Roster;

/// <summary>Opens and shuts a Remade's shell: its armour, what can be hit through it and how it looks.</summary>
public sealed partial class WFShellSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFRemadeShellComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFRemadeShellComponent, MobStateChangedEvent>(OnMobState);
    }

    private void OnMapInit(Entity<WFRemadeShellComponent> ent, ref MapInitEvent args)
    {
        SetOpen(ent.Owner, false);
    }

    /// <summary>A dead one is drawn from its dead state, with no shell over it.</summary>
    private void OnMobState(Entity<WFRemadeShellComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            _appearance.SetData(ent, WFRemadeShellVisuals.Open, true);
    }

    public bool IsOpen(EntityUid uid)
    {
        return TryComp<WFRemadeShellComponent>(uid, out var shell) && shell.Open;
    }

    /// <summary>Opens or shuts the shell. Does nothing if it's already so.</summary>
    public void SetOpen(EntityUid uid, bool open)
    {
        if (!TryComp<WFRemadeShellComponent>(uid, out var shell))
            return;

        var changed = shell.Open != open || (!open && shell.Hidden.Count == 0);
        shell.Open = open;
        if (TryComp<DamageableComponent>(uid, out var damageable))
            _damageable.SetDamageModifierSetId(uid, open ? shell.OpenArmour : shell.ShutArmour, damageable);
        _appearance.SetData(uid, WFRemadeShellVisuals.Open, open);
        if (!changed || !TryComp<WFRemadeLimbsComponent>(uid, out var limbs))
            return;

        if (open)
        {
            foreach (var limb in limbs.Limbs)
            {
                if (!shell.Hidden.TryGetValue(limb.Id, out var saved))
                    continue;

                limb.Weight = saved.Weight;
                limb.Zones = saved.Zones;
            }

            shell.Hidden.Clear();
            return;
        }

        foreach (var limb in limbs.Limbs)
        {
            if (!shell.Covered.Contains(limb.Id) || shell.Hidden.ContainsKey(limb.Id))
                continue;

            shell.Hidden[limb.Id] = (limb.Weight, limb.Zones);
            limb.Weight = 0f;
            limb.Zones = new List<TargetBodyPart>();
        }
    }
}
