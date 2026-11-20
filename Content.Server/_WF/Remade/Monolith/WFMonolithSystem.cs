using System.Linq;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Counterplay;
using Content.Server._WF.Remade.Dread;
using Content.Server._WF.Remade.Sector;
using Content.Server.Cargo.Components;
using Content.Server.Chat.Systems;
using Content.Server.Power.Components;
using Content.Shared._WF.Remade;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Tiles;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Monolith;

/// <summary>Runs every Monolith: climbs its resonance, pays its power gift and moves it through its stages.</summary>
public sealed partial class WFMonolithSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private WFDreadAlarmSystem _alarm = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedPointLightSystem _light = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCorruptionSystem _corruption = default!;
    [Dependency] private WFRemadeClearSystem _clear = default!;

    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CorpseCountRate = TimeSpan.FromSeconds(5);

    /// <summary>Least time between two stage stings, whichever stones climb.</summary>
    private static readonly TimeSpan StingGap = TimeSpan.FromSeconds(45);

    /// <summary>No stage sting plays before this. Tests clear it.</summary>
    public TimeSpan NextSting { get; set; }

    private readonly HashSet<Entity<HumanoidAppearanceComponent>> _humanoids = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFMonolithComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFMonolithComponent, EntityTerminatingEvent>(OnTerminating);
    }

    private void OnMapInit(Entity<WFMonolithComponent> ent, ref MapInitEvent args)
    {
        SetStage(ent, WFMonolithMath.StageFor(ent.Comp.Resonance));
    }

    private void OnTerminating(Entity<WFMonolithComponent> ent, ref EntityTerminatingEvent args)
    {
        _corruption.StartDecay(ent.Comp);

        // Chipped shards go with their Scion, wherever they were carried.
        foreach (var shard in ent.Comp.Shards.ToArray())
        {
            if (!TerminatingOrDeleted(shard))
                QueueDel(shard);
        }

        ent.Comp.Shards.Clear();
        _clear.LastStoneGone(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFMonolithComponent>();
        while (query.MoveNext(out var uid, out var monolith))
        {
            if (now < monolith.NextTick)
                continue;

            monolith.NextTick = now + TickRate;
            Tick((uid, monolith), now);
        }
    }

    private void Tick(Entity<WFMonolithComponent> ent, TimeSpan now)
    {
        var monolith = ent.Comp;
        if (now >= monolith.NextCorpseCount)
        {
            monolith.NextCorpseCount = now + CorpseCountRate;
            monolith.Corpses = CountCorpses(ent);
        }

        if (IsBarred(ent))
        {
            monolith.Draw = 0f;
            monolith.MatterDraw = 0f;
            monolith.FleshDraw = 0f;
            monolith.ReachDraw = 0f;
            monolith.WardDraw = 0f;
        }

        var rate = WFMonolithMath.Rate(EffectiveDraw(ent), monolith.Resonance, monolith.Corpses) + monolith.SelfClimb;
        monolith.Resonance = Math.Min(monolith.Resonance + rate * (float) TickRate.TotalSeconds, WFMonolithMath.Cap(monolith.MaxStage));

        if (TryComp<PowerSupplierComponent>(ent, out var supplier))
            supplier.MaxSupply = WFMonolithMath.Output(monolith.Draw, monolith.Resonance);

        var stage = WFMonolithMath.StageFor(monolith.Resonance);
        if (stage != monolith.Stage)
            SetStage(ent, stage);
    }

    private int CountCorpses(Entity<WFMonolithComponent> ent)
    {
        _humanoids.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.CorpseRadius, _humanoids);

        var count = 0;
        foreach (var humanoid in _humanoids)
        {
            if (_mobState.IsDead(humanoid))
                count++;
        }

        return count;
    }

    /// <summary>A Monolith can't be drawn on while it sits on or near a main station.</summary>
    public bool IsBarred(EntityUid monolith)
    {
        return _corruption.IsSanctuaryAt(monolith);
    }

    /// <summary>Sets the Power dial.</summary>
    public void SetDraw(Entity<WFMonolithComponent> ent, float draw)
    {
        SetDial(ent, WFGift.Power, draw);
    }

    /// <summary>
    /// Sets a Gift's dial. A sanctuary grid pins every dial to nothing, and a rooted Monolith holds Matter, Flesh and Reach at full.
    /// </summary>
    public void SetDial(Entity<WFMonolithComponent> ent, WFGift gift, float value)
    {
        var monolith = ent.Comp;
        if (IsBarred(ent))
            value = 0f;
        else if (gift != WFGift.Power && monolith.Stage == WFMonolithStage.Crescendo)
            return;

        value = Math.Clamp(value, 0f, 1f);
        switch (gift)
        {
            case WFGift.Power:
                monolith.Draw = value;
                if (TryComp<PowerSupplierComponent>(ent, out var supplier))
                    supplier.MaxSupply = WFMonolithMath.Output(value, monolith.Resonance);
                break;
            case WFGift.Matter:
                monolith.MatterDraw = value;
                break;
            case WFGift.Flesh:
                monolith.FleshDraw = value;
                break;
            case WFGift.Reach:
                monolith.ReachDraw = value;
                break;
            case WFGift.Ward:
                monolith.WardDraw = value;
                break;
        }
    }

    /// <summary>The dial a Gift runs at now: nothing on a sanctuary grid, full once the Monolith has rooted.</summary>
    public float GetDial(Entity<WFMonolithComponent> ent, WFGift gift)
    {
        if (IsBarred(ent))
            return 0f;

        var monolith = ent.Comp;
        var set = gift switch
        {
            WFGift.Matter => monolith.MatterDraw,
            WFGift.Flesh => monolith.FleshDraw,
            WFGift.Reach => monolith.ReachDraw,
            WFGift.Ward => monolith.WardDraw,
            _ => monolith.Draw,
        };

        return gift == WFGift.Power ? Math.Clamp(set, 0f, 1f) : WFMonolithMath.Dial(monolith.Stage, set);
    }

    /// <summary>All the dials added up, which is what drives the climb.</summary>
    public float EffectiveDraw(Entity<WFMonolithComponent> ent)
    {
        return WFMonolithMath.EffectiveDraw(
            GetDial(ent, WFGift.Power),
            GetDial(ent, WFGift.Matter),
            GetDial(ent, WFGift.Flesh),
            GetDial(ent, WFGift.Reach),
            GetDial(ent, WFGift.Ward));
    }

    /// <summary>Adds resonance outside the climb, such as a chipped shard or a jump.</summary>
    public void AddResonance(Entity<WFMonolithComponent> ent, float amount)
    {
        ent.Comp.Resonance = Math.Clamp(ent.Comp.Resonance + amount, 0f, WFMonolithMath.Cap(ent.Comp.MaxStage));
        var stage = WFMonolithMath.StageFor(ent.Comp.Resonance);
        if (stage != ent.Comp.Stage)
            SetStage(ent, stage);
    }

    /// <summary>Takes a fed corpse's relief off the resonance. Nothing quiets a Monolith past its Crescendo.</summary>
    public bool TryQuiet(Entity<WFMonolithComponent> ent)
    {
        if (ent.Comp.Stage == WFMonolithStage.Crescendo)
            return false;

        ent.Comp.Resonance = Math.Max(0f, ent.Comp.Resonance - ent.Comp.FeedRelief);
        var stage = WFMonolithMath.StageFor(ent.Comp.Resonance);
        if (stage != ent.Comp.Stage)
            SetStage(ent, stage);

        return true;
    }

    private void SetStage(Entity<WFMonolithComponent> ent, WFMonolithStage stage)
    {
        var old = ent.Comp.Stage;
        ent.Comp.Stage = stage;
        _appearance.SetData(ent, WFMonolithVisuals.Stage, stage);

        // Only a quiet Scion is a thing a buyer will touch.
        if (stage == WFMonolithStage.Quiet)
            RemComp<CargoSellBlacklistComponent>(ent);
        else
            EnsureComp<CargoSellBlacklistComponent>(ent);

        if (_light.TryGetLight(ent, out var light))
        {
            _light.SetEnabled(ent, stage != WFMonolithStage.Quiet, light);
            _light.SetRadius(ent, 2f + 2f * (int) stage, light);
        }

        if (stage == WFMonolithStage.Crescendo && old != WFMonolithStage.Crescendo)
            Crescendo(ent);
        else if (stage > old)
            Sting(ent, stage);
    }

    /// <summary>Everyone in the round hears the stone climb, wherever they are.</summary>
    private void Sting(Entity<WFMonolithComponent> ent, WFMonolithStage stage)
    {
        var index = (int) stage - 1;
        var now = _timing.CurTime;
        if (index < 0 || index >= ent.Comp.StageSounds.Count || !ent.Comp.Local && now < NextSting)
            return;

        // A splinter's climb is heard only aboard its own grid.
        var grid = ent.Comp.Local ? Transform(ent).GridUid : null;
        if (!ent.Comp.Local)
            NextSting = now + StingGap;

        _audio.PlayGlobal(ent.Comp.StageSounds[index], Filter.Empty().AddWhere(session =>
            session.AttachedEntity is { } attached && (grid == null || Transform(attached).GridUid == grid)), false);
        StingsPlayed++;
    }

    /// <summary>Stage stings played this round. Tests read it.</summary>
    public int StingsPlayed { get; private set; }

    /// <summary>The Scion roots where it stands, and the sector is told.</summary>
    private void Crescendo(Entity<WFMonolithComponent> ent)
    {
        RemComp<PullableComponent>(ent);

        var xform = Transform(ent);
        if (xform.GridUid != null && !xform.Anchored)
            _transform.AnchorEntity((ent, xform));

        // The alarm sounds three times, back to back, so the announcement leaves the sound to the repeater.
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("wf-monolith-crescendo-announcement"),
            Loc.GetString("wf-monolith-crescendo-sender"),
            playSound: false,
            colorOverride: Color.FromHex("#EC3232"));
        _alarm.Start(ent, ent.Comp.CrescendoSound, global: true);
        _audio.PlayGlobal(ent.Comp.CrescendoSong, Filter.Broadcast(), true);

        RaiseLocalEvent(ent, new WFMonolithRootedEvent());
    }
}
