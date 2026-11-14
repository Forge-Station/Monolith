using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Robust.Shared.Noise;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Planets;

/// <summary>
/// Keeps one seeded copy of each biome layer's noise, so terrain lookups stop serialiser-copying it for every tile.
/// Callers only read the copies, and reading a <see cref="FastNoiseLite"/> is safe from any thread.
/// </summary>
public sealed class WFBiomeNoiseCacheSystem : EntitySystem
{
    /// <summary>Copies kept before the cache starts over; every loaded world together needs a few hundred.</summary>
    public const int Capacity = 4096;

    private readonly ConcurrentDictionary<Key, FastNoiseLite> _copies = new();

    /// <summary>How many copies are cached.</summary>
    public int CopyCount => _copies.Count;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    /// <summary>The cached copy of <paramref name="source"/> with <paramref name="seed"/> added to its seed, if there is one.</summary>
    public bool TryGet(FastNoiseLite source, int seed, [NotNullWhen(true)] out FastNoiseLite? copy)
    {
        return _copies.TryGetValue(new Key(source, unchecked(source.GetSeed() + seed)), out copy);
    }

    /// <summary>Caches a seeded copy of <paramref name="source"/> and returns the one to use: an earlier copy if another thread got there first.</summary>
    public FastNoiseLite Add(FastNoiseLite source, FastNoiseLite copy)
    {
        if (_copies.Count >= Capacity)
            _copies.Clear();

        return _copies.GetOrAdd(new Key(source, copy.GetSeed()), copy);
    }

    /// <summary>Drops every copy.</summary>
    public void Clear()
    {
        _copies.Clear();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        _copies.Clear();
    }

    /// <summary>
    /// A source noise by reference and the seed its copy ends up with. The source's own seed is in it because
    /// <c>BiomeSystem.AddTemplate</c> shifts template seeds in place.
    /// </summary>
    private readonly struct Key : IEquatable<Key>
    {
        private readonly FastNoiseLite _source;
        private readonly int _seed;

        public Key(FastNoiseLite source, int seed)
        {
            _source = source;
            _seed = seed;
        }

        public bool Equals(Key other)
        {
            return ReferenceEquals(_source, other._source) && _seed == other._seed;
        }

        public override bool Equals(object? obj)
        {
            return obj is Key other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(RuntimeHelpers.GetHashCode(_source), _seed);
        }
    }
}
