namespace Content.Server._CE.ZLevels.Core;

public sealed partial class CEZLevelsSystem
{
    /// <summary>Forces the next gravity sweep to rebuild the pooled lift caches instead of waiting.</summary>
    public void WfInvalidateGravgenCapacity()
    {
        _nextGravityCheckTime = TimeSpan.Zero;
    }
}
