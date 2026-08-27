using Content.Goobstation.Shared.StationRadio.Components;
using Content.Shared.DeviceLinking;

namespace Content.Shared._Forge.StationRadio;

public static class StationRadioFrequencyHelpers
{
    public static bool TryGetBroadcastFrequency(IEntityManager entities, EntityUid vinylPlayer, out int frequency)
    {
        frequency = 1285;

        if (!entities.TryGetComponent(vinylPlayer, out DeviceLinkSourceComponent? source))
            return false;

        foreach (var linked in source.LinkedPorts.Keys)
        {
            if (!entities.TryGetComponent(linked, out RadioRigComponent? rig))
                continue;

            if (!entities.TryGetComponent(linked, out DeviceLinkSinkComponent? sink))
                continue;

            var hasServer = false;
            foreach (var linkedSource in sink.LinkedSources)
            {
                if (entities.HasComponent<StationRadioServerComponent>(linkedSource))
                {
                    hasServer = true;
                    break;
                }
            }

            if (!hasServer)
                continue;

            frequency = rig.Frequency;
            return true;
        }

        return false;
    }
}
