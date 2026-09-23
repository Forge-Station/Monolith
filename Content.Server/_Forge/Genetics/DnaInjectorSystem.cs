using Content.Server.DoAfter;
using Content.Shared._Forge.Genetics;
using Content.Shared._Forge.Genetics.Components;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Genetics;

public sealed class DnaInjectorSystem : EntitySystem
{
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly GeneticsSystem _genetics = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DnaInjectorComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<DnaInjectorComponent, DnaInjectDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<DnaInjectorComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<DnaInjectorComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (ent.Comp.Used)
        {
            args.PushMarkup(Loc.GetString("genetics-injector-used"));
            return;
        }

        var mode = ent.Comp.Activate
            ? Loc.GetString("genetics-injector-mode-activate")
            : Loc.GetString("genetics-injector-mode-clean");
        args.PushMarkup(Loc.GetString("genetics-injector-examine", ("mode", mode)));

        foreach (var gene in ent.Comp.Genes)
        {
            if (_genetics.IsDiscovered(gene) && _prototypes.TryIndex(gene, out var proto))
                args.PushMarkup(Loc.GetString("genetics-injector-examine-gene", ("gene", Loc.GetString(proto.Name))));
            else
                args.PushMarkup(Loc.GetString("genetics-injector-examine-gene", ("gene", Loc.GetString("genetics-gene-unknown"))));
        }
    }

    private void OnAfterInteract(Entity<DnaInjectorComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (ent.Comp.Used)
        {
            _popup.PopupEntity(Loc.GetString("genetics-injector-used"), ent, args.User);
            return;
        }

        if (_genetics.IsSteel(target))
        {
            _popup.PopupEntity(Loc.GetString("genetics-steel-no-mutate"), target, args.User);
            return;
        }

        if (!HasComp<GenomeComponent>(target))
        {
            _popup.PopupEntity(Loc.GetString("genetics-injector-no-genome", ("target", Identity.Name(target, EntityManager, args.User))), target, args.User);
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.InjectDelay, new DnaInjectDoAfterEvent(), ent, target, ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            BlockDuplicate = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            args.Handled = true;
    }

    private void OnDoAfter(Entity<DnaInjectorComponent> ent, ref DnaInjectDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        if (ent.Comp.Used || !TryComp<GenomeComponent>(target, out var genome))
            return;

        foreach (var gene in ent.Comp.Genes)
        {
            if (ent.Comp.Activate)
                _genetics.TryInjectGene(target, gene, genome);
            else
                _genetics.TryRemoveGene(target, gene, genome);
        }

        if (ent.Comp.SingleUse)
        {
            ent.Comp.Used = true;
            Dirty(ent);
        }

        _audio.PlayPvs("/Audio/Items/hypospray.ogg", target);
        _popup.PopupEntity(Loc.GetString("genetics-injector-success", ("target", Identity.Name(target, EntityManager, args.User))), target, args.User);
        args.Handled = true;
    }
}
