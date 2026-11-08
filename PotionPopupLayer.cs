using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Potions;

namespace TacticalRequests;

[HarmonyPatch(typeof(NPotionPopup), nameof(NPotionPopup._Ready))]
internal static class PotionPopupLayer
{
    static void Postfix(NPotionPopup __instance)
    {
        // Keep the popup attached to its holder so its lifecycle and input stay intact.
        var visuals = __instance.GetChildren().OfType<Control>().ToArray();
        var layer = new CanvasLayer { Name = "TacticalPotionPriority", Layer = 30,
            Transform = __instance.GetGlobalTransformWithCanvas() };
        __instance.AddChild(layer);
        var bounds = new Control { Name = "PopupBounds", Size = __instance.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(bounds);
        foreach (var control in visuals) control.Reparent(bounds, false);
    }
}
