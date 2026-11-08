using Godot;

namespace TacticalRequests;

internal sealed partial class CombatPanel
{
    private readonly HashSet<Key> observedShortcutKeys = new();
    private readonly HashSet<Key> polledShortcutKeys = new();
    private bool resetShortcutKeys = true;

    private static bool IsShortcutKeyPressed(Key code) => code is Key.Alt or Key.Ctrl or Key.Shift or Key.Meta
        ? Input.IsKeyPressed(code) : Input.IsPhysicalKeyPressed(code);

    private void PollShortcuts()
    {
        if (disposed) return;
        PollShortcutState(window.HasFocus(), IsShortcutKeyPressed);
    }

    private void PollShortcutState(bool focused, Func<Key, bool> pressed)
    {
        if (!focused)
        {
            observedShortcutKeys.Clear();
            polledShortcutKeys.Clear();
            reservedShortcutKeys.Clear();
            resetShortcutKeys = true;
            return;
        }
        foreach (var code in view.Keys.Values.Values.Select(chord => chord.Code).Distinct())
        {
            if (!pressed(code))
            {
                observedShortcutKeys.Remove(code);
                polledShortcutKeys.Remove(code);
                reservedShortcutKeys.Remove(code);
                continue;
            }
            if (!observedShortcutKeys.Add(code) || resetShortcutKeys) continue;
            var key = new InputEventKey
            {
                Pressed = true, PhysicalKeycode = code, Keycode = code,
                AltPressed = pressed(Key.Alt), CtrlPressed = pressed(Key.Ctrl),
                ShiftPressed = pressed(Key.Shift), MetaPressed = pressed(Key.Meta)
            };
            if (!CloseOnEscape(key)) continue;
            polledShortcutKeys.Add(code);
            GD.Print($"[TacticalRequests] physical shortcut {key.AsTextPhysicalKeycode()}: wheel={view.IsOpen}, reply={replySelection}");
        }
        resetShortcutKeys = false;
    }
}
