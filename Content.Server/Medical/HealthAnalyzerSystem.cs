using Content.Server.Body.Components;
using Content.Server.Medical.Components;
using Content.Server.PowerCell;
using Content.Server.Temperature.Components;
using Content.Shared.Traits.Assorted;
using Content.Shared.Chemistry.Components; // Forge-Change
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent; // Forge-Change
using Content.Shared.FixedPoint; // Forge-Change
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.MedicalScanner;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;
using Content.Server._NF.Traits.Assorted; // Frontier

// Shitmed Change
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared._Shitmed.Targeting;
using System.Linq;

namespace Content.Server.Medical;

public sealed partial class HealthAnalyzerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PowerCellSystem _cell = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedBodySystem _bodySystem = default!; // Shitmed Change
    [Dependency] private ItemToggleSystem _toggle = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<HealthAnalyzerComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<HealthAnalyzerComponent, HealthAnalyzerDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<HealthAnalyzerComponent, EntGotInsertedIntoContainerMessage>(OnInsertedIntoContainer);
        SubscribeLocalEvent<HealthAnalyzerComponent, ItemToggledEvent>(OnToggled);
        SubscribeLocalEvent<HealthAnalyzerComponent, DroppedEvent>(OnDropped);
        // Shitmed Change Start
        Subs.BuiEvents<HealthAnalyzerComponent>(HealthAnalyzerUiKey.Key, subs =>
        {
            subs.Event<HealthAnalyzerPartMessage>(OnHealthAnalyzerPartSelected);
        });
        // Shitmed Change End
    }

    public override void Update(float frameTime)
    {
        var analyzerQuery = EntityQueryEnumerator<HealthAnalyzerComponent, TransformComponent>();
        while (analyzerQuery.MoveNext(out var uid, out var component, out var transform))
        {
            //Update rate limited to 1 second
            if (component.NextUpdate > _timing.CurTime)
                continue;

            if (component.ScannedEntity is not {} patient)
                continue;

            if (Deleted(patient))
            {
                StopAnalyzingEntity((uid, component), patient);
                continue;
            }

            // Shitmed Change Start
            if (component.CurrentBodyPart != null
                && (Deleted(component.CurrentBodyPart)
                    || TryComp(component.CurrentBodyPart, out BodyPartComponent? bodyPartComponent)
                    && bodyPartComponent.Body is null))
            {
                BeginAnalyzingEntity((uid, component), patient, null);
                continue;
            }
            // Shitmed Change End

            component.NextUpdate = _timing.CurTime + component.UpdateInterval;

            //Get distance between health analyzer and the scanned entity
            var patientCoordinates = Transform(patient).Coordinates;
            if (!_transformSystem.InRange(patientCoordinates, transform.Coordinates, component.MaxScanRange))
            {
                //Range too far, disable updates until they are back in range
                PauseAnalyzingEntity((uid, component), patient);
                continue;
            }

            component.IsAnalyzerActive = true;
            UpdateScannedUser(uid, patient, true, component.CurrentBodyPart); // Shitmed Change
        }
    }

    /// <summary>
    /// Trigger the doafter for scanning
    /// </summary>
    private void OnAfterInteract(Entity<HealthAnalyzerComponent> uid, ref AfterInteractEvent args)
    {
        if (args.Target == null || !args.CanReach || !HasComp<MobStateComponent>(args.Target) || !_cell.HasDrawCharge(uid, user: args.User))
            return;

        _audio.PlayPvs(uid.Comp.ScanningBeginSound, uid);

        var doAfterCancelled = !_doAfterSystem.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, uid.Comp.ScanDelay, new HealthAnalyzerDoAfterEvent(), uid, target: args.Target, used: uid)
        {
            NeedHand = true,
            BreakOnMove = true,
        });

        if (args.Target == args.User || doAfterCancelled || uid.Comp.Silent)
            return;

        var msg = Loc.GetString("health-analyzer-popup-scan-target", ("user", Identity.Entity(args.User, EntityManager)));
        _popupSystem.PopupEntity(msg, args.Target.Value, args.Target.Value, PopupType.Medium);
    }

    private void OnDoAfter(Entity<HealthAnalyzerComponent> uid, ref HealthAnalyzerDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target == null || !_cell.HasDrawCharge(uid, user: args.User))
            return;

        if (!uid.Comp.Silent)
            _audio.PlayPvs(uid.Comp.ScanningEndSound, uid);

        OpenUserInterface(args.User, uid);
        BeginAnalyzingEntity(uid, args.Target.Value);
        args.Handled = true;
    }

    /// <summary>
    /// Turn off when placed into a storage item or moved between slots/hands
    /// </summary>
    private void OnInsertedIntoContainer(Entity<HealthAnalyzerComponent> uid, ref EntGotInsertedIntoContainerMessage args)
    {
        if (uid.Comp.ScannedEntity is { } patient)
            _toggle.TryDeactivate(uid.Owner);
    }

    /// <summary>
    /// Disable continuous updates once turned off
    /// </summary>
    private void OnToggled(Entity<HealthAnalyzerComponent> ent, ref ItemToggledEvent args)
    {
        if (!args.Activated && ent.Comp.ScannedEntity is { } patient)
            StopAnalyzingEntity(ent, patient);
    }

    /// <summary>
    /// Turn off the analyser when dropped
    /// </summary>
    private void OnDropped(Entity<HealthAnalyzerComponent> uid, ref DroppedEvent args)
    {
        if (uid.Comp.ScannedEntity is { } patient)
            _toggle.TryDeactivate(uid.Owner);
    }

    private void OpenUserInterface(EntityUid user, EntityUid analyzer)
    {
        if (!_uiSystem.HasUi(analyzer, HealthAnalyzerUiKey.Key))
            return;

        _uiSystem.OpenUi(analyzer, HealthAnalyzerUiKey.Key, user);
    }

    /// <summary>
    /// Mark the entity as having its health analyzed, and link the analyzer to it
    /// </summary>
    /// <param name="healthAnalyzer">The health analyzer that should receive the updates</param>
    /// <param name="target">The entity to start analyzing</param>
    /// <param name="part">Shitmed Change: The body part to analyze, if any</param>
    public void BeginAnalyzingEntity(Entity<HealthAnalyzerComponent> healthAnalyzer, EntityUid target, EntityUid? part = null)
    {
        //Link the health analyzer to the scanned entity
        healthAnalyzer.Comp.ScannedEntity = target;
        healthAnalyzer.Comp.CurrentBodyPart = part; // Shitmed Change

        _toggle.TryActivate(healthAnalyzer.Owner);

        UpdateScannedUser(healthAnalyzer, target, true, part); // Shitmed Change
    }

    /// <summary>
    /// If the scanner is active, sends one last update and sets it to inactive.
    /// </summary>
    /// <param name="healthAnalyzer">The health analyzer that's receiving the updates</param>
    /// <param name="target">The entity to analyze</param>
    private void PauseAnalyzingEntity(Entity<HealthAnalyzerComponent> healthAnalyzer, EntityUid target)
    {
        if (!healthAnalyzer.Comp.IsAnalyzerActive)
            return;

        UpdateScannedUser(healthAnalyzer, target, false);
        healthAnalyzer.Comp.IsAnalyzerActive = false;
    }

    /// <summary>
    /// Remove the analyzer from the active list, and remove the component if it has no active analyzers
    /// </summary>
    /// <param name="healthAnalyzer">The health analyzer that's receiving the updates</param>
    /// <param name="target">The entity to analyze</param>
    public void StopAnalyzingEntity(Entity<HealthAnalyzerComponent> healthAnalyzer, EntityUid target)
    {
        //Unlink the analyzer
        healthAnalyzer.Comp.ScannedEntity = null;
        healthAnalyzer.Comp.CurrentBodyPart = null; // Shitmed Change

        _toggle.TryDeactivate(healthAnalyzer.Owner);

        UpdateScannedUser(healthAnalyzer, target, false);
    }

    // Shitmed Change Start
    /// <summary>
    /// Shitmed Change: Handle the selection of a body part on the health analyzer
    /// </summary>
    /// <param name="healthAnalyzer">The health analyzer that's receiving the updates</param>
    /// <param name="args">The message containing the selected part</param>
    private void OnHealthAnalyzerPartSelected(Entity<HealthAnalyzerComponent> healthAnalyzer, ref HealthAnalyzerPartMessage args)
    {
        if (!TryGetEntity(args.Owner, out var owner))
            return;

        if (args.BodyPart == null)
        {
            BeginAnalyzingEntity(healthAnalyzer, owner.Value, null);
        }
        else
        {
            var (targetType, targetSymmetry) = _bodySystem.ConvertTargetBodyPart(args.BodyPart.Value);
            if (_bodySystem.GetBodyChildrenOfType(owner.Value, targetType, symmetry: targetSymmetry) is { } part)
                BeginAnalyzingEntity(healthAnalyzer, owner.Value, part.FirstOrDefault().Id);
        }
    }
    // Shitmed Change End

    /// <summary>
    /// Send an update for the target to the healthAnalyzer
    /// </summary>
    /// <param name="healthAnalyzer">The health analyzer</param>
    /// <param name="target">The entity being scanned</param>
    /// <param name="scanMode">True makes the UI show ACTIVE, False makes the UI show INACTIVE</param>
    /// <param name="part">Shitmed Change: The body part being scanned, if any</param>
    public void UpdateScannedUser(EntityUid healthAnalyzer, EntityUid target, bool scanMode, EntityUid? part = null)
    {
        if (!_uiSystem.HasUi(healthAnalyzer, HealthAnalyzerUiKey.Key))
            return;

        if (!HasComp<DamageableComponent>(target))
            return;

        var bodyTemperature = float.NaN;

        if (TryComp<TemperatureComponent>(target, out var temp))
            bodyTemperature = temp.CurrentTemperature;

        var bloodAmount = float.NaN;
        var bleeding = false;
        List<ReagentQuantity>? chemicalReagents = null; // Forge-Change
        var unrevivable = false;
        var uncloneable = false; // Frontier

        if (TryComp<BloodstreamComponent>(target, out var bloodstream))
        {
            if (_solutionContainerSystem.ResolveSolution(target, bloodstream.BloodSolutionName,
                    ref bloodstream.BloodSolution, out var bloodSolution))
            {
                bloodAmount = bloodSolution.FillFraction;
                bleeding = bloodstream.BleedAmount > 0;
            }

            // Forge-Change-Start
            if (_solutionContainerSystem.ResolveSolution(target, bloodstream.ChemicalSolutionName,
                    ref bloodstream.ChemicalSolution, out var chemicalSolution))
            {
                chemicalReagents = GetChemicalReagents(chemicalSolution);
            }
        }
            // Forge-Change-End

        // Shitmed Change Start
        Dictionary<TargetBodyPart, TargetIntegrity>? body = null;
        if (HasComp<TargetingComponent>(target))
            body = _bodySystem.GetBodyPartStatus(target);
        // Shitmed Change End

        if (TryComp<UnrevivableComponent>(target, out var unrevivableComp) && unrevivableComp.Analyzable)
            unrevivable = true;

        if (TryComp<UncloneableComponent>(target, out var uncloneableComp) && uncloneableComp.Analyzable) // Frontier
            uncloneable = true; // Frontier

        _uiSystem.ServerSendUiMessage(healthAnalyzer, HealthAnalyzerUiKey.Key, new HealthAnalyzerScannedUserMessage(
            GetNetEntity(target),
            bodyTemperature,
            bloodAmount,
            scanMode,
            bleeding,
            unrevivable,
            uncloneable, // Frontier
            // Shitmed Change
            body,
            part != null ? GetNetEntity(part) : null,
            chemicalReagents // Forge-Change
        ));
    }

    // Forge-Change-Start
    public static List<ReagentQuantity>? GetChemicalReagents(Solution chemicalSolution)
    {
        List<ReagentQuantity>? reagents = null;

        foreach (var quantity in chemicalSolution.Contents)
        {
            if (quantity.Quantity <= FixedPoint2.Zero)
                continue;

            reagents ??= new();
            reagents.Add(quantity);
        }

        return reagents;
    }
    // Forge-Change-End
}
