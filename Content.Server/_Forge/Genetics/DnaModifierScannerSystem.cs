using Content.Server._Forge.Genetics.Components;
using Content.Shared._Forge.Genetics.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Climbing.Systems;
using Content.Shared.Destructible;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.DragDrop;
using Content.Shared.FixedPoint;
using Content.Shared.MedicalScanner;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Server.Power.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Verbs;
using Robust.Server.Containers;
using Robust.Shared.Containers;
using static Content.Shared.MedicalScanner.SharedMedicalScannerComponent;

namespace Content.Server._Forge.Genetics;

public sealed class DnaModifierScannerSystem : EntitySystem
{
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly ClimbSystem _climb = default!;
    [Dependency] private readonly ContainerSystem _containers = default!;
    [Dependency] private readonly Content.Server.DeviceLinking.Systems.DeviceLinkSystem _deviceLink = default!;
    [Dependency] private readonly GeneticsConsoleSystem _console = default!;
    [Dependency] private readonly GeneticsSystem _genetics = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private const float UpdateRate = 1f;
    private float _updateDif;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DnaModifierScannerComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<DnaModifierScannerComponent, ContainerRelayMovementEntityEvent>(OnRelayMovement);
        SubscribeLocalEvent<DnaModifierScannerComponent, GetVerbsEvent<InteractionVerb>>(OnInsertVerb);
        SubscribeLocalEvent<DnaModifierScannerComponent, GetVerbsEvent<AlternativeVerb>>(OnAltVerbs);
        SubscribeLocalEvent<DnaModifierScannerComponent, DestructionEventArgs>(OnDestroyed);
        SubscribeLocalEvent<DnaModifierScannerComponent, DragDropTargetEvent>(OnDragDrop);
        SubscribeLocalEvent<DnaModifierScannerComponent, CanDropTargetEvent>(OnCanDrop);
        SubscribeLocalEvent<DnaModifierScannerComponent, PortDisconnectedEvent>(OnPortDisconnected);
        SubscribeLocalEvent<DnaModifierScannerComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<DnaModifierScannerComponent, SolutionTransferAttemptEvent>(OnTransferAttempt);
        SubscribeLocalEvent<DnaModifierScannerComponent, SolutionTransferredEvent>(OnTransferred);
        SubscribeLocalEvent<DnaModifierScannerComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
    }

    private void OnInit(Entity<DnaModifierScannerComponent> ent, ref ComponentInit args)
    {
        ent.Comp.BodyContainer = _containers.EnsureContainer<ContainerSlot>(ent, DnaModifierScannerComponent.ContainerId);
        _deviceLink.EnsureSinkPorts(ent, DnaModifierScannerComponent.ScannerPort);
    }

    private void OnCanDrop(Entity<DnaModifierScannerComponent> ent, ref CanDropTargetEvent args)
    {
        args.Handled = true;
        args.CanDrop |= CanInsert(args.Dragged);
    }

    public bool CanInsert(EntityUid target)
    {
        return HasComp<BodyComponent>(target) && _genetics.CanMutate(target);
    }

    private void OnRelayMovement(Entity<DnaModifierScannerComponent> ent, ref ContainerRelayMovementEntityEvent args)
    {
        if (!_blocker.CanInteract(args.Entity, ent))
            return;

        EjectBody(ent);
    }

    private void OnInsertVerb(Entity<DnaModifierScannerComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (args.Using == null || !args.CanAccess || !args.CanInteract || IsOccupied(ent.Comp) || !CanInsert(args.Using.Value))
            return;

        var toInsert = args.Using.Value;
        var name = MetaData(toInsert).EntityName;
        args.Verbs.Add(new InteractionVerb
        {
            Act = () => InsertBody(ent, toInsert),
            Category = VerbCategory.Insert,
            Text = name,
        });
    }

    private void OnAltVerbs(Entity<DnaModifierScannerComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (IsOccupied(ent.Comp))
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Act = () => EjectBody(ent),
                Category = VerbCategory.Eject,
                Text = Loc.GetString("medical-scanner-verb-noun-occupant"),
                Priority = 1,
            });
        }

        if (!IsOccupied(ent.Comp) && CanInsert(args.User) && _blocker.CanMove(args.User))
        {
            var user = args.User;
            args.Verbs.Add(new AlternativeVerb
            {
                Act = () => InsertBody(ent, user),
                Text = Loc.GetString("medical-scanner-verb-enter"),
            });
        }
    }

    private void OnDestroyed(Entity<DnaModifierScannerComponent> ent, ref DestructionEventArgs args)
    {
        EjectBody(ent);
    }

    private void OnDragDrop(Entity<DnaModifierScannerComponent> ent, ref DragDropTargetEvent args)
    {
        InsertBody(ent, args.Dragged);
    }

    private void OnPortDisconnected(Entity<DnaModifierScannerComponent> ent, ref PortDisconnectedEvent args)
    {
        ent.Comp.ConnectedConsole = null;
    }

    private void OnPowerChanged(Entity<DnaModifierScannerComponent> ent, ref PowerChangedEvent args)
    {
        UpdateAppearance(ent);
    }

    private void OnTransferAttempt(Entity<DnaModifierScannerComponent> ent, ref SolutionTransferAttemptEvent args)
    {
        if (args.To != ent.Owner)
            return;

        if (!_solution.TryGetDrainableSolution(args.From, out _, out var source))
            return;

        if (source.GetTotalPrototypeQuantity(ent.Comp.MutagenReagent) <= FixedPoint2.Zero)
            args.Cancel(Loc.GetString("genetics-scanner-need-mutagen"));
    }

    private void OnTransferred(Entity<DnaModifierScannerComponent> ent, ref SolutionTransferredEvent args)
    {
        if (args.To != ent.Owner)
            return;

        if (!_solution.TryGetSolution(ent.Owner, DnaModifierScannerComponent.MutagenSolution, out var soln, out var solution))
            return;

        for (var i = solution.Contents.Count - 1; i >= 0; i--)
        {
            var reagent = solution.Contents[i];
            if (reagent.Reagent.Prototype == ent.Comp.MutagenReagent)
                continue;

            _solution.RemoveReagent(soln.Value, reagent.Reagent, reagent.Quantity);
        }
    }

    private void OnSolutionChanged(Entity<DnaModifierScannerComponent> ent, ref SolutionContainerChangedEvent args)
    {
        if (args.SolutionId != DnaModifierScannerComponent.MutagenSolution)
            return;

        if (ent.Comp.ConnectedConsole != null)
            _console.UpdateUserInterface(ent.Comp.ConnectedConsole.Value);
    }

    public bool TryGetMutagen(EntityUid uid, out FixedPoint2 quantity, out FixedPoint2 max, DnaModifierScannerComponent? scanner = null)
    {
        quantity = FixedPoint2.Zero;
        max = FixedPoint2.Zero;
        if (!Resolve(uid, ref scanner, false))
            return false;

        if (!_solution.TryGetSolution(uid, DnaModifierScannerComponent.MutagenSolution, out _, out var solution))
            return false;

        quantity = solution.GetTotalPrototypeQuantity(scanner.MutagenReagent);
        max = solution.MaxVolume;
        return true;
    }

    public bool TryConsumeMutagen(EntityUid uid, FixedPoint2 amount, DnaModifierScannerComponent? scanner = null)
    {
        if (!Resolve(uid, ref scanner, false))
            return false;

        if (amount <= FixedPoint2.Zero)
            return true;

        if (!_solution.TryGetSolution(uid, DnaModifierScannerComponent.MutagenSolution, out var soln, out var solution))
            return false;

        if (solution.GetTotalPrototypeQuantity(scanner.MutagenReagent) < amount)
            return false;

        return _solution.RemoveReagent(soln.Value, scanner.MutagenReagent.ToString(), amount);
    }

    public static bool IsOccupied(DnaModifierScannerComponent scanner)
    {
        return scanner.BodyContainer.ContainedEntity != null;
    }

    public EntityUid? GetOccupant(DnaModifierScannerComponent scanner)
    {
        return scanner.BodyContainer.ContainedEntity;
    }

    public void InsertBody(EntityUid uid, EntityUid toInsert, DnaModifierScannerComponent? scanner = null)
    {
        if (!Resolve(uid, ref scanner))
            return;

        if (scanner.BodyContainer.ContainedEntity != null || !CanInsert(toInsert))
        {
            if (scanner.BodyContainer.ContainedEntity == null && _genetics.IsSteel(toInsert))
                _popup.PopupEntity(Loc.GetString("genetics-steel-no-mutate"), toInsert);
            return;
        }

        _containers.Insert(toInsert, scanner.BodyContainer);
        if (!HasComp<GenomeComponent>(toInsert))
            EnsureComp<GenomeComponent>(toInsert);
        UpdateAppearance((uid, scanner));
        if (scanner.ConnectedConsole != null)
            _console.UpdateUserInterface(scanner.ConnectedConsole.Value);
    }

    public void EjectBody(EntityUid uid, DnaModifierScannerComponent? scanner = null)
    {
        if (!Resolve(uid, ref scanner))
            return;

        if (scanner.BodyContainer.ContainedEntity is not { Valid: true } contained)
            return;

        _containers.Remove(contained, scanner.BodyContainer);
        _climb.ForciblySetClimbing(contained, uid);
        UpdateAppearance((uid, scanner));
        if (scanner.ConnectedConsole != null)
            _console.UpdateUserInterface(scanner.ConnectedConsole.Value);
    }

    private MedicalScannerStatus GetStatus(Entity<DnaModifierScannerComponent> ent)
    {
        if (!this.IsPowered(ent, EntityManager))
            return MedicalScannerStatus.Off;

        var body = ent.Comp.BodyContainer.ContainedEntity;
        if (body == null)
            return MedicalScannerStatus.Open;

        if (!TryComp<MobStateComponent>(body.Value, out var state))
            return MedicalScannerStatus.Yellow;

        if (_mobState.IsAlive(body.Value, state))
            return MedicalScannerStatus.Green;
        if (_mobState.IsCritical(body.Value, state))
            return MedicalScannerStatus.Red;
        if (_mobState.IsDead(body.Value, state))
            return MedicalScannerStatus.Death;

        return MedicalScannerStatus.Yellow;
    }

    private void UpdateAppearance(Entity<DnaModifierScannerComponent> ent)
    {
        _appearance.SetData(ent, MedicalScannerVisuals.Status, GetStatus(ent));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _updateDif += frameTime;
        if (_updateDif < UpdateRate)
            return;

        _updateDif -= UpdateRate;
        var query = EntityQueryEnumerator<DnaModifierScannerComponent>();
        while (query.MoveNext(out var uid, out var scanner))
            UpdateAppearance((uid, scanner));
    }
}
