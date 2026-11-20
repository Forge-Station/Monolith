using System.Globalization;
using System.IO;
using System.Numerics;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;

namespace Content.Shared._WF.MappingMode;

/// <summary>Whether the template draws below the grids or over everything.</summary>
[Serializable, NetSerializable]
public enum WFMappingTemplateLayer : byte
{
    Under,
    Over,
}

/// <summary>
/// Where and how the template image is drawn: its centre in world tiles, scale, rotation, opacity and layer. Every
/// setter clamps; read from and written to YAML text, no file access.
/// </summary>
public sealed class WFMappingTemplateSettings
{
    public const float MinScale = 0.01f;
    public const float MaxScale = 100f;

    /// <summary>Furthest the centre may be from the origin on either axis, in tiles.</summary>
    public const float MaxOffset = 100000f;

    public const int MinPixelsPerTile = 1;
    public const int MaxPixelsPerTile = 1024;
    public const int DefaultPixelsPerTile = 32;
    public const float DefaultOpacity = 0.5f;

    private Vector2 _position;
    private float _scaleX = 1f;
    private float _scaleY = 1f;
    private float _rotation;
    private float _opacity = DefaultOpacity;
    private int _pixelsPerTile = DefaultPixelsPerTile;

    /// <summary>The image's centre in world tiles.</summary>
    public Vector2 Position
    {
        get => _position;
        set => _position = new Vector2(ClampOffset(value.X), ClampOffset(value.Y));
    }

    public float ScaleX
    {
        get => _scaleX;
        set => _scaleX = ClampScale(value);
    }

    public float ScaleY
    {
        get => _scaleY;
        set => _scaleY = ClampScale(value);
    }

    /// <summary>Whether editing one scale keeps the ratio between the two.</summary>
    public bool Uniform { get; set; } = true;

    /// <summary>Counter-clockwise turn in degrees, kept between -180 and 180.</summary>
    public float Rotation
    {
        get => _rotation;
        set => _rotation = WrapDegrees(value);
    }

    public float Opacity
    {
        get => _opacity;
        set => _opacity = float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : DefaultOpacity;
    }

    /// <summary>How many image pixels make one tile at scale 1.</summary>
    public int PixelsPerTile
    {
        get => _pixelsPerTile;
        set => _pixelsPerTile = Math.Clamp(value, MinPixelsPerTile, MaxPixelsPerTile);
    }

    /// <summary>Whether dragging, the wheel and the arrows leave it alone.</summary>
    public bool Locked { get; set; }

    public bool Visible { get; set; } = true;

    public WFMappingTemplateLayer Layer { get; set; } = WFMappingTemplateLayer.Under;

    /// <summary>The z-level it belongs to; it is drawn only there.</summary>
    public int Depth { get; set; }

    /// <summary>Sets one scale; with <see cref="Uniform"/> the other follows by the same factor.</summary>
    public void SetScaleX(float value)
    {
        var old = _scaleX;
        ScaleX = value;
        if (Uniform && old > 0f)
            ScaleY = _scaleY * (_scaleX / old);
    }

    /// <inheritdoc cref="SetScaleX"/>
    public void SetScaleY(float value)
    {
        var old = _scaleY;
        ScaleY = value;
        if (Uniform && old > 0f)
            ScaleX = _scaleX * (_scaleY / old);
    }

    /// <summary>The image's size in tiles.</summary>
    public Vector2 Size(Vector2i pixels)
    {
        return new Vector2(pixels.X * _scaleX / _pixelsPerTile, pixels.Y * _scaleY / _pixelsPerTile);
    }

    /// <summary>The image's quad in world space.</summary>
    public Box2Rotated Quad(Vector2i pixels)
    {
        var half = Size(pixels) / 2f;
        return new Box2Rotated(new Box2(_position - half, _position + half), Angle.FromDegrees(_rotation), _position);
    }

    /// <summary>Back to the defaults at a new centre; the depth stays.</summary>
    public void Reset(Vector2 position)
    {
        var depth = Depth;
        CopyFrom(new WFMappingTemplateSettings());
        Position = position;
        Depth = depth;
    }

    public WFMappingTemplateSettings Clone()
    {
        var clone = new WFMappingTemplateSettings();
        clone.CopyFrom(this);
        return clone;
    }

    public void CopyFrom(WFMappingTemplateSettings other)
    {
        _position = other._position;
        _scaleX = other._scaleX;
        _scaleY = other._scaleY;
        _rotation = other._rotation;
        _opacity = other._opacity;
        _pixelsPerTile = other._pixelsPerTile;
        Uniform = other.Uniform;
        Locked = other.Locked;
        Visible = other.Visible;
        Layer = other.Layer;
        Depth = other.Depth;
    }

    /// <summary>The part a session shares: centre, scale, pixels per tile, rotation, layer and depth.</summary>
    public WFMappingTemplateState ToState()
    {
        return new WFMappingTemplateState(_position.X, _position.Y, _scaleX, _scaleY, _rotation, _pixelsPerTile, Layer,
            Depth);
    }

