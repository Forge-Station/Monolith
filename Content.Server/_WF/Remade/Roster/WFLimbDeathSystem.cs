using Content.Server._WF.Remade.Elites;
using Content.Server._WF.Remade.Limbs;
using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;

namespace Content.Server._WF.Remade.Roster;

/// <summary>Kills a Remade that has lost every one of the limbs it can't live without.</summary>
public sealed partial class WFLimbDeathSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WFEliteSystem _elite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFDiesWithoutLimbsComponent, WFRemadeLimbSeveredEvent>(OnSevered);
    }

    private void OnSevered(Entity<WFDiesWithoutLimbsComponent> ent, ref WFRemadeLimbSeveredEvent args)
    {
        if (_mobState.IsDead(ent) || !TryComp<WFRemadeLimbsComponent>(ent, out var limbs))
            return;

        foreach (var id in ent.Comp.Limbs)
        {
            if (!limbs.Severed.Contains(id))
                return;
        }

        _popup.PopupEntity(Loc.GetString("wf-remade-limb-death", ("mob", ent.Owner)), ent, PopupType.MediumCaution);
        _elite.Fell(ent);
    }
}
