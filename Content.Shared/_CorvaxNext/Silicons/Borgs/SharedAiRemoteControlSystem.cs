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
        SubscribeLocalEvent<StationAiHeldComponent, EntityTerminatingEvent>(OnHeldAiTerminating); // Forge - change
    }

    // Forge - change
    protected virtual void OnMindUnvisited(EntityUid uid, AiRemoteControllerComponent component, MindUnvisitedMessage args)
    {
        ReturnMindIntoAi(uid);
    }

    /// <summary>
    ///     Forge - change: if the AI brain itself is destroyed while it is piloting something, nothing
    ///     un-visits the controlled entity (SharedMindSystem only reacts to the *visited* entity
    ///     terminating), so without this the borg would stay activated, powered and stuck on the AI's
    ///     radio channels forever.
    /// </summary>
    private void OnHeldAiTerminating(EntityUid uid, StationAiHeldComponent component, ref EntityTerminatingEvent args)
    {
        if (component.CurrentConnectedEntity is { } controlled)
            ReleaseRemoteControl(controlled);
    }

    /// <summary>
    ///     Forge - change: restores the controlled entity to its pre-takeover state and drops the link.
    ///     Deliberately independent of the AI core, so it still works when the core or brain is gone.
    /// </summary>
    /// <returns>True if an AI link was actually released.</returns>
    public bool ReleaseRemoteControl(EntityUid entity)
    {
        if (!TryComp<AiRemoteControllerComponent>(entity, out var remoteComp) ||
            remoteComp.AiHolder == null ||
            remoteComp.LinkedMind == null)
        {
            return false;
        }

        if (TryComp<StationAiHeldComponent>(remoteComp.AiHolder.Value, out var stationAiHeldComp))
            stationAiHeldComp.CurrentConnectedEntity = null;

        // Clear the link before UnVisit raises MindUnvisitedMessage, so the re-entrant call bails.
        var mind = remoteComp.LinkedMind.Value;
        remoteComp.AiHolder = null;
        remoteComp.LinkedMind = null;

        if (TryComp(entity, out IntrinsicRadioTransmitterComponent? transmitter) &&
            remoteComp.PreviouslyTransmitterChannels != null)
            transmitter.Channels = [.. remoteComp.PreviouslyTransmitterChannels];
        if (TryComp(entity, out ActiveRadioComponent? radio) &&
            remoteComp.PreviouslyActiveRadioChannels != null)
            radio.Channels = [.. remoteComp.PreviouslyActiveRadioChannels];

        remoteComp.PreviouslyTransmitterChannels = null;
        remoteComp.PreviouslyActiveRadioChannels = null;

        _mind.UnVisit(mind);

        OnAiReleased(entity);
        return true;
    }

    /// <summary>
    ///     Forge - change: called only when an AI link was genuinely released, so server-side teardown
    ///     (deactivating the chassis) can't fire for unrelated minds that merely stopped visiting.
    /// </summary>
    protected virtual void OnAiReleased(EntityUid entity)
    {
    }

    public void ReturnMindIntoAi(EntityUid entity)
    {
        if (!TryComp<AiRemoteControllerComponent>(entity, out var remoteComp) || remoteComp.AiHolder == null)
            return;

        // Forge - change: resolve the core up front, but don't let a missing core abort the release -
        // that used to leave the entity permanently linked and activated.
        var hasCore = _stationAiSystem.TryGetCore(remoteComp.AiHolder.Value, out var stationAiCore)
                      && stationAiCore.Comp?.RemoteEntity != null;
        var returnCoordinates = Transform(entity).Coordinates;

        if (!ReleaseRemoteControl(entity))
            return;

        if (!hasCore)
            return;

        _stationAiSystem.SwitchRemoteEntityMode(stationAiCore, true);
        _xformSystem.SetCoordinates(stationAiCore.Comp!.RemoteEntity!.Value, returnCoordinates);
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
