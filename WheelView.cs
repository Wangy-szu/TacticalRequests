using Godot;
using System.Text.Json;
using TacticalRequests.Core;

namespace TacticalRequests;

// Native Godot controls + draw callback: no custom C# scene registration required.
internal sealed class WheelView
{
    public readonly KeyBindings Keys = new();
    public int FontSize { get; private set; } = 28;
    public void SetFontSize(int size)
    {
        if (Rows.Theme != null && FontSize == size) return;
        FontSize = size;
        var theme = new Theme { DefaultFontSize = size };
        theme.SetFontSize("font_size", "Label", size);
        theme.SetFontSize("font_size", "Button", size);
        Rows.Theme = theme;
        foreach (var label in labels.GetChildren().OfType<Label>()) label.AddThemeFontSizeOverride("font_size", size);
        center.AddThemeFontSizeOverride("font_size", size);
    }
    public readonly Control Root = new() { Name = "WheelRoot", MouseFilter = Control.MouseFilterEnum.Ignore };
    public readonly Control Inbox = new() { Name = "Requests", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
    public readonly Control Rows = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly ColorRect backdrop = new() { Name = "WheelOverlay", Color = new Color(0, 0, 0, 0.12f), Visible = false };
    private readonly Control wheel = new() { Name = "Wheel", Size = new Vector2(440, 440), MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly Control labels = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly PanelContainer editor = new() { Name = "SlotEditor", Visible = false };
    private readonly Label center = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly WheelLoadout loadout;
    private readonly Action<Need> selected;
    private bool self;
    private bool slOnly;
    private int hovered = -1;
    private Need[] Slots => slOnly ? [Need.RequestSl] : self ? loadout.Self : loadout.Enemy;
    private static string SlotText(Need need) => need == Need.RequestSl ? "SL" : Needs.ShortText(need);
    public bool IsOpen => backdrop.Visible;
    internal bool SlOnly => slOnly;
    private const string SettingsPath = "user://tactical_requests_wheel.json";
    private static readonly Vector2 Center = new(220, 220);

    public WheelView(CanvasLayer layer, Action<Need> selected)
    {
        this.selected = selected;
        loadout = Load();
        layer.AddChild(Root); Root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Root.AddChild(Inbox); Inbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Inbox.AddChild(Rows); Rows.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Root.AddChild(backdrop); backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        backdrop.AddChild(wheel); wheel.AddChild(labels); wheel.AddChild(center);
        center.Position = Center - new Vector2(62, 48); center.Size = new Vector2(124, 96); center.AddThemeFontSizeOverride("font_size", 16);
        backdrop.AddChild(editor); editor.AddThemeStyleboxOverride("panel", BoxStyle());
        wheel.Draw += Draw;
        backdrop.GuiInput += Input;
    }
    internal static StyleBoxFlat BoxStyle() => new() { BgColor = new Color("18232af5"), BorderColor = new Color("b79561"),
        BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
        CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
        ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10 };

    public void Open(bool isSelf, string targetName, Vector2 position, bool slOnly = false)
    {
        this.slOnly = isSelf && slOnly;
        self = isSelf; hovered = -1; editor.Visible = false;
        var size = Root.GetViewportRect().Size;
        wheel.Position = new Vector2(Mathf.Clamp(position.X - 220, 8, Math.Max(8, size.X - 448)),
            Mathf.Clamp(position.Y - 220, 86, Math.Max(86, size.Y - 690)));
        center.Text = center.TooltipText = targetName;
        backdrop.Visible = true; RebuildLabels(); wheel.QueueRedraw();
    }
    public void Close() { backdrop.Visible = false; editor.Visible = false; }
    public bool HandleKey(InputEventKey key)
    {
        if (!IsOpen || !key.Pressed) return false;
        if (key.Keycode == Key.Escape)
        {
            if (editor.Visible) editor.Hide(); else Close();
            return true;
        }
        var code = key.PhysicalKeycode == Key.None ? key.Keycode : key.PhysicalKeycode;
        int slot = Array.FindIndex(KeyBindings.Actions.Skip(8).ToArray(), a => Keys.Values[a].Code == code);
        if (slot < 0 || slot > 8) return false;
        if (!key.Echo && !editor.Visible && slot < Slots.Length && !key.AltPressed && !key.CtrlPressed && !key.MetaPressed)
        {
            if (key.ShiftPressed) EditSlot(slot);
            else { selected(Slots[slot]); Close(); }
        }
        return true;
    }
    public void SetRequestCount(int count)
    {
        Inbox.Visible = count > 0;
    }
    private void RebuildLabels()
    {
        foreach (Node n in labels.GetChildren()) { labels.RemoveChild(n); n.QueueFree(); }
        for (int i = 0; i < Slots.Length; i++)
        {
            float angle = -Mathf.Pi / 2 + i * Mathf.Tau / Slots.Length;
            var position = Center + Vector2.FromAngle(angle) * 143 - new Vector2(54, 42);
            var label = new Label { Name = "Slot" + i, Text = $"{Keys.Values[KeyBindings.Actions[8+i]]}\n{SlotText(Slots[i])}", Position = position, Size = new Vector2(108, 84),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = Control.MouseFilterEnum.Ignore };
            label.AddThemeFontSizeOverride("font_size", FontSize); labels.AddChild(label);
        }
    }
    private int Sector(Vector2 position)
    {
        Vector2 delta = position - wheel.Position - Center;
        if (delta.Length() < 73 || delta.Length() > 205) return -1;
        float step = Mathf.Tau / Slots.Length;
        return (int)(Mathf.PosMod(delta.Angle() + Mathf.Pi / 2 + step / 2, Mathf.Tau) / step);
    }
    private void Input(InputEvent e)
    {
        if (!IsOpen) return;
        if (e is InputEventMouseMotion motion && !editor.Visible)
        {
            int next = Sector(motion.Position);
            if (next != hovered) { hovered = next; wheel.QueueRedraw(); }
        }
        if (e is not InputEventMouseButton { Pressed: true } mouse) return;
        if (editor.Visible) { editor.Hide(); return; }
        int slot = Sector(mouse.Position);
        if (mouse.ButtonIndex == MouseButton.Right && slot >= 0) { EditSlot(slot); return; }
        if (mouse.ButtonIndex == MouseButton.Left)
        {
            if (slot >= 0) selected(Slots[slot]);
            Close();
        }
        else if (mouse.ButtonIndex == MouseButton.Right) Close();
        backdrop.AcceptEvent();
    }
    private void Draw()
    {
        float step = Mathf.Tau / Slots.Length;
        for (int i = 0; i < Slots.Length; i++)
        {
            float start = -Mathf.Pi / 2 + i * step - step / 2 + 0.018f;
            float end = start + step - 0.036f;
            var vertices = new List<Vector2>();
            for (int j = 0; j <= 24; j++) vertices.Add(Center + Vector2.FromAngle(Mathf.Lerp(start, end, j / 24f)) * 205);
            for (int j = 24; j >= 0; j--) vertices.Add(Center + Vector2.FromAngle(Mathf.Lerp(start, end, j / 24f)) * 73);
            wheel.DrawColoredPolygon(vertices.ToArray(), new Color(i == hovered ? "536d74fa" : "17272ff5"));
            wheel.DrawArc(Center, 205, start, end, 24, new Color(i == hovered ? "ffdb91" : "b79561"), i == hovered ? 3 : 1.3f, true);
        }
        wheel.DrawCircle(Center, 68, new Color("101c23f5"));
        wheel.DrawArc(Center, 70, 0, Mathf.Tau, 80, new Color("b79561"), 1.5f, true);
    }
    private void EditSlot(int slot)
    {
        if (slOnly) return;
        foreach (Node n in editor.GetChildren()) { editor.RemoveChild(n); n.QueueFree(); }
        var list = new VBoxContainer(); editor.AddChild(list);
        list.AddChild(new Label { Text = $"替换：{SlotText(Slots[slot])}" });
        foreach (var need in self ? Needs.SelfPool : Needs.EnemyPool)
        {
            var button = new Button { Text = SlotText(need) + (Slots.Contains(need) ? " · 已在轮盘" : ""), TooltipText = Needs.Text(need) };
            list.AddChild(button);
            button.Pressed += () => {
                loadout.Replace(self, slot, need); Save(); editor.Hide(); RebuildLabels(); wheel.QueueRedraw();
            };
        }
        editor.Position = wheel.Position + new Vector2(105, 0); editor.Visible = true;
    }
    private static WheelLoadout Load()
    {
        try {
            using var file = Godot.FileAccess.Open(SettingsPath, Godot.FileAccess.ModeFlags.Read);
            var value = file == null ? new WheelLoadout() : JsonSerializer.Deserialize<WheelLoadout>(file.GetAsText()) ?? new();
            value.Validate(); return value;
        } catch { return new(); }
    }
    private void Save()
    {
        using var file = Godot.FileAccess.Open(SettingsPath, Godot.FileAccess.ModeFlags.Write);
        if (file != null) file.StoreString(JsonSerializer.Serialize(loadout));
        else GD.PushWarning("[TacticalRequests] Could not save wheel preferences.");
    }
}
