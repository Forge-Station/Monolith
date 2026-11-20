using Content.Server.GameTicking;
using Content.Shared._WF.Xenonids.Name;
using Content.Shared.GameTicking;
using Content.Shared.NameModifier.EntitySystems;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._WF.Xenonids.Name;

public sealed partial class XenoNameSystem : SharedXenoNameSystem
{

    [Dependency] private NameModifierSystem _nameModifier = default!;
    [Dependency] private IRobustRandom _random = default!;

    private readonly List<int> _available = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _available.Clear();
        for (var i = 1; i < 1000; i++)
        {
            _available.Add(i);
        }
    }

    public override void SetupName(EntityUid xeno)
    {
        base.SetupName(xeno);

        if (!TryComp(xeno, out ActorComponent? actor))
            return;

        try
        {
            // WOLFGATE: profiles here have no xeno prefix/postfix; names keep RMC's default prefix
            var name = EnsureComp<XenoNameComponent>(xeno);
            name.Number = _available.Count == 0 ? _random.Next(1, 1000) : _random.PickAndTake(_available);

            _nameModifier.RefreshNameModifiers(xeno);
            RemCompDeferred<AssignXenoNameComponent>(xeno);
        }
        catch (Exception e)
        {
            Log.Error($"Error setting up xeno name for {ToPrettyString(xeno)}:\n{e}");
        }
    }
}
