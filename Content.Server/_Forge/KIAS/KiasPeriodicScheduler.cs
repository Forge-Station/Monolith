namespace Content.Server._Forge.KIAS;

public sealed class KiasPeriodicScheduler(double intervalSeconds)
{
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(intervalSeconds);
    private readonly Dictionary<EntityUid, TimeSpan> _due = new();
    private readonly PriorityQueue<EntityUid, TimeSpan> _queue = new();

    public void Add(EntityUid uid, TimeSpan now)
    {
        if (_due.ContainsKey(uid)) return;
        var phase = (uint) uid.Id % 60 / 60.0;
        var due = now + _interval * phase;
        _due.Add(uid, due);
        _queue.Enqueue(uid, due);
    }

    public void Remove(EntityUid uid) => _due.Remove(uid);

    public bool TryDue(TimeSpan now, out EntityUid uid)
    {
        while (_queue.TryPeek(out uid, out var due) && due <= now)
        {
            _queue.Dequeue();
            if (!_due.TryGetValue(uid, out var current) || current != due) continue;
            var next = now + _interval;
            _due[uid] = next;
            _queue.Enqueue(uid, next);
            return true;
        }
        uid = default;
        return false;
    }
}
