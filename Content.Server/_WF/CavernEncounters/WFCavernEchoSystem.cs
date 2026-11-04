using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.Speech;
using Content.Shared.Chat;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.CavernEncounters;

/// <summary>
/// The Echo keeps what people say near it and, while someone is in earshot but not close, says one of those lines back
/// in the speaker's name. Whoever comes within its lunge range becomes its prey.
/// </summary>
public sealed partial class WFCavernEchoSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);

    private TimeSpan _nextCheck;
    private readonly HashSet<Entity<HumanoidAppearanceComponent>> _people = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFCavernEchoComponent, ListenEvent>(OnListen);
    }

    private void OnListen(Entity<WFCavernEchoComponent> ent, ref ListenEvent args)
    {
        if (args.Source == ent.Owner || !HasComp<HumanoidAppearanceComponent>(args.Source) || string.IsNullOrWhiteSpace(args.Message))
            return;

        var name = Identity.Name(args.Source, EntityManager);
        var message = args.Message;
        ent.Comp.Lines.RemoveAll(l => l.Line == message);
        ent.Comp.Lines.Add((name, message));

        while (ent.Comp.Lines.Count > ent.Comp.MaxLines)
        {
            ent.Comp.Lines.RemoveAt(0);
        }
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextCheck)
            return;

        _nextCheck = now + CheckInterval;

        var query = EntityQueryEnumerator<WFCavernEchoComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var echo, out var xform))
        {
            if (!_mobState.IsAlive(uid))
                continue;

            _people.Clear();
            _lookup.GetEntitiesInRange(xform.Coordinates, echo.SpeakRange, _people);

            var anyone = false;
            var close = false;
            foreach (var person in _people)
            {
                if (!_mobState.IsAlive(person))
                    continue;

                anyone = true;
                if (!xform.Coordinates.TryDistance(EntityManager, Transform(person).Coordinates, out var distance) || distance > echo.LungeRange)
                    continue;

                close = true;
                if (echo.Prey.Add(person))
                {
                    _faction.AggroEntity(uid, person.Owner);
                    _audio.PlayPvs(echo.LungeSound, uid);
                }
            }

            if (anyone && !close && now >= echo.NextLine)
                TryRepeat((uid, echo));
        }
    }

    /// <summary>Says back one line it heard, in its speaker's name; false when it has heard nothing.</summary>
    public bool TryRepeat(Entity<WFCavernEchoComponent> ent)
    {
        var now = _timing.CurTime;
        var wait = _random.Next((int) ent.Comp.MinWait.TotalSeconds, (int) ent.Comp.MaxWait.TotalSeconds + 1);
        ent.Comp.NextLine = now + TimeSpan.FromSeconds(wait);

        if (ent.Comp.Lines.Count == 0)
            return false;

        // Prefer a line it hasn't just said.
        var choices = ent.Comp.Lines.Where(l => l.Line != ent.Comp.LastSpoken).ToList();
        var (name, line) = _random.Pick(choices.Count > 0 ? choices : ent.Comp.Lines);

        ent.Comp.LastSpoken = line;
        _chat.TrySendInGameICMessage(ent, line, InGameICChatType.Speak, hideChat: false, hideLog: true,
            nameOverride: name, checkRadioPrefix: false, ignoreActionBlocker: true);
        return true;
    }
}