    /// <summary>Takes the shared part from <paramref name="state"/>, clamped; opacity, show and lock stay.</summary>
    public void ApplyState(WFMappingTemplateState state)
    {
        Position = new Vector2(state.X, state.Y);
        ScaleX = state.ScaleX;
        ScaleY = state.ScaleY;
        Rotation = state.Rotation;
        PixelsPerTile = state.PixelsPerTile;
        Layer = state.Layer == WFMappingTemplateLayer.Over ? WFMappingTemplateLayer.Over : WFMappingTemplateLayer.Under;
        Depth = state.Depth;
    }

    /// <summary>A state with every value clamped as the setters do, and the depth kept between the two given.</summary>
    public static WFMappingTemplateState Clamp(WFMappingTemplateState state, int minDepth, int maxDepth)
    {
        var settings = new WFMappingTemplateSettings();
        settings.ApplyState(state);
        settings.Depth = Math.Clamp(state.Depth, minDepth, Math.Max(minDepth, maxDepth));
        return settings.ToState();
    }

    /// <summary>Reads settings; missing or malformed values keep their defaults, as does unreadable text.</summary>
    public static WFMappingTemplateSettings Read(string text)
    {
        var settings = new WFMappingTemplateSettings();
        YamlMappingNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
        }
        catch (Exception)
        {
            return settings;
        }

        if (root == null)
            return settings;

        settings.Position = new Vector2(Float(root, "x", 0f), Float(root, "y", 0f));
        settings.ScaleX = Float(root, "scaleX", 1f);
        settings.ScaleY = Float(root, "scaleY", 1f);
        settings.Uniform = Bool(root, "uniform", true);
        settings.Rotation = Float(root, "rotation", 0f);
        settings.Opacity = Float(root, "opacity", DefaultOpacity);
        settings.PixelsPerTile = Int(root, "pixelsPerTile", DefaultPixelsPerTile);
        settings.Locked = Bool(root, "locked", false);
        settings.Visible = Bool(root, "visible", true);
        settings.Layer = Scalar(root, "layer") == "over" ? WFMappingTemplateLayer.Over : WFMappingTemplateLayer.Under;
        settings.Depth = Int(root, "depth", 0);
        return settings;
    }

    /// <summary>The settings as YAML text.</summary>
    public string Write()
    {
        var root = new YamlMappingNode();
        root.Add("x", FloatText(_position.X));
        root.Add("y", FloatText(_position.Y));
        root.Add("scaleX", FloatText(_scaleX));
        root.Add("scaleY", FloatText(_scaleY));
        root.Add("uniform", BoolText(Uniform));
        root.Add("rotation", FloatText(_rotation));
        root.Add("opacity", FloatText(_opacity));
        root.Add("pixelsPerTile", _pixelsPerTile.ToString(CultureInfo.InvariantCulture));
        root.Add("locked", BoolText(Locked));
        root.Add("visible", BoolText(Visible));
        root.Add("layer", Layer == WFMappingTemplateLayer.Over ? "over" : "under");
        root.Add("depth", Depth.ToString(CultureInfo.InvariantCulture));

        var writer = new StringWriter();
        new YamlStream(new YamlDocument(root)).Save(writer, false);
        return writer.ToString();
    }

    public static float ClampScale(float value)
    {
        return float.IsFinite(value) ? Math.Clamp(value, MinScale, MaxScale) : 1f;
    }

    public static float ClampOffset(float value)
    {
        return float.IsFinite(value) ? Math.Clamp(value, -MaxOffset, MaxOffset) : 0f;
    }

    /// <summary>An angle in degrees brought into (-180, 180].</summary>
    public static float WrapDegrees(float value)
    {
        if (!float.IsFinite(value))
            return 0f;

        var wrapped = value % 360f;
        if (wrapped > 180f)
            wrapped -= 360f;
        else if (wrapped <= -180f)
            wrapped += 360f;

        return wrapped;
    }

    private static string FloatText(float value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string BoolText(bool value)
    {
        return value ? "true" : "false";
    }

    private static float Float(YamlMappingNode node, string key, float fallback)
    {
        return Scalar(node, key) is { } text && Parse.TryFloat(text, out var value) && float.IsFinite(value)
            ? value
            : fallback;
    }

    private static int Int(YamlMappingNode node, string key, int fallback)
    {
        return Scalar(node, key) is { } text && Parse.TryInt32(text, out var value) ? value : fallback;
    }

    private static bool Bool(YamlMappingNode node, string key, bool fallback)
    {
        return Scalar(node, key) switch
        {
            "true" => true,
            "false" => false,
            _ => fallback,
        };
    }

    /// <summary>A mapping's scalar by key; enumerated, since the sandbox doesn't allow its dictionary.</summary>
    private static string? Scalar(YamlMappingNode node, string key)
    {
        foreach (var pair in node)
        {
            if (pair.Key is YamlScalarNode { Value: { } name } && name == key)
                return (pair.Value as YamlScalarNode)?.Value;
        }

        return null;
    }
}
