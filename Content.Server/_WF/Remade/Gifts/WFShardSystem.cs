using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Monolith;
using Content.Server._WF.Remade.Structures;
using Content.Server.Cargo.Components;
using Content.Server.Kitchen.Components;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._WF.Remade;
using Content.Shared.Examine;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Power.Components;
using Content.Shared.Pulling.Events;
using Content.Shared.Tools.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Gifts;

/// <summary>
/// Shards. A sharp tool or a welder on an anchored Scion chips one off and adds resonance. A shard is a power cell that never runs
/// down. It sells high while its Scion is quiet. Once its Scion reaches Growth, a shard left still for two minutes roots and grows a
/// little corruption, and needs a sharp tool to cut free. A shard goes when its Scion does.
/// </summary>
public sealed partial class WFShardSystem : EntitySystem
{
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCorruptionSystem _corruption = default!;
    [Dependency] private WFMonolithSystem _monolith = default!;

    public static readonly EntProtoId ShardProto = "WFMonolithShard";

    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(1);
    private static readonly SoundSpecifier ChipSound = new SoundCollectionSpecifier("MetalThud");
    private const float ChipSeconds = 8f;
    private const float WelderFuel = 5f;
    private const float MoveTolerance = 0.25f;

    private TimeSpan _nextTick;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFMonolithComponent, InteractUsingEvent>(OnChipUse);
        SubscribeLocalEvent<WFMonolithComponent, WFMonolithChipDoAfterEvent>(OnChipped);
        SubscribeLocalEvent<WFMonolithShardComponent, InteractUsingEvent>(OnCutUse);
        SubscribeLocalEvent<WFMonolithShardComponent, WFMonolithShardCutDoAfterEvent>(OnCut);
        SubscribeLocalEvent<WFMonolithShardComponent, GettingPickedUpAttemptEvent>(OnPickup);
        SubscribeLocalEvent<WFMonolithShardComponent, BeingPulledAttemptEvent>(OnPulled);
        SubscribeLocalEvent<WFMonolithShardComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WFMonolithShardComponent, EntityTerminatingEvent>(OnTerminating);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextTick)
            return;

        _nextTick = now + TickRate;
        var shards = EntityQueryEnumerator<WFMonolithShardComponent>();
        while (shards.MoveNext(out var uid, out var shard))
        {
            Tick((uid, shard), now);
        }
    }

    private void Tick(Entity<WFMonolithShardComponent> shard, TimeSpan now)
    {
        if (shard.Comp.Parent is not { } parent || TerminatingOrDeleted(parent) || !TryComp<WFMonolithComponent>(parent, out var monolith))
        {
            QueueDel(shard);
            return;
        }

        var stage = monolith.Stage;
        _appearance.SetData(shard, WFMonolithVisuals.Stage, stage);

        // The same refusal as the Scion: a buyer only takes one while it is quiet.
        if (stage == WFMonolithStage.Quiet)
            RemComp<CargoSellBlacklistComponent>(shard);
        else
            EnsureComp<CargoSellBlacklistComponent>(shard);

        // The cell works anywhere but on or near a main station.
        var barred = _corruption.IsSanctuaryAt(shard);
        if (TryComp<BatterySelfRechargerComponent>(shard, out var recharger))
            recharger.AutoRecharge = !barred;

        if (!barred && TryComp<BatteryComponent>(shard, out var battery))
            _battery.SetCharge(shard, battery.MaxCharge, battery);

        TrackStill((shard, shard.Comp), (parent, monolith), now);
    }

    /// <summary>Roots a shard that has lain still long enough, and uproots one that has been moved.</summary>
    private void TrackStill(Entity<WFMonolithShardComponent> shard, Entity<WFMonolithComponent> parent, TimeSpan now)
    {
        var comp = shard.Comp;
        var xform = Transform(shard);
        if (_container.IsEntityInContainer(shard) || !_transform.InRange(comp.Last, xform.Coordinates, MoveTolerance))
        {
            if (comp.Deployed)
                Undeploy(shard);

            comp.StillSince = now;
            comp.Last = xform.Coordinates;
            return;
        }

        if (comp.Deployed || parent.Comp.Stage < WFMonolithStage.Growth || now - comp.StillSince < comp.DeployAfter)
            return;

        Deploy(shard, parent);
    }

    private void Deploy(Entity<WFMonolithShardComponent> shard, Entity<WFMonolithComponent> parent)
    {
        if (!TryComp<WFCorruptionNodeComponent>(shard, out var node) || !_corruption.SeedShard(shard, (shard, node), parent))
            return;

        shard.Comp.Deployed = true;
        _popup.PopupEntity(Loc.GetString("wf-shard-rooted"), shard, PopupType.MediumCaution);
    }

    /// <summary>Plants a shard of a woken stone already rooted where it lands. Null if nothing can root there.</summary>
    public EntityUid? PlantRooted(Entity<WFMonolithComponent> parent, EntityCoordinates at)
    {
        var shard = Spawn(ShardProto, at);
        var comp = Comp<WFMonolithShardComponent>(shard);
        comp.Parent = parent;
        comp.Last = Transform(shard).Coordinates;
        comp.StillSince = _timing.CurTime;
        parent.Comp.Shards.Add(shard);
        Deploy((shard, comp), parent);
        if (comp.Deployed)
            return shard;

        parent.Comp.Shards.Remove(shard);
        Del(shard);
        return null;
    }

    private void Undeploy(Entity<WFMonolithShardComponent> shard)
    {
        shard.Comp.Deployed = false;
        if (TryComp<WFCorruptionNodeComponent>(shard, out var node))
            _corruption.DetachNode((shard, node));
    }

    private void OnChipUse(Entity<WFMonolithComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var tool = args.Used;
        var welder = _tool.HasQuality(tool, "Welding");
        if (!welder && !HasComp<SharpComponent>(tool))
            return;

        args.Handled = true;
        if (!Transform(ent).Anchored)
        {
            _popup.PopupEntity(Loc.GetString("wf-shard-chip-loose"), ent, args.User);
            return;
        }

        if (!CanChip(ent, args.User))
            return;

        _popup.PopupEntity(Loc.GetString("wf-shard-chip-start", ("tool", tool)), ent, args.User);
        if (welder)
        {
            _tool.UseTool(tool, args.User, ent, ChipSeconds * 1.5f, "Welding", new WFMonolithChipDoAfterEvent(), WelderFuel);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ChipSeconds, new WFMonolithChipDoAfterEvent(), ent, ent, tool)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private bool CanChip(Entity<WFMonolithComponent> ent, EntityUid user)
    {
        if (_monolith.IsBarred(ent))
        {
            _popup.PopupEntity(Loc.GetString("wf-shard-chip-barred"), ent, user);
            return false;
        }

        if (ent.Comp.Shards.Count >= WFMonolithMath.MaxShards)
        {
            _popup.PopupEntity(Loc.GetString("wf-shard-chip-none-left"), ent, user);
            return false;
        }

        return true;
    }

    private void OnChipped(Entity<WFMonolithComponent> ent, ref WFMonolithChipDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        if (!Transform(ent).Anchored || !CanChip(ent, args.User))
            return;

        var shard = Spawn(ShardProto, Transform(ent).Coordinates);
        Comp<WFMonolithShardComponent>(shard).Parent = ent;
        ent.Comp.Shards.Add(shard);
        _monolith.AddResonance(ent, WFMonolithMath.ChipResonance);
        _audio.PlayPvs(ChipSound, ent);
        _popup.PopupEntity(Loc.GetString("wf-shard-chipped"), ent, args.User, PopupType.MediumCaution);
    }

    private void OnCutUse(Entity<WFMonolithShardComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !ent.Comp.Deployed || !HasComp<SharpComponent>(args.Used))
            return;

        args.Handled = true;
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ent.Comp.CutSeconds, new WFMonolithShardCutDoAfterEvent(), ent, ent, args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private void OnCut(Entity<WFMonolithShardComponent> ent, ref WFMonolithShardCutDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !ent.Comp.Deployed)
            return;

        args.Handled = true;
        Undeploy(ent);
        ent.Comp.StillSince = _timing.CurTime;
        _popup.PopupEntity(Loc.GetString("wf-shard-cut-free"), ent, args.User);
    }

    private void OnPickup(Entity<WFMonolithShardComponent> ent, ref GettingPickedUpAttemptEvent args)
    {
        if (!ent.Comp.Deployed)
            return;

        args.Cancel();
        _popup.PopupEntity(Loc.GetString("wf-shard-stuck"), ent, args.User);
    }

    private void OnPulled(Entity<WFMonolithShardComponent> ent, ref BeingPulledAttemptEvent args)
    {
        if (ent.Comp.Deployed)
            args.Cancel();
    }

    private void OnExamined(Entity<WFMonolithShardComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(ent.Comp.Deployed ? "wf-shard-examine-rooted" : "wf-shard-examine"));
    }

    private void OnTerminating(Entity<WFMonolithShardComponent> ent, ref EntityTerminatingEvent args)
    {
        if (TryComp<WFMonolithComponent>(ent.Comp.Parent, out var monolith))
            monolith.Shards.Remove(ent);
    }
}
