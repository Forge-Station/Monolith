using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared._WF.WolfWeather;

/// <summary>One kind of weather: its layers, sounds, light tint, wind and lightning.</summary>
[Prototype]
public sealed partial class WolfWeatherPrototype : IPrototype, IInheritingPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<WolfWeatherPrototype>))]
    public string[]? Parents { get; private set; }

    [AbstractDataField]
    public bool Abstract { get; private set; }

    [DataField]
    public List<WolfWeatherLayer> Layers = new();

    /// <summary>Changes to single inherited layers, picked by index.</summary>
    [DataField]
    public List<WolfWeatherLayerOverride> LayerOverrides = new();

    private List<WolfWeatherLayer>? _finalLayers;

    /// <summary>The layers to draw, with the overrides applied.</summary>
    public IReadOnlyList<WolfWeatherLayer> FinalLayers => _finalLayers ??= BuildFinalLayers();

    [DataField]
    public Vector2 Wind;

    [DataField]
    public float Gust = 0.15f;

    [DataField]
    public float GustPeriod = 20f;

    [DataField]
    public float WindResponse = 6f;

    /// <summary>Sounds that loop while the weather is on, all at once.</summary>
    [DataField]
    public List<WolfWeatherSound> Sounds = new();

    [DataField]
    public TimeSpan FadeIn = TimeSpan.FromSeconds(15);

    [DataField]
    public TimeSpan FadeOut = TimeSpan.FromSeconds(15);

    [DataField]
    public Color AmbientTint = Color.White;

    [DataField]
    public WolfWeatherLightning? Lightning;

    /// <summary>How wet open ground gets at full strength, from 0 to 1. Above 0.3 puddles form too.</summary>
    [DataField]
    public float GroundWetness;

    /// <summary>Snow laid on open ground at full strength, in cover levels a minute.</summary>
    [DataField]
    public float Snowfall;

    /// <summary>
    /// The colour of the puddles and wet floor this weather leaves, as the ground shader takes it; null is plain water
    /// (<see cref="Content.Shared._WF.GroundCover.WFGroundWeatherComponent.Water"/>). Blood rain sets a blood red.
    /// </summary>
    [DataField]
    public Color? GroundTint;

    private List<WolfWeatherLayer> BuildFinalLayers()
    {
        if (LayerOverrides.Count == 0)
            return Layers;

        var layers = new List<WolfWeatherLayer>(Layers.Count);
        foreach (var layer in Layers)
        {
            layers.Add(layer.Copy());
        }

        foreach (var change in LayerOverrides)
        {
            if (change.Index >= 0 && change.Index < layers.Count)
                change.ApplyTo(layers[change.Index]);
        }

        return layers;
    }
}

/// <summary>A looping weather sound. With several files, one is picked at random when the weather starts.</summary>
[DataDefinition]
public sealed partial class WolfWeatherSound
{
    [DataField]
    public List<ResPath> Files = new();

    /// <summary>Picks from this sound collection's files as well as <see cref="Files"/>.</summary>
    [DataField]
    public ProtoId<SoundCollectionPrototype>? Collection;

    /// <summary>Volume as a share of normal. 1 is normal, 0.25 is a quarter.</summary>
    [DataField]
    public float Volume = 1f;
}

/// <summary>One shader layer of a weather, such as rain streaks or splashes.</summary>
[DataDefinition]
public sealed partial class WolfWeatherLayer
{
    [DataField]
    public WolfWeatherLayerKind Kind = WolfWeatherLayerKind.Rain;

    [DataField]
    public float Spacing = 0.5f;

    [DataField]
    public float Density = 0.5f;

    [DataField]
    public float FallSpeed = 12f;

    [DataField]
    public float WindScale = 1f;

    [DataField]
    public float Length = 6f;

    [DataField]
    public float Width = 1f;

    [DataField]
    public float Wobble;

    [DataField]
    public float Rate = 1f;

    [DataField]
    public float Softness = 0.25f;

    [DataField]
    public Color Color = Color.White;

    [DataField]
    public int MinQuality = 1;

    public WolfWeatherLayer Copy()
    {
        return new WolfWeatherLayer
        {
            Kind = Kind,
            Spacing = Spacing,
            Density = Density,
            FallSpeed = FallSpeed,
            WindScale = WindScale,
            Length = Length,
            Width = Width,
            Wobble = Wobble,
            Rate = Rate,
            Softness = Softness,
            Color = Color,
            MinQuality = MinQuality,
        };
    }
}

/// <summary>New values for some fields of one inherited layer. Fields left out keep the parent's value.</summary>
[DataDefinition]
public sealed partial class WolfWeatherLayerOverride
{
    /// <summary>Which layer to change, counting from 0.</summary>
    [DataField(required: true)]
    public int Index;

    [DataField]
    public float? Spacing;

    [DataField]
    public float? Density;

    [DataField]
    public float? FallSpeed;

    [DataField]
    public float? WindScale;

    [DataField]
    public float? Length;

    [DataField]
    public float? Width;

    [DataField]
    public float? Wobble;

    [DataField]
    public float? Rate;

    [DataField]
    public float? Softness;

    [DataField]
    public Color? Color;

    [DataField]
    public int? MinQuality;

    public void ApplyTo(WolfWeatherLayer layer)
    {
        layer.Spacing = Spacing ?? layer.Spacing;
        layer.Density = Density ?? layer.Density;
        layer.FallSpeed = FallSpeed ?? layer.FallSpeed;
        layer.WindScale = WindScale ?? layer.WindScale;
        layer.Length = Length ?? layer.Length;
        layer.Width = Width ?? layer.Width;
        layer.Wobble = Wobble ?? layer.Wobble;
        layer.Rate = Rate ?? layer.Rate;
        layer.Softness = Softness ?? layer.Softness;
        layer.Color = Color ?? layer.Color;
        layer.MinQuality = MinQuality ?? layer.MinQuality;
    }
}

/// <summary>What a weather layer draws.</summary>
public enum WolfWeatherLayerKind : byte
{
    Rain,
    Snow,
    Splash,
    Fog,
}
