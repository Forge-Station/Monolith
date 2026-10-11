#nullable enable

using System.Reflection;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.CombatConsole;

/// <summary>Dispatches real button press/release handling without bypassing disabled or toggle behavior.</summary>
internal static class WFButtonTestInput
{
    public static void Click(BaseButton button)
    {
        Assert.That(button.VisibleInTree, Is.True, $"{button.Name} must be visible before clicking.");
        Assert.That(button.Width, Is.GreaterThan(0), $"{button.Name} must be laid out before clicking.");
        Assert.That(button.Height, Is.GreaterThan(0), $"{button.Name} must be laid out before clicking.");
        var local = button.Size / 2;
        var pointer = new ScreenCoordinates(button.GlobalPixelPosition + local * button.UIScale, button.Window!.Id);
        foreach (var state in new[] { BoundKeyState.Down, BoundKeyState.Up })
        {
            var args = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state, pointer, true,
                local, local * button.UIScale);
            // The UI manager exposes no public input dispatch; retain the engine's complete button path.
            button.GetType().GetMethod(state == BoundKeyState.Down ? "KeyBindDown" : "KeyBindUp",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, new object[] { args });
        }
    }

    public static void Toggle(BaseButton button, bool pressed)
    {
        if (button.Pressed == pressed)
            return;
        Click(button);
        Assert.That(button.Pressed, Is.EqualTo(pressed), $"{button.Name} did not accept the requested toggle.");
    }
}
