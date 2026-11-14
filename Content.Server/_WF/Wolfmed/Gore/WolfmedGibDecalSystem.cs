using Content.Server._WF.Wolfmed.Wounds;
using Content.Server.Decals;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Sounds;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Server.Body.Components;
using Content.Shared.Decals;
using Content.Shared.Humanoid;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Wolfmed.Gore;

/// <summary>
/// GORE: the gibs. An extreme trauma (a limb off, a belly opened, a body gibbed) leaves Escape From Nevado's gib
/// art on the deck around the body as decals: the blood layer in the body's blood colour, the skin-coloured bits in
/// its skin colour, the innards in their own colours. Cleanable like the floor splats. Organic bodies only.
/// </summary>
public sealed class WolfmedGibDecalSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private WolfmedGoreSystem _gore = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private WolfmedOrganicSoundSystem _organic = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Every gib decal id starts with this; the layer suffixes are _meat and _flesh.</summary>
    public const string Prefix = "WFWolfmedGib_";

    /// <summary>Skin for a body that has no humanoid appearance to read it from.</summary>
    private static readonly Color DefaultSkin = Color.FromHex("#C0967F");

    /// <summary>A limb off: the splats and the streaks, no innards.</summary>
    public static readonly string[] Dismemberment = { "gibmid1", "gib1", "gib3", "gib4", "gib5" };

    /// <summary>A belly opened: the ones with flesh and guts in them.</summary>
    public static readonly string[] Evisceration = { "gib2", "gib6", "gibmid1", "gib4" };

    /// <summary>The whole body: everything.</summary>
    public static readonly string[] Gib = { "gibmid1", "gib1", "gib2", "gib3", "gib4", "gib5", "gib6" };

    /// <summary>The states that come in four directions (one is picked at random).</summary>
    private static readonly HashSet<string> Directional = new() { "gib3", "gib4", "gib5", "gib6" };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedPartAmputatedEvent>(OnAmputated);
        SubscribeLocalEvent<WolfmedTorsoOverflowEvent>(OnTorsoOverflow, after: new[] { typeof(WolfmedEviscerationSystem) });
        SubscribeLocalEvent<WoundHostComponent, BeingGibbedEvent>(OnGibbed);
    }

    private void OnAmputated(ref WolfmedPartAmputatedEvent args)
    {
        Throw(args.Body, Dismemberment, _cfg.GetCVar(WolfmedCVars.GibsDismemberment));
    }

    private void OnTorsoOverflow(ref WolfmedTorsoOverflowEvent args)
    {
        // Only when the evisceration system, which runs first, has the belly open.
        if (HasComp<WolfmedEviscerationComponent>(args.Part))
            Throw(args.Body, Evisceration, _cfg.GetCVar(WolfmedCVars.GibsEvisceration));
    }

    private void OnGibbed(Entity<WoundHostComponent> body, ref BeingGibbedEvent args)
    {
        Throw(body, Gib, _cfg.GetCVar(WolfmedCVars.GibsGib));
    }

    /// <summary>
    /// Puts <paramref name="count"/> gibs from <paramref name="pool"/> on the deck around the body, each within
    /// wolfmed.gib_spread tiles, or on the body's own tile when the roll lands off the grid. Returns how many landed.
    /// </summary>
    public int Throw(EntityUid body, string[] pool, int count)
    {
        if (count <= 0 || !_cfg.GetCVar(WolfmedCVars.GibDecals) || TerminatingOrDeleted(body) ||
            !_organic.IsOrganicBody(body) || _gore.GetBloodColor(body) is not { } blood)
            return 0;

        var xform = Transform(body);
        if (xform.GridUid is not { } grid || !TryComp(grid, out MapGridComponent? gridComp))
            return 0;

        var skin = TryComp(body, out HumanoidAppearanceComponent? humanoid) ? humanoid.SkinColor : DefaultSkin;
        var origin = _transform.WithEntityId(_transform.GetMoverCoordinates(body, xform), grid);
        var spread = MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.GibSpread));
        var placed = 0;
        for (var i = 0; i < count; i++)
        {
            var position = origin.Position + _random.NextAngle().ToVec() * _random.NextFloat(0f, spread);
            var coordinates = new EntityCoordinates(grid, position);
            // Off the grid's edge (a shuttle, a tiny test grid): the gib lands where the body is instead.
            if (!_map.TryGetTileRef(grid, gridComp, coordinates, out var tile) || tile.Tile.IsEmpty)
                coordinates = origin;

            var name = _random.Pick(pool);
            if (Directional.Contains(name))
                name = $"{name}_{_random.Next(4)}";

            var rotation = _random.NextAngle();
            // The blood under everything, the innards over it, the skin-coloured bits on top.
            if (Add(Prefix + name, coordinates, blood, rotation, 0))
                placed++;
            Add($"{Prefix}{name}_meat", coordinates, null, rotation, 1);
            Add($"{Prefix}{name}_flesh", coordinates, skin, rotation, 2);
        }

        return placed;
    }

    private bool Add(string id, EntityCoordinates coordinates, Color? color, Angle rotation, int zIndex)
    {
        return _prototypes.HasIndex<DecalPrototype>(id) &&
               _decals.TryAddDecal(id, coordinates, out _, color, rotation, zIndex, cleanable: true);
    }
}
