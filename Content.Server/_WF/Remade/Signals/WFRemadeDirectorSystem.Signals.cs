using Content.Server._WF.Remade.Monolith;
using Robust.Shared.Map;

namespace Content.Server._WF.Remade.Director;

/// <summary>Hooks for the Signals into the Director's tricks that are private to it.</summary>
public sealed partial class WFRemadeDirectorSystem
{
    /// <summary>Flickers the lights near a crewman, as the Director does.</summary>
    public void SignalFlicker(EntityUid crewman)
    {
        FlickerNear(crewman);
    }

    /// <summary>Plays a Remade's voice somewhere near a crewman, only for them. Returns false if the stone has no voice to give.</summary>
    public bool SignalFalseSound(EntityUid crewman, EntityUid stone)
    {
        if (!TryComp<WFMonolithComponent>(stone, out var monolith))
            return false;

        var director = TryComp<WFRemadeDirectorComponent>(stone, out var found) ? found : new WFRemadeDirectorComponent();
        var offset = _random.NextAngle().ToVec() * _random.NextFloat(6f, 10f);
        _audio.PlayStatic(FalseSoundFor(director, monolith.Stage), crewman, Transform(crewman).Coordinates.Offset(offset));
        return true;
    }
}
