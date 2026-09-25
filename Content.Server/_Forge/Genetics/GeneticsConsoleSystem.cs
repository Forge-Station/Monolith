using Content.Server._Forge.Genetics.Components;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Power.EntitySystems;
using Content.Server.Stack;
using Content.Shared._Forge.Genetics;
using Content.Shared._Forge.Genetics.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Genetics;

public sealed class GeneticsConsoleSystem : EntitySystem
{
    [Dependency] private readonly DeviceLinkSystem _deviceLink = default!;
    [Dependency] private readonly DnaModifierScannerSystem _scanner = default!;
    [Dependency] private readonly GeneticsSystem _genetics = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticsConsoleComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<GeneticsConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<GeneticsConsoleComponent, NewLinkEvent>(OnNewLink);
        SubscribeLocalEvent<GeneticsConsoleComponent, PortDisconnectedEvent>(OnPortDisconnected);
        SubscribeLocalEvent<GeneticsConsoleComponent, AnchorStateChangedEvent>(OnAnchor);
        SubscribeLocalEvent<GeneticsConsoleComponent, AfterActivatableUIOpenEvent>(OnUiOpen);
        SubscribeLocalEvent<GeneticsConsoleComponent, GeneticsConsoleActionMessage>(OnAction);
        SubscribeLocalEvent<GeneticsConsoleComponent, PowerChangedEvent>(OnPowerChanged);
    }

    private void OnInit(Entity<GeneticsConsoleComponent> ent, ref ComponentInit args)
    {
        _deviceLink.EnsureSourcePorts(ent, GeneticsConsoleComponent.ScannerPort, GeneticsConsoleComponent.ServerPort);
    }

    private void OnMapInit(Entity<GeneticsConsoleComponent> ent, ref MapInitEvent args)
    {
        RefreshLinks(ent);
        RecheckRange(ent);
        UpdateUserInterface(ent);
    }

    private void OnNewLink(Entity<GeneticsConsoleComponent> ent, ref NewLinkEvent args)
    {
        if (args.SourcePort != GeneticsConsoleComponent.ScannerPort &&
            args.SourcePort != GeneticsConsoleComponent.ServerPort)
            return;

        RefreshLinks(ent);
        RecheckRange(ent);
        UpdateUserInterface(ent);
    }

    private void OnPortDisconnected(Entity<GeneticsConsoleComponent> ent, ref PortDisconnectedEvent args)
    {
        if (args.Port != GeneticsConsoleComponent.ScannerPort &&
            args.Port != GeneticsConsoleComponent.ServerPort)
            return;

        RefreshLinks(ent);
        RecheckRange(ent);
        UpdateUserInterface(ent);
    }

    private void RefreshLinks(Entity<GeneticsConsoleComponent> ent)
    {
        if (ent.Comp.Scanner is { } previous
            && TryComp<DnaModifierScannerComponent>(previous, out var previousScanner)
            && previousScanner.ConnectedConsole == ent.Owner)
            previousScanner.ConnectedConsole = null;

        ent.Comp.Scanner = null;
        ent.Comp.Servers.Clear();

        if (!TryComp<DeviceLinkSourceComponent>(ent, out var source))
            return;

        foreach (var (sink, links) in source.LinkedPorts)
        {
            foreach (var (sourcePort, _) in links)
            {
                if (sourcePort == GeneticsConsoleComponent.ScannerPort
                    && TryComp<DnaModifierScannerComponent>(sink, out var scanner))
                {
                    ent.Comp.Scanner = sink;
                    scanner.ConnectedConsole = ent;
                }

                if (sourcePort == GeneticsConsoleComponent.ServerPort
                    && HasComp<GeneticsServerComponent>(sink))
                    ent.Comp.Servers.Add(sink);
            }
        }
    }

    private void OnAnchor(Entity<GeneticsConsoleComponent> ent, ref AnchorStateChangedEvent args)
    {
        RecheckRange(ent);
        UpdateUserInterface(ent);
    }

    private void OnUiOpen(Entity<GeneticsConsoleComponent> ent, ref AfterActivatableUIOpenEvent args)
    {
        UpdateUserInterface(ent);
    }

    private void OnPowerChanged(Entity<GeneticsConsoleComponent> ent, ref PowerChangedEvent args)
    {
        UpdateUserInterface(ent);
    }

    private void OnAction(Entity<GeneticsConsoleComponent> ent, ref GeneticsConsoleActionMessage args)
    {
        if (!_power.IsPowered(ent))
            return;

        var hasOccupant = TryGetOccupant(ent, out var occupant);
        var journalPrint = args.Action is GeneticsConsoleAction.Isolate or GeneticsConsoleAction.Clean
                           && args.GeneId != null
                           && _prototypes.HasIndex<GenePrototype>(args.GeneId);

        if (!hasOccupant && args.Action != GeneticsConsoleAction.Eject && !journalPrint)
        {
            UpdateUserInterface(ent);
            return;
        }

        switch (args.Action)
        {
            case GeneticsConsoleAction.Eject:
                if (ent.Comp.Scanner is { } scanner)
                    _scanner.EjectBody(scanner);
                break;
            case GeneticsConsoleAction.Irradiate:
                if (!hasOccupant || ent.Comp.Scanner is not { } irradiateScanner)
                    break;
                if (!TryComp<DnaModifierScannerComponent>(irradiateScanner, out var irradiateComp) ||
                    !_scanner.TryConsumeMutagen(irradiateScanner, irradiateComp.IrradiateCost, irradiateComp))
                {
                    _popup.PopupEntity(Loc.GetString("genetics-irradiate-no-mutagen"), ent, args.Actor);
                    break;
                }

                _genetics.IrradiateSubject(occupant);
                break;
            case GeneticsConsoleAction.Activate:
                if (!hasOccupant)
                    break;
                if (args.GeneId != null && Enum.TryParse(args.GeneId, out GeneBranch expressBranch))
                    _genetics.TryExpressBranch(occupant, expressBranch);
                else if (args.GeneId != null)
                    _genetics.TryActivateGene(occupant, args.GeneId);
                break;
            case GeneticsConsoleAction.Deactivate:
                if (!hasOccupant)
                    break;
                if (args.GeneId != null && Enum.TryParse(args.GeneId, out GeneBranch dropBranch))
                    _genetics.TryDeactivateBranch(occupant, dropBranch, requireAssembled: true);
                else if (args.GeneId != null)
                    _genetics.TryDeactivateGene(occupant, args.GeneId, requireAssembled: true);
                break;
            case GeneticsConsoleAction.Isolate:
                if (args.GeneId != null && Enum.TryParse(args.GeneId, out GeneBranch isolateBranch))
                {
                    if (hasOccupant && TryResolvePrintGene(occupant, isolateBranch.ToString(), requireAssembled: true, out var isolateId))
                        TryPrintInjector(ent, isolateId, activate: true, requireAssembled: true);
                }
                else if (args.GeneId != null)
                    TryPrintInjector(ent, args.GeneId, activate: true, requireAssembled: false);
                break;
            case GeneticsConsoleAction.Clean:
                if (args.GeneId != null && Enum.TryParse(args.GeneId, out GeneBranch cleanBranch))
                {
                    if (hasOccupant && TryResolvePrintGene(occupant, cleanBranch.ToString(), requireAssembled: false, out var cleanId))
                        TryPrintInjector(ent, cleanId, activate: false, requireAssembled: false);
                }
                else if (args.GeneId != null)
                    TryPrintInjector(ent, args.GeneId, activate: false, requireAssembled: false);
                break;
            case GeneticsConsoleAction.PulseBlock:
                if (!hasOccupant || args.GeneId == null || args.BlockIndex is not { } block)
                    break;
                if (ent.Comp.Scanner is not { } pulseScanner ||
                    !TryComp<DnaModifierScannerComponent>(pulseScanner, out var pulseComp) ||
                    !_scanner.TryConsumeMutagen(pulseScanner, pulseComp.PulseCost, pulseComp))
                {
                    _popup.PopupEntity(Loc.GetString("genetics-pulse-no-mutagen"), ent, args.Actor);
                    break;
                }

                _genetics.PulseBranchBlock(occupant, args.GeneId, block);
                break;
        }

        UpdateUserInterface(ent);
    }

    private bool TryGetOccupant(Entity<GeneticsConsoleComponent> ent, out EntityUid occupant)
    {
        occupant = default;
        RecheckRange(ent);
        if (ent.Comp.Scanner is not { } scannerUid || !ent.Comp.ScannerInRange)
            return false;

        if (!TryComp<DnaModifierScannerComponent>(scannerUid, out var scanner))
            return false;

        var contained = _scanner.GetOccupant(scanner);
        if (contained == null)
            return false;

        occupant = contained.Value;
        return true;
    }

    private void TryPrintInjector(Entity<GeneticsConsoleComponent> ent, string geneId, bool activate, bool requireAssembled)
    {
        if (!_prototypes.TryIndex<GenePrototype>(geneId, out var proto))
            return;

        if (!_genetics.IsDiscovered(ent, geneId))
            return;

        if (requireAssembled)
        {
            if (!TryGetOccupant(ent, out var occupant) || !TryComp<GenomeComponent>(occupant, out var genome))
                return;

            if (activate && !_genetics.IsAssembled(genome, proto))
                return;
        }

        var injector = Spawn(ent.Comp.InjectorPrototype, Transform(ent).Coordinates);
        var comp = EnsureComp<DnaInjectorComponent>(injector);
        comp.Genes = new() { geneId };
        comp.Activate = activate;
        comp.Used = false;
        Dirty(injector, comp);
        _stack.TryMergeToContacts(injector);
    }

    private bool TryResolvePrintGene(EntityUid occupant, string selectedId, bool requireAssembled, out string geneId)
    {
        if (Enum.TryParse(selectedId, out GeneBranch branch))
            return _genetics.TryGetPrintableGene(occupant, branch, requireAssembled, out geneId);

        geneId = selectedId;
        return _prototypes.HasIndex<GenePrototype>(selectedId) && _genetics.IsDiscovered(occupant, selectedId);
    }

    public void RecheckRange(EntityUid uid, GeneticsConsoleComponent? console = null)
    {
        if (!Resolve(uid, ref console, false))
            return;

        RecheckRange((uid, console));
    }

    private void RecheckRange(Entity<GeneticsConsoleComponent> ent)
    {
        if (ent.Comp.Scanner is not { } scanner)
        {
            ent.Comp.ScannerInRange = false;
            return;
        }

        var consoleXform = Transform(ent);
        var scannerXform = Transform(scanner);
        ent.Comp.ScannerInRange = consoleXform.Coordinates.TryDistance(EntityManager, scannerXform.Coordinates, out var dist)
                                  && dist <= ent.Comp.MaxDistance;
    }

    public void UpdateUserInterface(EntityUid uid, GeneticsConsoleComponent? console = null)
    {
        if (!Resolve(uid, ref console, false))
            return;

        RecheckRange((uid, console));

        var state = new GeneticsConsoleBoundUserInterfaceState
        {
            Powered = _power.IsPowered(uid),
            ScannerConnected = console.Scanner != null,
            ScannerInRange = console.ScannerInRange,
        };

        if (console.Scanner is { } linkedScanner && TryComp<DnaModifierScannerComponent>(linkedScanner, out var mutagenScanner))
        {
            state.IrradiateCost = mutagenScanner.IrradiateCost.Float();
            state.PulseCost = mutagenScanner.PulseCost.Float();
            if (_scanner.TryGetMutagen(linkedScanner, out var mutagen, out var mutagenMax, mutagenScanner))
            {
                state.Mutagen = mutagen.Float();
                state.MutagenMax = mutagenMax.Float();
            }
        }

        if (TryGetOccupant((uid, console), out var occupant))
        {
            state.OccupantPresent = true;
            state.OccupantCritical = _genetics.CanIrradiate(occupant) == false;
            state.OccupantName = Identity.Name(occupant, EntityManager);
            state.UniqueDna = _genetics.GetUniqueDna(occupant);

            if (TryComp<GenomeComponent>(occupant, out var genome))
            {
                _genetics.EnsureBranches(genome);
                _genetics.RecalculateInstability(genome);
                state.Instability = genome.Instability;
                state.InstabilityThreshold = genome.InstabilityThreshold;
                foreach (GeneBranch branch in Enum.GetValues<GeneBranch>())
                {
                    var strand = _genetics.GetBranchSequence(genome, branch);
                    var expressed = false;
                    var discovered = false;
                    var canIsolate = false;
                    var canDeactivate = false;
                    GenePrototype? known = null;

                    foreach (var geneId in _genetics.EnumerateRoundGenes(branch))
                    {
                        if (!_prototypes.TryIndex<GenePrototype>(geneId, out var proto))
                            continue;

                        var assembled = _genetics.IsAssembled(genome, proto);
                        var active = genome.Genes.TryGetValue(geneId, out var geneState) && geneState.Active;
                        expressed |= active;

                        if (!_genetics.IsDiscovered(uid, geneId))
                            continue;

                        discovered = true;
                        known ??= proto;
                        if (active || assembled)
                            known = proto;

                        canIsolate |= assembled;
                        canDeactivate |= active && assembled;
                    }

                    string description;
                    if (expressed && known != null && canDeactivate)
                        description = Loc.GetString(known.Description);
                    else if (expressed && known != null)
                        description = Loc.GetString("genetics-console-phenotype-locked");
                    else if (expressed)
                        description = Loc.GetString("genetics-console-phenotype-unknown");
                    else if (discovered)
                        description = Loc.GetString("genetics-console-phenotype-known-idle");
                    else
                        description = Loc.GetString("genetics-console-phenotype-blind");

                    var title = Loc.GetString($"genetics-branch-{branch.ToString().ToLowerInvariant()}");
                    if (expressed && known != null)
                        title = Loc.GetString("genetics-console-branch-known",
                            ("branch", title),
                            ("name", Loc.GetString(known.Name)));
                    else if (expressed)
                        title = Loc.GetString("genetics-console-branch-expressed", ("branch", title));

                    state.Branches.Add(new GeneticsConsoleBranchEntry
                    {
                        Id = branch.ToString(),
                        Branch = branch,
                        Sequence = new List<string>(strand),
                        Expressed = expressed,
                        Discovered = discovered,
                        CanIsolate = canIsolate,
                        CanClean = discovered,
                        CanDeactivate = canDeactivate,
                        Title = title,
                        Description = description,
                        BlockHints = genome.AssemblyHints.TryGetValue(branch, out var hints)
                            ? new List<byte>(hints)
                            : new(),
                    });
                }
            }
        }

        state.Discoveries = _genetics.GetDiscoveryJournal(uid);

        _ui.SetUiState(uid, GeneticsConsoleUiKey.Key, state);
    }
}
