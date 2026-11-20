using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Director;
using Content.Server.Radio.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Pinpointer;
using Content.Shared.Radio.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Echoes;

/// <summary>
/// Echoes. A crewman who dies near a Monolith calls on their own headset a minute or two later, in their own name, asking for
/// help somewhere near their body. Whoever goes finds the Director has laid a fake body there. Once per corpse, and rarely.
/// </summary>
public sealed class WFRemadeEchoSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFRemadeDirectorSystem _director = default!;

    private const float PlaceRange = 25f;
    private const string FallbackChannel = "Common";
    private const int LineCount = 6;
    private const int PlaceLineCount = 4;

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    /// <summary>Least time between two echoes in the round.</summary>
    private static readonly TimeSpan EchoGap = TimeSpan.FromSeconds(90);

    private TimeSpan _next;
    private TimeSpan _nextEcho;

    /// <summary>Echoes sent this round. Tests read it.</summary>
    public int Sent { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobState);
    }

    private void OnMobState(MobStateChangedEvent args)
    {
        // Only a crewman who died where a stone could see them: the Director keeps their stress.
        if (args.NewMobState != MobState.Dead || !HasComp<WFRemadeStressComponent>(args.Target)
            || HasComp<WFRemadeComponent>(args.Target) || HasComp<WFRemadeEchoComponent>(args.Target))
        {
            return;
        }

        EnsureComp<WFRemadeEchoComponent>(args.Target).At = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(60f, 150f));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + Tick;
        var done = new List<EntityUid>();
        var query = EntityQueryEnumerator<WFRemadeEchoComponent>();
        while (query.MoveNext(out var uid, out var echo))
        {
            // Brought back: nothing to echo.
            if (!_mobState.IsDead(uid) && !HasComp<WFRemadeComponent>(uid))
            {
                done.Add(uid);
                continue;
            }

            if (now < echo.At)
                continue;

            if (now < _nextEcho)
            {
                echo.At = _nextEcho + TimeSpan.FromSeconds(_random.NextFloat(5f, 30f));
                continue;
            }

            done.Add(uid);
            if (TryEcho(uid))
                _nextEcho = now + EchoGap;
        }

        foreach (var uid in done)
        {
            RemComp<WFRemadeEchoComponent>(uid);
        }
    }

    /// <summary>Calls on the dead crewman's headset in their name and lays a trap by the body. False if they had no headset.</summary>
    public bool TryEcho(EntityUid corpse)
    {
        if (!_inventory.TryGetSlotEntity(corpse, "ears", out var headset)
            || !TryComp<EncryptionKeyHolderComponent>(headset, out var keys))
        {
            return false;
        }

        var channel = keys.DefaultChannel ?? FallbackChannel;
        if (!keys.Channels.Contains(channel))
            channel = FallbackChannel;

        var line = PlaceNear(corpse) is { } place
            ? Loc.GetString($"wf-remade-echo-place-{_random.Next(1, PlaceLineCount + 1)}", ("place", place))
            : Loc.GetString($"wf-remade-echo-{_random.Next(1, LineCount + 1)}");

        _radio.SendRadioMessage(corpse, line, channel, headset.Value);
        Sent++;
        _director.TryLureAt(corpse);
        return true;
    }

    /// <summary>The name of the nearest beacon on the body's grid, if one is close.</summary>
    private string? PlaceNear(EntityUid corpse)
    {
        var xform = Transform(corpse);
        var here = _transform.GetWorldPosition(xform);
        string? best = null;
        var bestDistance = PlaceRange * PlaceRange;
        var beacons = EntityQueryEnumerator<NavMapBeaconComponent, TransformComponent>();
        while (beacons.MoveNext(out var beacon, out var beaconXform))
        {
            if (beaconXform.GridUid != xform.GridUid || string.IsNullOrWhiteSpace(beacon.Text))
                continue;

            var distance = (_transform.GetWorldPosition(beaconXform) - here).LengthSquared();
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = beacon.Text;
            }
        }

        return best;
    }
}

/// <summary>A crewman who died near a stone, and when their headset will speak for them.</summary>
[RegisterComponent]
public sealed partial class WFRemadeEchoComponent : Component
{
    [ViewVariables]
    public TimeSpan At;
}
