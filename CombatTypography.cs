using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace TacticalRequests;

internal sealed partial class CombatPanel
{
    private void SyncCombatFont()
    {
        var button = ui.FindChildren("*", "Control", true, false).OfType<NEndTurnButton>().FirstOrDefault();
        var label = button?.GetNodeOrNull<Label>("Visuals/Label");
        if (label == null) return;
        int sourceSize = label.LabelSettings?.FontSize ?? label.GetThemeFontSize("font_size");
        float sourceScale = label.GetGlobalTransformWithCanvas().Y.Length();
        float targetScale = view.Rows.GetGlobalTransformWithCanvas().Y.Length();
        int size = Math.Max(1, (int)Math.Round(sourceSize * sourceScale / Math.Max(0.01f, targetScale)));
        view.SetFontSize(size);
        if (requestNotice != null) requestNotice.AddThemeFontSizeOverride("font_size", size);
    }
}
