using Content.Server.Popups;
using Content.Server.Stack;
using Content.Shared._WF.Bonfires;
using Content.Shared.Audio;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.Temperature;
using Content.Shared.Temperature.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Bonfires;

/// <summary>
/// Burns a fire's fuel down a second at a time, puts it out when it's spent, and takes wood, paper and a flame to feed
/// and relight it. A fire that is out gives no warmth (<c>EntityRadiusHeaterSystem.IsActive</c> asks), light or sound.
/// </summary>
public sealed class WFBonfireFuelSystem : EntitySystem
{
    [Dependency] private SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPointLightSystem _light = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>The material wood is counted in.</summary>
    public const string Wood = "Wood";

    private static readonly TimeSpan BurnInterval = TimeSpan.FromSeconds(1);

    private readonly List<EntityUid> _spent = new();
    private TimeSpan _nextBurn;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFBonfireFuelComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WFBonfireFuelComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WFBonfireFuelComponent, ExaminedEvent>(OnExamined);
    }

    /// <summary>A fire that starts out is made dark and cold; a lit one is as its prototype draws it.</summary>
    private void OnStartup(Entity<WFBonfireFuelComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.Infinite)
        {
            ent.Comp.Lit = true;
            return;
        }

        if (!ent.Comp.Lit)
            Apply(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextBurn)
            return;

        _nextBurn = now + BurnInterval;
        _spent.Clear();
        var query = EntityQueryEnumerator<WFBonfireFuelComponent>();
        while (query.MoveNext(out var uid, out var fuel))
        {
            if (!fuel.Lit || fuel.Infinite)
                continue;

            fuel.Fuel = MathF.Max(0f, fuel.Fuel - (float) BurnInterval.TotalSeconds);
            if (fuel.Fuel <= 0f)
                _spent.Add(uid);
        }

        foreach (var uid in _spent)
        {
            SetLit((uid, Comp<WFBonfireFuelComponent>(uid)), false);
        }
    }

    /// <summary>Seconds of burning an item gives, from its wood and its tags; 0 if it won't burn.</summary>
    public float FuelOf(Entity<WFBonfireFuelComponent> fire, EntityUid item)
    {
        var seconds = 0f;
        if (TryComp<PhysicalCompositionComponent>(item, out var composition)
            && composition.MaterialComposition.TryGetValue(Wood, out var wood))
            seconds += wood * fire.Comp.WoodSeconds;

        foreach (var (tag, value) in fire.Comp.TagSeconds)
        {
            if (_tag.HasTag(item, tag))
                seconds = MathF.Max(seconds, value);
        }

        return seconds;
    }

    /// <summary>Whether an item is burning or glowing, so it lights a fire.</summary>
    private bool IsHot(EntityUid item)
    {
        var ev = new IsHotEvent();
        RaiseLocalEvent(item, ev);
        return ev.IsHot;
    }

    private void OnInteractUsing(Entity<WFBonfireFuelComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.Infinite)
            return;

        var seconds = FuelOf(ent, args.Used);
        if (seconds > 0f)
        {
            args.Handled = true;
            if (ent.Comp.Fuel + seconds > ent.Comp.MaxFuel)
            {
                _popup.PopupEntity(Loc.GetString("wf-bonfire-full", ("fire", ent.Owner)), ent, args.User);
                return;
            }

            AddFuel(ent, args.Used, seconds);
            _popup.PopupEntity(Loc.GetString("wf-bonfire-fed", ("fire", ent.Owner)), ent, args.User);
            return;
        }

        if (ent.Comp.Lit || !IsHot(args.Used))
            return;

        args.Handled = true;
        if (ent.Comp.Fuel <= 0f)
        {
            _popup.PopupEntity(Loc.GetString("wf-bonfire-nothing", ("fire", ent.Owner)), ent, args.User);
            return;
        }

        SetLit(ent, true);
        _popup.PopupEntity(Loc.GetString("wf-bonfire-lit", ("fire", ent.Owner)), ent, args.User);
    }

    /// <summary>Puts one unit of an item on a fire and takes its seconds of fuel.</summary>
    public void AddFuel(Entity<WFBonfireFuelComponent> fire, EntityUid item, float seconds)
    {
        fire.Comp.Fuel = MathF.Min(fire.Comp.MaxFuel, fire.Comp.Fuel + seconds);
        if (TryComp<StackComponent>(item, out var stack) && stack.Count > 1)
            _stack.Use(item, 1, stack);
        else
            QueueDel(item);

        _audio.PlayPvs(fire.Comp.FuelSound, fire);
    }

    /// <summary>Lights or puts out a fire; a fire with nothing to burn can't be lit.</summary>
    public void SetLit(Entity<WFBonfireFuelComponent> fire, bool lit)
    {
        if (lit && fire.Comp.Fuel <= 0f && !fire.Comp.Infinite)
            return;

        if (fire.Comp.Lit == lit)
            return;

        fire.Comp.Lit = lit;
        if (lit)
            _audio.PlayPvs(fire.Comp.LightSound, fire);

        Apply(fire);
    }

    /// <summary>Sets the fire's light, crackle, hotness and sprite to whether it is lit.</summary>
    private void Apply(Entity<WFBonfireFuelComponent> fire)
    {
        var lit = fire.Comp.Lit;
        _light.SetEnabled(fire, lit);
        _ambient.SetAmbience(fire, lit);
        _appearance.SetData(fire, WFBonfireVisuals.Lit, lit);
        if (lit)
            EnsureComp<AlwaysHotComponent>(fire);
        else
            RemComp<AlwaysHotComponent>(fire);
    }

    private void OnExamined(Entity<WFBonfireFuelComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || ent.Comp.Infinite)
            return;

        var comp = ent.Comp;
        string id;
        if (comp.Lit)
            id = comp.Fuel < comp.MaxFuel * comp.LowShare ? "wf-bonfire-examine-low" : "wf-bonfire-examine-burning";
        else
            id = comp.Fuel > 0f ? "wf-bonfire-examine-out-fuel" : "wf-bonfire-examine-out";

        args.PushMarkup(Loc.GetString(id));
    }
}
