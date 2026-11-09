using Content.Shared._Forge.Genetics.Mutations;
using Content.Shared.Actions;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Misc;
using Robust.Shared.Audio;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Genetics.Mutations;

/// <summary>
/// Telekinesis uses a body-mounted tether gun so the held object follows the mouse cursor.
/// Drops the hold if LOS / range is lost.
/// </summary>
public sealed partial class GeneticTelekinesisSystem : EntitySystem
{
    private static readonly TimeSpan LosCheckInterval = TimeSpan.FromSeconds(0.2);

    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTetherGunSystem _tether = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticTelekinesisComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<GeneticTelekinesisComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<GeneticTelekinesisComponent, GeneticTelekinesisActionEvent>(OnGrasp);
        SubscribeLocalEvent<GeneticTelekinesisComponent, GeneticTelekinesisFlingActionEvent>(OnFling);
        SubscribeLocalEvent<GeneticTelekinesisComponent, BeforeThrowEvent>(OnBeforeThrow);
    }

    private void OnStartup(Entity<GeneticTelekinesisComponent> ent, ref ComponentStartup args)
    {
        _actions.AddAction(ent.Owner, ref ent.Comp.ActionEntity, ent.Comp.Action);
        _actions.AddAction(ent.Owner, ref ent.Comp.FlingActionEntity, ent.Comp.FlingAction);

        if (HasComp<TetherGunComponent>(ent.Owner))
            return;

        var tether = EnsureComp<TetherGunComponent>(ent.Owner);
        tether.LineColor = Color.FromHex("#A060FF");
        tether.MassLimit = 50f;
        tether.MaxForce = 200f;
        tether.Frequency = 10f;
        tether.DampingRatio = 4f;
        tether.MaxDistance = ent.Comp.Range;
        tether.CanTetherAlive = false;
        tether.CanUnanchor = false;
        Dirty(ent.Owner, tether);
        ent.Comp.AddedTetherGun = true;
        Dirty(ent);
    }

    private void OnShutdown(Entity<GeneticTelekinesisComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<TetherGunComponent>(ent.Owner, out var tether))
            _tether.TryStopTether(ent.Owner, tether);

        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.FlingActionEntity);

        if (ent.Comp.AddedTetherGun)
            RemComp<TetherGunComponent>(ent.Owner);
    }

    private void OnBeforeThrow(Entity<GeneticTelekinesisComponent> ent, ref BeforeThrowEvent args)
    {
        args.ThrowSpeed *= ent.Comp.ThrowSpeedMultiplier;
        if (args.Direction.LengthSquared() > 0)
            args.Direction *= 1f + ent.Comp.ThrowRangeBonus / 8f;
    }

    private void OnGrasp(Entity<GeneticTelekinesisComponent> ent, ref GeneticTelekinesisActionEvent args)
    {
        if (args.Handled || !TryComp<TetherGunComponent>(ent.Owner, out var tether))
            return;

        var target = args.Target;

        if (tether.Tethered == target)
        {
            args.Handled = true;
            _tether.TryStopTether(ent.Owner, tether);
            _popup.PopupEntity(Loc.GetString("gene-telekinesis-release"), ent.Owner, ent.Owner);
            return;
        }

        if (!_interaction.InRangeUnobstructed(ent.Owner, target, range: ent.Comp.Range))
        {
            _popup.PopupEntity(Loc.GetString("gene-telekinesis-blocked"), ent.Owner, ent.Owner, PopupType.SmallCaution);
            return;
        }

        tether.MaxDistance = ent.Comp.Range;

        if (!_tether.TryTether(ent.Owner, target, ent.Owner, tether))
        {
            _popup.PopupEntity(Loc.GetString("gene-telekinesis-fail"), ent.Owner, ent.Owner, PopupType.SmallCaution);
            return;
        }

        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("gene-telekinesis-pull"), ent.Owner, ent.Owner);
    }

    private void OnFling(Entity<GeneticTelekinesisComponent> ent, ref GeneticTelekinesisFlingActionEvent args)
    {
        if (args.Handled || !TryComp<TetherGunComponent>(ent.Owner, out var tether))
            return;

        if (tether.Tethered == null)
        {
            _popup.PopupEntity(Loc.GetString("gene-telekinesis-nothing"), ent.Owner, ent.Owner, PopupType.SmallCaution);
            return;
        }

        if (!_tether.TryLaunchTethered(
                ent.Owner,
                tether,
                args.Target,
                ent.Comp.FlingForce,
                ent.Comp.FlingDistance,
                new SoundPathSpecifier("/Audio/Weapons/soup.ogg")))
        {
            _popup.PopupEntity(Loc.GetString("gene-telekinesis-fail"), ent.Owner, ent.Owner, PopupType.SmallCaution);
            return;
        }

        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("gene-telekinesis-fling"), ent.Owner, ent.Owner);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<GeneticTelekinesisComponent, TetherGunComponent>();
        while (query.MoveNext(out var uid, out var gene, out var tether))
        {
            if (tether.Tethered is not { } held)
                continue;

            if (gene.NextLosCheck > _timing.CurTime)
                continue;

            gene.NextLosCheck = _timing.CurTime + LosCheckInterval;

            // Uses telekinesis InRangeOverride → 5 tiles + real LOS raycast.
            if (_interaction.InRangeUnobstructed(uid, held, range: gene.Range))
                continue;

            _tether.TryStopTether(uid, tether);
            _popup.PopupEntity(Loc.GetString("gene-telekinesis-lost"), uid, uid, PopupType.SmallCaution);
        }
    }
}
