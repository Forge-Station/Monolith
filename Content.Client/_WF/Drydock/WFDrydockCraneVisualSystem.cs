using System.Linq;
using System.Numerics;
using Content.Shared._WF.Drydock;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.Client._WF.Drydock;

/// <summary>
/// Builds a crane's sprites and eases its arm, boom, carriage and tool head toward their targets. The arm extends first;
/// the head unfolds once it settles and folds before the arm retracts.
/// </summary>
/// <remarks>
/// The body stays on the crane, but the moving parts draw on a client-only rig parented to the map. Sprites are culled per
/// grid, so an arm reaching past the crane's grid would vanish whenever the camera left that grid; the map is always drawn.
/// </remarks>
public sealed partial class WFDrydockCraneVisualSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly ResPath CraneRsi = new("/Textures/_WF/Drydock/crane.rsi");
    private static readonly ResPath MachineRsi = new("/Textures/_WF/Drydock/crane_machine.rsi");

    /// <summary>Arm length below which the arm's end cap and the stowed head are hidden.</summary>
    private const float StowedArm = 0.05f;

    private readonly Dictionary<EntityUid, CraneLayers> _cranes = new();

    /// <summary>Cranes whose sprites are rebuilt after this frame's loop: mirrored, upgraded to a new reach, or their rig was deleted.</summary>
    private readonly List<EntityUid> _rebuild = new();

    public override void Initialize()
    {
        base.Initialize();

        // The rig follows the crane's interpolated position, so it moves after transforms are lerped.
        UpdatesAfter.Add(typeof(TransformSystem));

        SubscribeLocalEvent<WFDrydockCraneComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WFDrydockCraneComponent, ComponentShutdown>(OnShutdown);
    }

    /// <summary>The client-only entity that draws a crane's arm and boom, if it has one.</summary>
    public bool TryGetRig(EntityUid crane, out EntityUid rig)
    {
        rig = _cranes.TryGetValue(crane, out var layers) ? layers.Rig : EntityUid.Invalid;
        return rig.IsValid();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        _rebuild.Clear();
        var query = EntityQueryEnumerator<WFDrydockCraneComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crane, out var sprite, out var xform))
        {
            if (!_cranes.TryGetValue(uid, out var layers))
                continue;

            if (layers.Mirrored != crane.Mirrored || layers.Reach != (crane.MaxArm, crane.Sections, crane.SectionLength) ||
                !TryComp(layers.Rig, out SpriteComponent? rigSprite))
            {
                _rebuild.Add(uid);
                continue;
            }

            var rig = (layers.Rig, rigSprite);
            if (!SyncRig(xform, sprite, layers, rig))
                continue;

            // Read before easing: the arm may only retract once the boom is folded.
            var folded = crane.VisualAngle == crane.ParkAngle && crane.VisualExtension == crane.SectionLength;

            Ease(crane, frameTime, folded);
            PlaceBoom(rig, crane, layers);
            UpdateArm(rig, crane, layers);
            UpdateHead(rig, crane, layers, folded);
            UpdateBeacons((uid, sprite), rig, crane, layers);
        }

        foreach (var uid in _rebuild)
        {
            if (TryComp(uid, out WFDrydockCraneComponent? crane))
                Build((uid, crane));
        }
    }

    /// <summary>
    /// Puts the rig on the crane's map at the crane's world position and rotation, or hides it while the crane has no map or
    /// is hidden. Returns whether the rig is shown.
    /// </summary>
    private bool SyncRig(TransformComponent xform, SpriteComponent sprite, CraneLayers layers, Entity<SpriteComponent?> rig)
    {
        var shown = xform.MapUid != null && sprite.Visible;
        if (layers.RigShown != shown)
        {
            layers.RigShown = shown;
            _sprite.SetVisible(rig, shown);
        }

        if (xform.MapUid is not { } map)
            return false;

        var rigXform = Transform(rig.Owner);
        if (rigXform.ParentUid != map)
            _transform.SetCoordinates(rig.Owner, new EntityCoordinates(map, Vector2.Zero));

        // Local to the map is world space. Setting the world position would re-parent the rig onto the grid under it.
        var (position, rotation) = _transform.GetWorldPositionRotation(xform);
        _transform.SetLocalPositionRotation(rig.Owner, position, rotation, rigXform);
        return shown;
    }

    /// <summary>Moves the visual arm, boom, trolley and carriage toward the crane's targets at its speed, slowed by its power.</summary>
    private void Ease(WFDrydockCraneComponent crane, float frameTime, bool folded)
    {
        var extension = Math.Clamp(crane.Extension, crane.SectionLength, crane.MaxExtension);

        // Every axis slows with the crane's power, and a stalled crane does not move at all.
        var power = crane.PowerRatio < WFDrydockCraneComponent.StallRatio ? 0f : crane.PowerRatio;
        var step = frameTime * power * crane.SpeedMultiplier;

        if (crane.Arm > crane.VisualArm || folded)
            crane.VisualArm = Approach(crane.VisualArm, crane.Arm, crane.ArmSpeed * step);

        // The boom and tool head work only once the arm has settled; until then they fold.
        var settled = crane.VisualArm == crane.Arm;
        crane.VisualAngle = Approach(crane.VisualAngle, settled ? crane.Angle : crane.ParkAngle, crane.SlewSpeed * step);
        crane.VisualExtension = Approach(crane.VisualExtension, settled ? extension : crane.SectionLength, crane.ExtendSpeed * step);
        crane.VisualTrolley = Approach(crane.VisualTrolley, settled ? crane.TrolleyPosition : 0f, crane.TrolleySpeed * step);

        // The carriage rides the arm, so it never passes the arm's end.
        crane.VisualPosition = Math.Clamp(Approach(crane.VisualPosition, crane.Position, crane.Speed * step), 0f, crane.VisualArm);
    }

    /// <summary>Places the boom sections, tip, trolley and carriage along the slewed boom.</summary>
    private void PlaceBoom(Entity<SpriteComponent?> rig, WFDrydockCraneComponent crane, CraneLayers layers)
    {
        var pivot = crane.ArmToLocal(0f, crane.VisualPosition);
        var dir = crane.VisualBoomDirection;
        var slide = crane.Sections > 1 ? (crane.VisualExtension - crane.SectionLength) / (crane.Sections - 1) : 0f;

        foreach (var (layer, section, tile) in layers.Boom)
        {
            Place(rig, layer, pivot, dir, section * slide + tile + 1f);
        }

        Place(rig, layers.Tip, pivot, dir, crane.VisualExtension);
        Place(rig, layers.TipBeacon, pivot, dir, crane.VisualExtension);
        Place(rig, layers.Trolley, pivot, dir, Math.Clamp(crane.VisualTrolley, 1f, crane.VisualExtension));

        // The carriage stays square on the arm; the turret on it slews with the boom.
        PlaceAt(rig, layers.Carriage, pivot, Angle.Zero);
        PlaceAt(rig, layers.Turret, pivot, new Angle(dir));
        PlaceAt(rig, layers.CarriageBeacon, pivot, Angle.Zero);
    }

    /// <summary>Shows the arm segments up to its length and moves the end cap and the outermost segment to match.</summary>
    private void UpdateArm(Entity<SpriteComponent?> rig, WFDrydockCraneComponent crane, CraneLayers layers)
    {
        var armShown = Math.Min(layers.Arm.Count, (int) MathF.Ceiling(crane.VisualArm));
        if (armShown != layers.ArmShown)
        {
            layers.ArmShown = armShown;
            for (var i = 0; i < layers.Arm.Count; i++)
            {
                _sprite.LayerSetVisible(rig, layers.Arm[i], i < armShown);
                if (i < armShown)
                {
                    _sprite.LayerSetOffset(rig, layers.Arm[i], crane.ArmToLocal(0f, i + 0.5f));
                }
            }
        }

        // Only the last visible segment can overhang the head; keep it within the arm's end cap.
        if (armShown > 0)
        {
            var last = armShown - 1;
            var along = Math.Max(0.5f, Math.Min(last + 0.5f, crane.VisualArm - 0.5f));
            _sprite.LayerSetOffset(rig, layers.Arm[last], crane.ArmToLocal(0f, along));
        }

        _sprite.LayerSetOffset(rig, layers.ArmEnd, crane.ArmToLocal(0f, crane.VisualArm));
        var endShown = crane.VisualArm >= StowedArm;
        if (layers.EndShown != endShown)
        {
            layers.EndShown = endShown;
            _sprite.LayerSetVisible(rig, layers.ArmEnd, endShown);
        }
    }

    /// <summary>Hides the boom and tool head while they sit stowed inside the machine.</summary>
    private void UpdateHead(Entity<SpriteComponent?> rig, WFDrydockCraneComponent crane, CraneLayers layers, bool folded)
    {
        var stowed = crane.VisualArm < StowedArm && folded;
        var headShown = !stowed;
        if (layers.HeadShown == headShown)
            return;

        layers.HeadShown = headShown;
        SetHeadVisible(rig, layers, headShown);
    }

    /// <summary>Switches the beacons and machine lights on or off as the crane gains or loses power.</summary>
    private void UpdateBeacons(Entity<SpriteComponent?> body, Entity<SpriteComponent?> rig, WFDrydockCraneComponent crane, CraneLayers layers)
    {
        var lit = crane.Active && crane.PowerRatio >= WFDrydockCraneComponent.StallRatio;
        if (layers.BeaconOn == lit)
            return;

        layers.BeaconOn = lit;
        var state = lit ? "beacon_on" : "beacon_off";
        _sprite.LayerSetRsiState(rig, layers.TipBeacon, state);
        _sprite.LayerSetRsiState(rig, layers.CarriageBeacon, state);
        _sprite.LayerSetVisible(body, layers.MachineLights, lit);
    }

    private void SetHeadVisible(Entity<SpriteComponent?> rig, CraneLayers layers, bool visible)
    {
        foreach (var (layer, _, _) in layers.Boom)
        {
            _sprite.LayerSetVisible(rig, layer, visible);
        }

        _sprite.LayerSetVisible(rig, layers.Tip, visible);
        _sprite.LayerSetVisible(rig, layers.TipBeacon, visible);
        _sprite.LayerSetVisible(rig, layers.Trolley, visible);
        _sprite.LayerSetVisible(rig, layers.Carriage, visible);
        _sprite.LayerSetVisible(rig, layers.Turret, visible);
        _sprite.LayerSetVisible(rig, layers.CarriageBeacon, visible);
    }

    private void OnStartup(Entity<WFDrydockCraneComponent> ent, ref ComponentStartup args)
    {
        // Start stowed so a crane arriving mid-job animates out to its targets instead of popping in.
        var comp = ent.Comp;
        comp.VisualArm = 0f;
        comp.VisualPosition = 0f;
        comp.VisualAngle = comp.ParkAngle;
        comp.VisualExtension = comp.SectionLength;
        comp.VisualTrolley = 0f;

        Build(ent);
    }

    private void OnShutdown(Entity<WFDrydockCraneComponent> ent, ref ComponentShutdown args)
    {
        if (_cranes.Remove(ent.Owner, out var layers))
            QueueDel(layers.Rig);
    }

    /// <summary>
    /// Builds the crane's body layers and a fresh rig with its arm and boom layers, replacing any earlier build. The
    /// animation state on the component is kept.
    /// </summary>
    private void Build(Entity<WFDrydockCraneComponent> ent)
    {
        if (!TryComp(ent.Owner, out SpriteComponent? sprite))
            return;

        if (_cranes.Remove(ent.Owner, out var old))
            QueueDel(old.Rig);

        // Drop the prototype's placeholder layers, or the previous build's body.
        var body = (ent.Owner, sprite);
        for (var i = sprite.AllLayers.Count() - 1; i >= 0; i--)
        {
            _sprite.RemoveLayer(body, i);
        }

        var comp = ent.Comp;
        var rigUid = Spawn(null, MapCoordinates.Nullspace);
        // Grid traversal would move the rig onto a grid it passes over; it must stay on the map.
        Transform(rigUid).GridTraversal = false;
        var rigSprite = AddComp<SpriteComponent>(rigUid);
        _sprite.SetDrawDepth((rigUid, rigSprite), sprite.DrawDepth);
        var rig = (rigUid, rigSprite);
        var layers = new CraneLayers { Rig = rigUid, Mirrored = comp.Mirrored, Reach = (comp.MaxArm, comp.Sections, comp.SectionLength) };

        // The arm is one segment per tile, each centred on its tile; the end cap sits at the arm's head.
        for (var i = 0; i < MathF.Ceiling(comp.MaxArm); i++)
        {
            var segment = AddLayer(rig, CraneRsi, "arm");
            _sprite.LayerSetOffset(rig, segment, comp.ArmToLocal(0f, i + 0.5f));
            layers.Arm.Add(segment);
        }

        layers.ArmEnd = AddLayer(rig, CraneRsi, "arm_end");

        // Innermost section first, so each outer sleeve draws over the one sliding out of it.
        for (var section = comp.Sections - 1; section >= 0; section--)
        {
            var state = section == 0 ? "boom_outer" : section == comp.Sections - 1 ? "boom_inner" : "boom_mid";
            for (var tile = 0; tile < comp.SectionLength; tile++)
            {
                layers.Boom.Add((AddLayer(rig, CraneRsi, state), section, tile));
            }
        }

        layers.Tip = AddLayer(rig, CraneRsi, "boom_tip");
        layers.TipBeacon = AddLayer(rig, CraneRsi, "beacon_off");
        layers.Trolley = AddLayer(rig, CraneRsi, "trolley");
        layers.Carriage = AddLayer(rig, CraneRsi, "carriage");
        layers.Turret = AddLayer(rig, CraneRsi, "turret");
        layers.CarriageBeacon = AddLayer(rig, CraneRsi, "beacon_off");

        // The body is the 2x2 square beside the arm's root; mirrored cranes use the body facing -X.
        var center = comp.ArmToLocal(0.5f, -1f);
        var machine = AddLayer(body, MachineRsi, comp.Mirrored ? "machine_mirrored" : "machine");
        layers.MachineLights = AddLayer(body, MachineRsi, comp.Mirrored ? "machine_lights_mirrored" : "machine_lights");
        _sprite.LayerSetOffset(body, machine, center);
        _sprite.LayerSetOffset(body, layers.MachineLights, center);

        // Same depth as the rig but later in order, so the body draws over the arm and the stowed head.
        sprite.RenderOrder = 1;

        // Lights and beacon lenses glow on their own, so they skip scene lighting.
        sprite.LayerSetShader(layers.MachineLights, "unshaded");
        rigSprite.LayerSetShader(layers.TipBeacon, "unshaded");
        rigSprite.LayerSetShader(layers.CarriageBeacon, "unshaded");

        _cranes[ent.Owner] = layers;
    }

    /// <summary>Puts a layer at a distance along a boom direction from the pivot, turned with it.</summary>
    private void Place(Entity<SpriteComponent?> sprite, int layer, Vector2 pivot, Vector2 dir, float distance)
    {
        PlaceAt(sprite, layer, pivot + dir * distance, new Angle(dir));
    }

    private void PlaceAt(Entity<SpriteComponent?> sprite, int layer, Vector2 offset, Angle angle)
    {
        _sprite.LayerSetOffset(sprite, layer, offset);
        _sprite.LayerSetRotation(sprite, layer, angle);
    }

    private int AddLayer(Entity<SpriteComponent?> sprite, ResPath rsi, string state)
    {
        return _sprite.AddLayer(sprite, new SpriteSpecifier.Rsi(rsi, state));
    }

    /// <summary>
    /// Moves <paramref name="current"/> toward <paramref name="target"/> by at most <paramref name="maxDelta"/>.
    /// </summary>
    private static float Approach(float current, float target, float maxDelta)
    {
        if (current < target)
            return MathF.Min(current + maxDelta, target);

        return MathF.Max(current - maxDelta, target);
    }

    /// <summary>
    /// Layer indices for one crane: the machine lights on the crane, the rest on its rig. Boom layers know their section and
    /// tile within it. The <c>*Shown</c> and <c>BeaconOn</c> fields cache the last applied visibility, so a null forces the
    /// first update.
    /// </summary>
    private sealed class CraneLayers
    {
        public EntityUid Rig;
        public bool Mirrored;
        public (float MaxArm, int Sections, int SectionLength) Reach;
        public readonly List<int> Arm = new();
        public readonly List<(int Layer, int Section, int Tile)> Boom = new();
        public int ArmEnd;
        public int Tip;
        public int TipBeacon;
        public int Trolley;
        public int Carriage;
        public int Turret;
        public int CarriageBeacon;
        public int MachineLights;
        public int ArmShown = -1;
        public bool? EndShown;
        public bool? HeadShown;
        public bool? BeaconOn;
        public bool? RigShown;
    }
}
