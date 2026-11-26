using System.Numerics;
using Content.Shared._WF.AsteroidMining;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Timing;

namespace Content.Client._WF.AsteroidMining;

/// <summary>
/// Picks each rock's sprite variant and vein tint, makes its cracks glow with heat under a beam, shakes it as it nears
/// cracking, and registers the overlays that draw it breaking and draw it from its grid while it is out of view range.
/// </summary>
public sealed partial class WFAsteroidVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedPointLightSystem _lights = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IResourceCache _resources = default!;

    private static readonly Color Ember = new(0.55f, 0.04f, 0f);
    private static readonly Color Orange = new(1f, 0.42f, 0.05f);
    private static readonly Color WhiteHot = new(1f, 0.92f, 0.7f);
    private static readonly WFAsteroidVisualLayers[] Layers =
        { WFAsteroidVisualLayers.Rock, WFAsteroidVisualLayers.Veins, WFAsteroidVisualLayers.Heat };

    /// <summary>Rocks with heat to show or still cooling off, the only ones the frame update touches.</summary>
    private readonly HashSet<EntityUid> _hot = new();
    private readonly List<EntityUid> _cooled = new();

    /// <summary>The RSI each rock prototype and look draws from out of view range; null if it has none.</summary>
    private readonly Dictionary<(EntProtoId Rock, string? Look), RSI?> _farRsis = new();
    private readonly HashSet<(EntProtoId Rock, string? Look)> _pendingRsis = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFAsteroidComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WFAsteroidComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WFAsteroidGridVisualsComponent, AfterAutoHandleStateEvent>(OnGridState);
        _overlays.AddOverlay(new WFAsteroidCrackOverlay(EntityManager, _prototypes, _timing));
        _overlays.AddOverlay(new WFAsteroidFarOverlay(EntityManager, this));
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<WFAsteroidCrackOverlay>();
        _overlays.RemoveOverlay<WFAsteroidFarOverlay>();
        base.Shutdown();
    }

    private void OnStartup(Entity<WFAsteroidComponent> ent, ref ComponentStartup args)
    {
        Apply(ent);
    }

    private void OnState(Entity<WFAsteroidComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        Apply(ent);
    }

    private void OnGridState(Entity<WFAsteroidGridVisualsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (ent.Comp.Rock is { } rock && !_farRsis.ContainsKey((rock, ent.Comp.Look)))
            _pendingRsis.Add((rock, ent.Comp.Look));
    }

    /// <summary>The RSI to draw a grid's rock from while the rock is out of view range, once it has been loaded.</summary>
    public RSI? FarRsi(WFAsteroidGridVisualsComponent visuals)
    {
        return visuals.Rock is { } rock && _farRsis.TryGetValue((rock, visuals.Look), out var rsi) ? rsi : null;
    }

    private void Apply(Entity<WFAsteroidComponent> ent)
    {
        if (ent.Comp.Charge > 0f)
            _hot.Add(ent);

        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        // A look swaps every layer to an RSI with the same states and silhouettes.
        var swapped = false;
        if (ent.Comp.Look != ent.Comp.AppliedLook && ent.Comp.Look is { } look && ent.Comp.Looks.TryGetValue(look, out var swap))
        {
            ent.Comp.AppliedLook = look;
            swapped = true;
            foreach (var layer in Layers)
            {
                _sprite.LayerSetRsi((ent, sprite), layer, swap);
            }
        }

        // States arrive many times a second while a beam heats the rock; only a new variant or look changes the states.
        var variant = ent.Comp.Variant;
        if (swapped || variant != ent.Comp.AppliedVariant)
        {
            ent.Comp.AppliedVariant = variant;
            _sprite.LayerSetRsiState((ent, sprite), WFAsteroidVisualLayers.Rock, $"rock{variant}");
            _sprite.LayerSetRsiState((ent, sprite), WFAsteroidVisualLayers.Veins, $"veins{variant}");
            _sprite.LayerSetRsiState((ent, sprite), WFAsteroidVisualLayers.Heat, $"heat{variant}");
        }

        _sprite.LayerSetColor((ent, sprite), WFAsteroidVisualLayers.Rock, ent.Comp.RockColor);
        _sprite.LayerSetColor((ent, sprite), WFAsteroidVisualLayers.Veins, ent.Comp.VeinColor);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        LoadPendingRsis();

        _cooled.Clear();
        foreach (var uid in _hot)
        {
            if (!TryComp<WFAsteroidComponent>(uid, out var rock) || !TryComp<SpriteComponent>(uid, out var sprite))
            {
                _cooled.Add(uid);
                continue;
            }

            // Ease toward the server's heat so 50 ms state steps don't show.
            rock.DisplayCharge += (rock.Charge - rock.DisplayCharge) * MathF.Min(1f, frameTime * 6f);
            if (rock.Charge <= 0f && rock.DisplayCharge < 0.005f)
                rock.DisplayCharge = 0f;
            SetHeat((uid, rock, sprite), rock.DisplayCharge);
            Shake((uid, rock, sprite));
            if (rock.Charge <= 0f && rock.DisplayCharge <= 0f)
                _cooled.Add(uid);
        }

        foreach (var uid in _cooled)
        {
            _hot.Remove(uid);
        }
    }

    /// <summary>
    /// Loads the RSIs newly seen asteroid grids need. A look names its RSI; otherwise a sprite's RSI only loads on a spawned
    /// entity, so one is made in nullspace to read it, as the engine's own icon helpers do.
    /// </summary>
    private void LoadPendingRsis()
    {
        foreach (var key in _pendingRsis)
        {
            if (_farRsis.ContainsKey(key))
                continue;

            if (!_prototypes.TryIndex(key.Rock, out var proto) ||
                !proto.TryComp<WFAsteroidComponent>(out var rockProto, EntityManager.ComponentFactory))
            {
                _farRsis[key] = null;
                continue;
            }

            RSI? rsi;
            if (key.Look is { } look && rockProto.Looks.TryGetValue(look, out var path))
            {
                rsi = _resources.GetResource<RSIResource>(SpriteSpecifierSerializer.TextureRoot / path).RSI;
            }
            else
            {
                var dummy = Spawn(key.Rock, MapCoordinates.Nullspace);
                rsi = CompOrNull<SpriteComponent>(dummy)?.BaseRSI;
                Del(dummy);
            }

            _farRsis[key] = rsi;
        }

        _pendingRsis.Clear();
    }

    /// <summary>Past 60% crack progress the rock trembles, harder the closer it is to breaking.</summary>
    private void Shake(Entity<WFAsteroidComponent, SpriteComponent> ent)
    {
        var shake = Math.Clamp((ent.Comp1.Progress - 0.6f) / 0.4f, 0f, 1f);
        if (shake <= 0f || ent.Comp1.Charge <= 0f)
        {
            if (ent.Comp2.Offset != Vector2.Zero)
                _sprite.SetOffset((ent, ent.Comp2), Vector2.Zero);
            return;
        }

        var time = (float) _timing.RealTime.TotalSeconds;
        var amplitude = (0.02f + 0.06f * shake) * MathF.Min(1.5f, 0.5f + ent.Comp1.Radius * 0.2f);
        var offset = new Vector2(MathF.Sin(time * 47f + ent.Owner.Id), MathF.Cos(time * 53f + ent.Owner.Id * 0.7f)) * amplitude;
        _sprite.SetOffset((ent, ent.Comp2), offset);
    }

    private void SetHeat(Entity<WFAsteroidComponent, SpriteComponent> ent, float charge)
    {
        var heat = Math.Clamp(charge, 0f, 1.2f);
        var color = heat < 0.55f
            ? Color.InterpolateBetween(Ember, Orange, heat / 0.55f)
            : Color.InterpolateBetween(Orange, WhiteHot, Math.Clamp((heat - 0.55f) / 0.45f, 0f, 1f));
        var alpha = Math.Clamp((heat - 0.03f) / 0.45f, 0f, 1f);
        if (ent.Comp1.Overcharging)
        {
            var time = (float) _timing.RealTime.TotalSeconds;
            alpha *= 0.65f + 0.35f * MathF.Sin(time * 27f);
        }

        _sprite.LayerSetColor((ent, ent.Comp2), WFAsteroidVisualLayers.Heat, color.WithAlpha(alpha));
        // Its shader breaks the sprite batch, so the layer is only drawn while it shows.
        _sprite.LayerSetVisible((ent, ent.Comp2), WFAsteroidVisualLayers.Heat, alpha > 0f);
        if (!TryComp<PointLightComponent>(ent, out var light))
            return;

        _lights.SetEnabled(ent, alpha > 0.02f, light);
        _lights.SetColor(ent, color, light);
        _lights.SetEnergy(ent, alpha * 2.5f, light);
    }
}
