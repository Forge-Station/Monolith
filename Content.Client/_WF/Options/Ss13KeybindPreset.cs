using System.Linq;
using Content.Shared.Input;
using Robust.Client.Input;
using Robust.Shared.Input;
using static Robust.Client.Input.Keyboard;

namespace Content.Client._WF.Options;

/// <summary>The SS13 hotkey layout, for every action that has a counterpart here.</summary>
public static class Ss13KeybindPreset
{
    /// <summary>Bindings the preset sets. A function listed here loses its stock keys.</summary>
    public static readonly (BoundKeyFunction Function, Key Key, Key Mod)[] Binds =
    {
        (EngineKeyFunctions.MoveUp, Key.W, Key.Unknown),
        (EngineKeyFunctions.MoveUp, Key.Up, Key.Unknown),
        (EngineKeyFunctions.MoveLeft, Key.A, Key.Unknown),
        (EngineKeyFunctions.MoveLeft, Key.Left, Key.Unknown),
        (EngineKeyFunctions.MoveDown, Key.S, Key.Unknown),
        (EngineKeyFunctions.MoveDown, Key.Down, Key.Unknown),
        (EngineKeyFunctions.MoveRight, Key.D, Key.Unknown),
        (EngineKeyFunctions.MoveRight, Key.Right, Key.Unknown),
        (EngineKeyFunctions.Walk, Key.Alt, Key.Unknown),
        (ContentKeyFunctions.ToggleStanding, Key.U, Key.Unknown),

        (ContentKeyFunctions.ThrowItemInHand, Key.R, Key.Unknown),
        (ContentKeyFunctions.ThrowItemInHand, Key.End, Key.Unknown),
        (ContentKeyFunctions.ReleasePulledObject, Key.H, Key.Unknown),
        (ContentKeyFunctions.ReleasePulledObject, Key.Delete, Key.Unknown),
        (ContentKeyFunctions.WFOfferItem, Key.G, Key.Unknown),
        (ContentKeyFunctions.AltUseItemInHand, Key.Z, Key.Control),
        (ContentKeyFunctions.SmartEquipSuitStorage, Key.Q, Key.Shift),
        (ContentKeyFunctions.SmartEquipPocket1, Key.Num1, Key.Control),
        (ContentKeyFunctions.SmartEquipPocket2, Key.Num2, Key.Control),

        (ContentKeyFunctions.FocusOOC, Key.O, Key.Unknown),
        (ContentKeyFunctions.FocusEmote, Key.M, Key.Unknown),
        (ContentKeyFunctions.FocusLOOC, Key.L, Key.Unknown),
        (ContentKeyFunctions.FocusWhisperChat, Key.Y, Key.Unknown),
        (ContentKeyFunctions.FocusAdminChat, Key.F3, Key.Unknown),
        (ContentKeyFunctions.FocusDeadChat, Key.F10, Key.Unknown),

        (EngineKeyFunctions.HideUI, Key.F12, Key.Unknown),
        (EngineKeyFunctions.WindowCloseAll, Key.Home, Key.Unknown),

        (ContentKeyFunctions.TargetHead, Key.NumpadNum8, Key.Unknown),
        (ContentKeyFunctions.TargetTorso, Key.NumpadNum5, Key.Unknown),
        (ContentKeyFunctions.TargetRightArm, Key.NumpadNum4, Key.Unknown),
        (ContentKeyFunctions.TargetLeftArm, Key.NumpadNum6, Key.Unknown),
        (ContentKeyFunctions.TargetRightLeg, Key.NumpadNum1, Key.Unknown),
        (ContentKeyFunctions.TargetLeftLeg, Key.NumpadNum3, Key.Unknown),

        // Stock keys the layout above takes, moved somewhere free.
        (ContentKeyFunctions.OpenLanguageMenu, Key.J, Key.Unknown),
        (ContentKeyFunctions.OpenEmotesMenu, Key.N, Key.Unknown),
        (ContentKeyFunctions.OpenCraftingMenu, Key.G, Key.Shift),
        (EngineKeyFunctions.ShowDebugMonitors, Key.F3, Key.Shift),
        (ContentKeyFunctions.RotateObjectClockwise, Key.R, Key.Control),
        (EngineKeyFunctions.CameraRotateLeft, Key.NumpadNum7, Key.Control),
        (EngineKeyFunctions.CameraRotateRight, Key.NumpadNum9, Key.Control),
        (EngineKeyFunctions.CameraReset, Key.NumpadNum8, Key.Control),
        (ContentKeyFunctions.ZoomOut, Key.NumpadNum4, Key.Control),
        (ContentKeyFunctions.ZoomIn, Key.NumpadNum6, Key.Control),
        (ContentKeyFunctions.ResetZoom, Key.NumpadNum5, Key.Control),
    };

    /// <summary>Functions left with no key. Escape still opens the escape menu.</summary>
    public static readonly BoundKeyFunction[] Unbound =
    {
        EngineKeyFunctions.EscapeMenu,
    };

    /// <summary>Functions bound as a toggle rather than a hold.</summary>
    private static readonly BoundKeyFunction[] Toggles =
    {
        EngineKeyFunctions.HideUI,
        EngineKeyFunctions.ShowDebugMonitors,
    };

    /// <summary>Resets every binding to stock, then applies the preset. Doesn't save.</summary>
    public static void Apply(IInputManager input, bool toggleWalk)
    {
        input.ResetAllBindings();

        foreach (var function in Binds.Select(b => b.Function).Concat(Unbound).Distinct())
        {
            foreach (var binding in input.GetKeyBindings(function).ToArray())
            {
                input.RemoveBinding(binding);
            }
        }

        foreach (var (function, key, mod) in Binds)
        {
            var toggle = Toggles.Contains(function) || toggleWalk && function == EngineKeyFunctions.Walk;
            input.RegisterBinding(new KeyBindingRegistration
            {
                Function = function,
                BaseKey = key,
                Mod1 = mod,
                Type = toggle ? KeyBindingType.Toggle : KeyBindingType.State,
            });
        }
    }
}
