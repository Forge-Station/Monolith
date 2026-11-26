// New Frontiers - This file is licensed under AGPLv3
// Copyright (c) 2024 New Frontiers Contributors
// See AGPLv3.txt for details.
using Content.Shared._NF.Shuttles.Events;

namespace Content.Client.Shuttles.UI
{
    public sealed partial class ShuttleConsoleWindow
    {
        public event Action<NetEntity?, InertiaDampeningMode>? OnInertiaDampeningModeChanged;
        public event Action<float?>? OnMaxShuttleSpeedChanged;
        public event Action<string, string>? OnNetworkPortButtonPressed;

        private void NfInitialize()
        {
            NavContainer.OnInertiaDampeningModeChanged += (entity, mode) =>
            {
                OnInertiaDampeningModeChanged?.Invoke(entity, mode);
            };

            NavContainer.OnMaxShuttleSpeedChanged += (maxSpeed) =>
            {
                OnMaxShuttleSpeedChanged?.Invoke(maxSpeed);
            };

            NavContainer.OnNetworkPortButtonPressed += (sourcePort, targetPort) =>
            {
                OnNetworkPortButtonPressed?.Invoke(sourcePort, targetPort);
            };

            NavContainer.OnAddCoordinateMarker += (x, y) => OnAddCoordinateMarker?.Invoke(x, y);
            NavContainer.OnRemoveMarker += id => OnRemoveMarker?.Invoke(id);
            MapContainer.OnTrackEntity += entity => OnTrackEntity?.Invoke(entity);
            MapContainer.OnRemoveMarker += id => OnRemoveMarker?.Invoke(id);
        }

        public event Action<float, float>? OnAddCoordinateMarker;
        public event Action<int>? OnRemoveMarker;
        public event Action<NetEntity>? OnTrackEntity;
    }
}
