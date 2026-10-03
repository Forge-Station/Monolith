// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 ImHoks <142083149+ImHoks@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 ImHoks <imhokzzzz@gmail.com>
// SPDX-FileCopyrightText: 2025 KillanGenifer <killangenifer@gmail.com>
// SPDX-FileCopyrightText: 2025 gluesniffler <159397573+gluesniffler@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._CorvaxNext.Silicons.Borgs.Components;
using Content.Shared.Actions;
using Content.Shared.Mind;
using Content.Shared.Mind.Components; // Forge - change
using Content.Shared.Radio.Components; // Forge - change
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Serialization;

namespace Content.Shared._CorvaxNext.Silicons.Borgs;

public abstract partial class SharedAiRemoteControlSystem : EntitySystem
{
    [Dependency] private SharedStationAiSystem _stationAiSystem = default!;
    [Dependency] private SharedTransformSystem _xformSystem = default!;
    [Dependency] private SharedMindSystem _mind = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AiRemoteControllerComponent, MindUnvisitedMessage>(OnMindUnvisited); // Forge - change
    }

    // Forge - change
    protected virtual void OnMindUnvisited(EntityUid uid, AiRemoteControllerComponent component, MindUnvisitedMessage args)
    {
        ReturnMindIntoAi(uid);
    }

    public void ReturnMindIntoAi(EntityUid entity)
    {
        if (!TryComp<AiRemoteControllerComponent>(entity, out var remoteComp))
            return;

        if (remoteComp?.AiHolder == null
            || !_stationAiSystem.TryGetCore(remoteComp.AiHolder.Value, out var stationAiCore)
            || stationAiCore.Comp?.RemoteEntity == null)
            return;

        if (remoteComp.LinkedMind == null)
            return;

        if (!TryComp<StationAiHeldComponent>(remoteComp.AiHolder.Value, out var stationAiHeldComp))
            return;

        stationAiHeldComp.CurrentConnectedEntity = null;

        // Forge - change: clear the link before UnVisit raises MindUnvisitedMessage.
        var mind = remoteComp.LinkedMind.Value;
        remoteComp.AiHolder = null;
        remoteComp.LinkedMind = null;
        if (TryComp(entity, out IntrinsicRadioTransmitterComponent? transmitter) &&
            remoteComp.PreviouslyTransmitterChannels != null)
            transmitter.Channels = [.. remoteComp.PreviouslyTransmitterChannels];
        if (TryComp(entity, out ActiveRadioComponent? radio) &&
            remoteComp.PreviouslyActiveRadioChannels != null)
            radio.Channels = [.. remoteComp.PreviouslyActiveRadioChannels];
        _mind.UnVisit(mind);

        _stationAiSystem.SwitchRemoteEntityMode(stationAiCore, true);

        _xformSystem.SetCoordinates(stationAiCore.Comp.RemoteEntity.Value, Transform(entity).Coordinates);
    }
}

public sealed partial class ReturnMindIntoAiEvent : InstantActionEvent
{
}

public sealed partial class ToggleRemoteDevicesScreenEvent : InstantActionEvent
{
}

[Serializable, NetSerializable]
public enum RemoteDeviceUiKey : byte
{
    Key
}
