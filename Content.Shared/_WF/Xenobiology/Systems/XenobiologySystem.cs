using Content.Shared._WF.Xenobiology.Components;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Jittering;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Xenobiology.Systems;

/// <summary>
/// Slime breeding, mutation and taming.
/// </summary>
public sealed partial class XenobiologySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private HungerSystem _hunger = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private MobGrowthSystem _growth = default!;
    [Dependency] private MobStateSystem _mob = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public const string StomachContainerId = "slimeStomach";

    /// <summary>
    /// Hunger a fresh slime starts with: hungry enough to hunt, not enough to grow.
    /// </summary>
    public const float StartingHunger = 40f;

    private readonly List<Entity<SlimeComponent>> _splitting = new();
    private readonly List<Entity<PendingSlimeSpawnComponent>> _pending = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SlimeComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<SlimeComponent, MapInitEvent>(OnMapInit, after: new[] { typeof(HungerSystem) });
        SubscribeLocalEvent<SlimeComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<SlimeComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<PendingSlimeSpawnComponent, MapInitEvent>(OnPendingMapInit, after: new[] { typeof(HungerSystem) });
    }

    private void OnInit(Entity<SlimeComponent> ent, ref ComponentInit args)
    {
        ent.Comp.Stomach = _container.EnsureContainer<Container>(ent, StomachContainerId);
    }

    private void OnMapInit(Entity<SlimeComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextUpdateTime = _timing.CurTime + ent.Comp.UpdateInterval;
        if (TryComp<HungerComponent>(ent, out var hunger))
            _hunger.SetHunger(ent, StartingHunger, hunger);
    }

    private void OnExamined(Entity<SlimeComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (ent.Comp.Tamer == args.Examiner)
            args.PushMarkup(Loc.GetString("slime-examined-tamer"));

        if (ent.Comp.Stomach.ContainedEntities.Count > 0)
            args.PushMarkup(Loc.GetString("slime-examined-stomach"));
    }

    private void OnInteractHand(Entity<SlimeComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled || _mob.IsDead(ent))
            return;

        var user = args.User;
        if (ent.Comp.Tamer != null)
        {
            _popup.PopupClient(Loc.GetString("slime-interaction-tame-fail"), user, user);
            return;
        }

        ent.Comp.Tamer = user;
        Dirty(ent);
        PredictedSpawnAtPosition(ent.Comp.TameEffect, Transform(ent).Coordinates);
        _popup.PopupClient(Loc.GetString("slime-interaction-tame"), user, user);
    }

    private void OnPendingMapInit(Entity<PendingSlimeSpawnComponent> ent, ref MapInitEvent args)
    {
        // Wait a tick: a marker filled into a crate starts up before it is inserted, and the slime should land inside.
        if (_net.IsServer)
            _pending.Add(ent);
    }

    /// <summary>
    /// Replaces a pending marker with its slime, inside whatever container holds the marker.
    /// </summary>
    public void SpawnPending(Entity<PendingSlimeSpawnComponent> ent)
    {
        if (TerminatingOrDeleted(ent) || EntityManager.IsQueuedForDeletion(ent))
            return;

        if (SpawnSlime(ent, ent.Comp.BasePrototype, ent.Comp.Breed) is { } slime)
        {
            // Every spawned slime gets its own spread of stats; offspring inherit them.
            var s = slime.Comp;
            s.MutationChance *= _random.NextFloat(0.5f, 1.5f);
            s.MaxOffspring += _random.Next(-1, 2);
            s.ExtractsProduced += _random.Next(0, 2);
            s.MitosisHunger *= _random.NextFloat(0.75f, 1.2f);
            Dirty(slime);
        }

        QueueDel(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_net.IsClient)
            return;

        foreach (var pending in _pending)
        {
            SpawnPending(pending);
        }

        _pending.Clear();

        var now = _timing.CurTime;
        _splitting.Clear();
        var query = EntityQueryEnumerator<SlimeComponent, MobGrowthComponent, HungerComponent>();
        while (query.MoveNext(out var uid, out var slime, out var growth, out var hunger))
        {
            if (now < slime.NextUpdateTime)
                continue;

            slime.NextUpdateTime = now + slime.UpdateInterval;
            if (_mob.IsDead(uid) || growth.IsFirstStage)
                continue;

            var value = _hunger.GetHunger(hunger);
            if (value > slime.MitosisHunger - slime.JitterDifference)
                _jitter.DoJitter(uid, TimeSpan.FromSeconds(1), true);

            if (value >= slime.MitosisHunger)
                _splitting.Add((uid, slime));
        }

        foreach (var ent in _splitting)
        {
            DoMitosis(ent);
        }
    }

    /// <summary>
    /// Splits a slime into offspring, each of which may mutate into one of its potential breeds.
    /// </summary>
    public void DoMitosis(Entity<SlimeComponent> ent)
    {
        var offspring = _random.Next(1, Math.Max(1, ent.Comp.MaxOffspring) + 1);
        _audio.PlayPvs(ent.Comp.MitosisSound, ent);

        for (var i = 0; i < offspring; i++)
        {
            var breed = ent.Comp.Breed;
            if (ent.Comp.PotentialMutations.Count > 0 && _random.Prob(Math.Clamp(ent.Comp.MutationChance, 0f, 1f)))
                breed = _random.Pick(ent.Comp.PotentialMutations);

            if (SpawnSlime(ent, ent.Comp.DefaultSlimeProto, breed) is not { } child)
                continue;

            child.Comp.Tamer = ent.Comp.Tamer;
            child.Comp.MutationChance = ent.Comp.MutationChance;
            child.Comp.MaxOffspring = ent.Comp.MaxOffspring;
            child.Comp.ExtractsProduced = ent.Comp.ExtractsProduced;
            Dirty(child);
        }

        _container.EmptyContainer(ent.Comp.Stomach);
        QueueDel(ent);
    }

    /// <summary>
    /// Spawns <paramref name="proto"/> next to <paramref name="parent"/> as a slime of <paramref name="breedId"/>.
    /// </summary>
    public Entity<SlimeComponent>? SpawnSlime(EntityUid parent, EntProtoId proto, ProtoId<BreedPrototype> breedId)
    {
        if (TerminatingOrDeleted(parent) || !ProtoMan.TryIndex(breedId, out var breed))
            return null;

        var uid = SpawnNextToOrDrop(proto, parent, null, breed.Components);
        if (!TryComp<SlimeComponent>(uid, out var slime))
            return null;

        if (slime.ShouldHaveShader && slime.Shader != null)
            _appearance.SetData(uid, XenoSlimeVisuals.Shader, slime.Shader);
        _appearance.SetData(uid, XenoSlimeVisuals.Color, slime.SlimeColor);
        _meta.SetEntityName(uid, breed.BreedName);

        if (TryComp<MobGrowthComponent>(uid, out var growth))
        {
            growth.BaseEntityName = breed.BreedName;
            _growth.UpdateAppearance((uid, growth));
        }

        return (uid, slime);
    }

    /// <summary>
    /// The extract a slime's breed makes when ground.
    /// </summary>
    public EntProtoId GetProducedExtract(Entity<SlimeComponent> slime)
    {
        return ProtoMan.TryIndex(slime.Comp.Breed, out var breed) ? breed.ProducedExtract : slime.Comp.DefaultExtract;
    }
}
