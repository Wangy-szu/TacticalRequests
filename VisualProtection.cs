using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace TacticalRequests;

[HarmonyPatch(typeof(NDamageNumVfx), nameof(NDamageNumVfx._Ready))]
internal static class VisualProtection
{
    private static readonly HashSet<NDamageNumVfx> damage = new();
    static void Postfix(NDamageNumVfx __instance) => damage.Add(__instance);
    internal static Rect2[] DamageBounds()
    {
        damage.RemoveWhere(n => !GodotObject.IsInstanceValid(n) || !n.IsInsideTree());
        return damage.Where(n => n.IsVisibleInTree() && n.Modulate.A > 0.1f)
            .Select(n => n.GetNodeOrNull<Control>("Label")).Where(n => n != null)
            .Select(n => CalloutPlacement.Bounds(n!).Grow(12)).ToArray();
    }
}
