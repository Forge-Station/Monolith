using Content.Shared._Goobstation.Research;
using Robust.Shared.Serialization;

namespace Content.Shared.Research.Components
{
    [NetSerializable, Serializable]
    public enum ResearchConsoleUiKey : byte
    {
        Key,
    }

    [Serializable, NetSerializable]
    public sealed class ConsoleUnlockTechnologyMessage : BoundUserInterfaceMessage
    {
        public string Id;

        public ConsoleUnlockTechnologyMessage(string id)
        {
            Id = id;
        }
    }

    [Serializable, NetSerializable]
    public sealed class ConsoleServerSelectionMessage : BoundUserInterfaceMessage
    {

    }

    [Serializable, NetSerializable]
    public sealed class ResearchConsoleBoundInterfaceState : BoundUserInterfaceState
    {
        public int Points;

        /// <summary>
        /// Goobstation field - all researches and their availablities
        /// </summary>
        public Dictionary<string, ResearchAvailability> Researches;

        /// <summary>
        /// Progress (0–100) toward unlocking each technology (prerequisites or research points).
        /// </summary>
        public Dictionary<string, byte> Progress;

        public ResearchConsoleBoundInterfaceState(
            int points,
            Dictionary<string, ResearchAvailability> researches,
            Dictionary<string, byte> progress)
        {
            Points = points;
            Researches = researches;
            Progress = progress;
        }
    }
}
