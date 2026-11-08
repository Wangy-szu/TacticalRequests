using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace TacticalRequests;

internal sealed partial class CombatPanel
{
    private readonly HashSet<Key> reservedShortcutKeys = new();

    internal bool InterceptStockShortcut(InputEvent input)
    {
        if (input is not InputEventKey key || !CanUseCombatOverlay()
            || ui.GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return false;
        var code = key.PhysicalKeycode == Key.None ? key.Keycode : key.PhysicalKeycode;
        bool reserved = reservedShortcutKeys.Contains(code);
        bool bound = KeyBindings.Actions.Take(8).Any(action =>
        {
            var chord = view.Keys.Values[action];
            return chord.Code == code && chord.Alt == key.AltPressed && chord.Ctrl == key.CtrlPressed
                && chord.Shift == key.ShiftPressed && !key.MetaPressed;
        });
        bool legacyEnemyKey = code == Key.E && key.AltPressed && !key.CtrlPressed && !key.ShiftPressed && !key.MetaPressed;
        if (!reserved && !bound && !legacyEnemyKey) return false;
        if (!key.Pressed)
        {
            reservedShortcutKeys.Remove(code);
            CloseOnEscape(key);
        }
        else
        {
            reservedShortcutKeys.Add(code);
            if (!key.Echo && !CloseOnEscape(key) && legacyEnemyKey)
            {
                Presentation.NoticeRequest = null;
                Presentation.NoticeText = "怪物轮盘快捷键为 " + view.Keys.Values["怪物轮盘"];
                Presentation.NoticeColor = new Color("ffd07d");
                Presentation.NoticeUntil = Now + 4;
                UpdateRequestNotice();
            }
        }
        ui.GetViewport().SetInputAsHandled();
        return true;
    }
}

[HarmonyPatch(typeof(NPlayerHand), nameof(NPlayerHand._UnhandledInput))]
public static class GuardCardShortcuts
{
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(InputEvent __0) => CombatPanel.Active?.InterceptStockShortcut(__0) != true;
}

[HarmonyPatch(typeof(NHotkeyManager), nameof(NHotkeyManager._UnhandledInput))]
public static class GuardGameShortcuts
{
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(InputEvent __0) => CombatPanel.Active?.InterceptStockShortcut(__0) != true;
}
