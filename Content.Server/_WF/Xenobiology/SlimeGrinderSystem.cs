using Content.Server.Power.Components;
using Content.Shared._WF.Xenobiology.Components;
using Content.Shared._WF.Xenobiology.Systems;
using Content.Shared.Audio;
using Content.Shared.Climbing.Events;
using Content.Shared.Construction.Components;
using Content.Shared.Jittering;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Power;
using Content.Shared.Throwing;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Xenobiology;

/// <summary>
/// A machine that grinds dead slimes into their extracts.
/// </summary>
[RegisterComponent]
public sealed partial class SlimeGrinderComponent : Component
{
    [ViewVariables]
    public float ProcessingTimer;

    [ViewVariables]
    public Dictionary<EntProtoId, int> YieldQueue = new();

    [DataField]
    public float ProcessingTimePerUnitMass = 0.1f;

    [DataField]
    public SoundSpecifier GrindSound = new SoundPathSpecifier("/Audio/Machines/reclaimer_startup.ogg");
}

/// <summary>
/// A slime grinder that is grinding.
/// </summary>
[RegisterComponent]
public sealed partial class ActiveSlimeGrinderComponent : Component;

/// <summary>
/// Takes dead slimes dropped or climbed onto a powered grinder and spits out their extracts.
/// </summary>
public sealed partial class SlimeGrinderSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private XenobiologySystem _xenobio = default!;

    private readonly List<EntProtoId> _done = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ActiveSlimeGrinderComponent, ComponentInit>(OnActiveInit);
        SubscribeLocalEvent<ActiveSlimeGrinderComponent, ComponentRemove>(OnActiveRemove);
        SubscribeLocalEvent<ActiveSlimeGrinderComponent, UnanchorAttemptEvent>(OnUnanchorAttempt);
        SubscribeLocalEvent<SlimeGrinderComponent, ClimbedOnEvent>(OnClimbedOn);
        SubscribeLocalEvent<SlimeGrinderComponent, PowerChangedEvent>(OnPowerChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ActiveSlimeGrinderComponent, SlimeGrinderComponent>();
        while (query.MoveNext(out var uid, out _, out var grinder))
        {
            grinder.ProcessingTimer = MathF.Max(0f, grinder.ProcessingTimer - frameTime);
            if (grinder.ProcessingTimer > 0)
                continue;

            _done.Clear();
            foreach (var (extract, count) in grinder.YieldQueue)
            {
                for (var i = 0; i < count; i++)
                {
                    SpawnNextToOrDrop(extract, uid);
                }

                _done.Add(extract);
            }

            foreach (var extract in _done)
            {
                grinder.YieldQueue.Remove(extract);
            }

            RemCompDeferred<ActiveSlimeGrinderComponent>(uid);
        }
    }

    private void OnActiveInit(Entity<ActiveSlimeGrinderComponent> ent, ref ComponentInit args)
    {
        if (!TryComp<SlimeGrinderComponent>(ent, out var grinder))
            return;

        _jitter.AddJitter(ent, -10, 100);
        _audio.PlayPvs(grinder.GrindSound, ent);
        _ambient.SetAmbience(ent, true);
    }

    private void OnActiveRemove(Entity<ActiveSlimeGrinderComponent> ent, ref ComponentRemove args)
    {
        RemComp<JitteringComponent>(ent);
        _ambient.SetAmbience(ent, false);
    }

    private void OnUnanchorAttempt(Entity<ActiveSlimeGrinderComponent> ent, ref UnanchorAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnPowerChanged(Entity<SlimeGrinderComponent> ent, ref PowerChangedEvent args)
    {
        if (args.Powered && ent.Comp.ProcessingTimer > 0)
            EnsureComp<ActiveSlimeGrinderComponent>(ent);
        else
            RemCompDeferred<ActiveSlimeGrinderComponent>(ent);
    }

    private void OnClimbedOn(Entity<SlimeGrinderComponent> ent, ref ClimbedOnEvent args)
    {
        if (CanGrind(ent, args.Climber))
            QueueProcess(args.Climber, ent);
    }

    private void QueueProcess(EntityUid uid, Entity<SlimeGrinderComponent> grinder)
    {
        if (!TryComp<PhysicsComponent>(uid, out var physics) || !TryComp<SlimeComponent>(uid, out var slime))
            return;

        EnsureComp<ActiveSlimeGrinderComponent>(grinder);
        grinder.Comp.ProcessingTimer += physics.FixturesMass * grinder.Comp.ProcessingTimePerUnitMass;

        var extract = _xenobio.GetProducedExtract((uid, slime));
        grinder.Comp.YieldQueue[extract] = grinder.Comp.YieldQueue.GetValueOrDefault(extract) + slime.ExtractsProduced;

        // Whatever the slime was digesting comes back out.
        foreach (var eaten in _container.EmptyContainer(slime.Stomach))
        {
            _throwing.TryThrow(eaten, _random.NextVector2() * 5);
        }

        QueueDel(uid);
    }

    private bool CanGrind(Entity<SlimeGrinderComponent> grinder, EntityUid target)
    {
        if (!Transform(grinder).Anchored
            || !HasComp<SlimeComponent>(target)
            || TryComp<MobStateComponent>(target, out var mobState) && mobState.CurrentState != MobState.Dead)
            return false;

        return !TryComp<ApcPowerReceiverComponent>(grinder, out var power) || power.Powered;
    }
}
