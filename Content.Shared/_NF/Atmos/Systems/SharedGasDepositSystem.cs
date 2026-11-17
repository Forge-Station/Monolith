using Content.Shared._Forge.Atmos.Components;
using Content.Shared._NF.Atmos.Components;
using Content.Shared.Atmos.Piping.Binary.Components;
using Content.Shared.Construction.Components;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Shared._NF.Atmos.Systems;

public abstract partial class SharedGasDepositSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] protected SharedUserInterfaceSystem UI = default!;

    /// <summary>
    /// How far from the extractor we search for a gas cloud when anchoring.
    /// </summary>
    private const float CloudAnchorSearchRange = 24f;

    // The amount reported in a given extractor is a multiple of this.
    const float DrillExamineAmountRound = 1000.0f;

    public override void Initialize()
    {
        base.Initialize();


        SubscribeLocalEvent<GasDepositExtractorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<GasDepositExtractorComponent, AnchorAttemptEvent>(OnAnchorAttempt);
        SubscribeLocalEvent<GasDepositExtractorComponent, ActivateInWorldEvent>(OnPumpActivate);
    }

    private void OnExamined(Entity<GasDepositExtractorComponent> ent, ref ExaminedEvent args)
    {
        if (!Transform(ent).Anchored || !args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("gas-deposit-drill-system-examined",
            ("statusColor", "lightblue"),
            ("pressure", ent.Comp.TargetPressure)));
        if (_net.IsServer && TryComp(ent.Comp.DepositEntity, out GasDepositComponent? deposit))
        {
            // Mono
            if (deposit.YieldBased)
            {
                var hitMinimum = deposit.Yield == deposit.MinYield;
                args.PushMarkup(Loc.GetString("gas-deposit-drill-system-examined-yield",
                    ("statusColor", "lightblue"),
                    ("yield", deposit.Yield * 100f),
                    ("hitMinimum", hitMinimum)));
            }
            else
            {
                float estimatedAmount = MathF.Round(deposit.GasLeft / DrillExamineAmountRound) * DrillExamineAmountRound;
                args.PushMarkup(Loc.GetString("gas-deposit-drill-system-examined-amount",
                    ("statusColor", "lightblue"),
                    ("value", estimatedAmount)));
            }
        }
    }

    public void OnAnchorAttempt(Entity<GasDepositExtractorComponent> ent, ref AnchorAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (!TryComp(ent, out TransformComponent? xform)
            || xform.GridUid is not { Valid: true } grid
            || !TryComp(grid, out MapGridComponent? gridComp))
        {
            args.Cancel();
            return;
        }

        var indices = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
        var enumerator = _map.GetAnchoredEntitiesEnumerator(grid, gridComp, indices);

        while (enumerator.MoveNext(out var otherEnt))
        {
            // Don't match yourself.
            if (otherEnt == ent)
                continue;

            if (!HasComp<GasDepositComponent>(otherEnt))
                continue;

            var isCloud = HasComp<GasCloudComponent>(otherEnt);
            var isSiphon = HasComp<GasCloudSiphonComponent>(ent);

            // Drills harvest solid deposits; siphons harvest clouds.
            if (isCloud != isSiphon)
                continue;

            ent.Comp.DepositEntity = otherEnt.Value;
            return;
        }

        if (HasComp<GasCloudSiphonComponent>(ent) && TryFindGasCloud(ent, xform, out var cloudUid))
        {
            ent.Comp.DepositEntity = cloudUid;
            return;
        }

        var failKey = HasComp<GasCloudSiphonComponent>(ent)
            ? "gas-cloud-siphon-no-cloud"
            : "gas-deposit-drill-no-resources";
        _popup.PopupPredicted(Loc.GetString(failKey), ent, args.User);
        args.Cancel();
    }

    private void OnPumpActivate(Entity<GasDepositExtractorComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        if (!TryComp(args.User, out ActorComponent? actor))
            return;

        if (Transform(ent).Anchored)
        {
            UI.OpenUi(ent.Owner, GasPressurePumpUiKey.Key, actor.PlayerSession);
            Dirty(ent);
        }
        else
        {
            _popup.PopupCursor(Loc.GetString("ui-needs-anchor"), args.User);
        }

        args.Handled = true;
    }

    private bool TryFindGasCloud(Entity<GasDepositExtractorComponent> ent, TransformComponent xform, out EntityUid cloudUid)
    {
        cloudUid = EntityUid.Invalid;
        var extractorMap = _transform.GetMapCoordinates(ent.Owner, xform);
        var bestDistance = CloudAnchorSearchRange;
        var found = false;

        var query = EntityQueryEnumerator<GasCloudComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var cloud, out var cloudXform))
        {
            if (cloud.CurrentRadius < 0.5f)
                continue;

            var cloudMap = _transform.GetMapCoordinates(uid, cloudXform);
            if (cloudMap.MapId != extractorMap.MapId)
                continue;

            var distance = (cloudMap.Position - extractorMap.Position).Length();
            if (distance > cloud.CurrentRadius || distance > bestDistance)
                continue;

            bestDistance = distance;
            cloudUid = uid;
            found = true;
        }

        return found;
    }
}
