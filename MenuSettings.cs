using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace TacticalRequests;

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
public static class MenuSettings
{
    public static void Postfix(NMainMenu __instance)
    {
        if (__instance.HasNode("TacticalKeySettings")) return;
        var layer = new CanvasLayer { Name = "TacticalKeySettings", Layer = 25 }; __instance.AddChild(layer);
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore }; layer.AddChild(root);
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _ = new KeySettingsDialog(root, new KeyBindings(), () => {}, () => {});
    }
}

[HarmonyPatch(typeof(NGame), nameof(NGame._Input))]
public static class SettingsInput
{
    public static bool Prefix(InputEvent __0)
    {
        if (CombatPanel.Active?.CloseOnEscape(__0) == true) return false;
        if (__0 is not InputEventKey { Pressed: true } key || KeySettingsDialog.Active?.Handle(key) != true) return true;
        ((SceneTree)Engine.GetMainLoop()).Root.GetViewport().SetInputAsHandled(); return false;
    }
}
