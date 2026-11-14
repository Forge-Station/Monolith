namespace Content.Client._WF.Hailing;

/// <summary>
/// Ticks every hail key. The console they sit on is hidden while the cockpit is up, and the engine skips the
/// frame update of a hidden control, so a key could not notice a hail arriving by itself.
/// </summary>
public sealed class HailKeySystem : EntitySystem
{
    private readonly List<HailKey> _keys = new();

    public void Track(HailKey key)
    {
        if (!_keys.Contains(key))
            _keys.Add(key);
    }

    public void Untrack(HailKey key)
    {
        _keys.Remove(key);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // Backwards, so a key that leaves the tree while it ticks doesn't upset the walk.
        for (var i = _keys.Count - 1; i >= 0; i--)
        {
            if (i < _keys.Count)
                _keys[i].Tick(frameTime);
        }
    }
}
