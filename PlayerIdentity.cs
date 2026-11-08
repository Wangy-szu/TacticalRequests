using Godot;

namespace TacticalRequests;

internal sealed partial class CombatPanel
{
    private static readonly Color[] playerColors = [new("85bfff"),new("ffc078"),new("d4a0ff"),new("7de0b3")];
    internal static Color PlayerSlotColor(int index) => index >= 0 && index < playerColors.Length ? playerColors[index] : new Color("c6ced8");
    private int PlayerIndex(ulong id) => testing && id == TestPeer ? 1 : state?.Players.ToList().FindIndex(p => p.NetId == id) ?? -1;
    private Color PlayerColor(ulong id) => PlayerSlotColor(PlayerIndex(id));
    private void AddAuthorColors(HBoxContainer row, IEnumerable<ulong> authors, Action select)
    {
        var ids=authors.Distinct().OrderBy(id => PlayerIndex(id)).ToArray();
        if (ids.Length < 2) return;
        var tags=new HBoxContainer { Name="RequestAuthors",MouseFilter=Control.MouseFilterEnum.Ignore };
        tags.AddThemeConstantOverride("separation",1); row.AddChild(tags);
        foreach (ulong id in ids) {
            int index=PlayerIndex(id);
            var label=new Label { Text=index >= 0 ? $"{index+1}" : "?",Modulate=PlayerColor(id),CustomMinimumSize=new Vector2(14,0),TooltipText=PlayerName(id),MouseFilter=Control.MouseFilterEnum.Stop };
            tags.AddChild(label);
            label.GuiInput += input => { if(input is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.Left}) { select(); label.AcceptEvent(); } };
        }
    }
}
