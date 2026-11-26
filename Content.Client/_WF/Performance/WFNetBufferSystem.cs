using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Client._WF.Performance;

/// <summary>Buffers more game states by default, so server tick hitches show less on the client.</summary>
public sealed partial class WFNetBufferSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;

    /// <summary>Game states the client keeps buffered unless the player picked their own.</summary>
    private const int DefaultBufferSize = 4;

    public override void Initialize()
    {
        base.Initialize();
        _cfg.OverrideDefault(CVars.NetBufferSize, DefaultBufferSize);
    }
}
