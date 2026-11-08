using Godot;

namespace TacticalRequests;

internal sealed class KeySettingsDialog
{
    private static Button AddButton(HBoxContainer parent, string text, Action action) { var button = new Button { Text = text }; parent.AddChild(button); button.Pressed += action; return button; }
    private KeyChord? pendingConflict;
    private Button allowConflict = null!;
    public Control Root = null!;
    public static KeySettingsDialog? Active;
    private readonly KeyBindings keys;
    private readonly Action changed;
    private readonly Action opening;
    public string? capturingKey;
    private Label keyNotice = null!;
    public readonly Dictionary<string,Button> keyButtons = new();
    public bool IsOpen => Root.Visible;
    public KeySettingsDialog(Control parent, KeyBindings keys, Action changed, Action opening, bool besideDrawPile = false)
    {
        this.keys = keys; this.changed = changed; this.opening = opening;
        var entry = new Button { Name = "KeySettingsButton", Text = "按键设置" }; parent.AddChild(entry);
        Root = new ColorRect { Name = "KeySettings", Color = new Color(0,0,0,0.6f), Visible = false };
        parent.AddChild(Root); Root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var panel = new PanelContainer { Position = new Vector2(650,120), CustomMinimumSize = new Vector2(620,0) };
        panel.AddThemeStyleboxOverride("panel", WheelView.BoxStyle()); Root.AddChild(panel);
        var body = new VBoxContainer(); panel.AddChild(body);
        body.AddChild(new Label { Text = "快捷键设置 · 点击右侧按钮后按新组合键" });
        keyNotice = new Label { Text = "全局操作使用 Alt/Ctrl；Esc 取消录入或关闭。设置仅影响本机。", AutowrapMode = TextServer.AutowrapMode.WordSmart }; body.AddChild(keyNotice);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(590,500) }; body.AddChild(scroll);
        void Layout() { var size = parent.GetViewportRect().Size; entry.Position = new Vector2(besideDrawPile ? 130 : 12, size.Y - 52); panel.Position = new Vector2(Math.Max(12,(size.X - 620)/2),Math.Max(82,(size.Y - 650)/2)); scroll.CustomMinimumSize = new Vector2(590,Math.Clamp(size.Y - 300,200,500)); }
        Root.Resized += Layout; Layout();
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; scroll.AddChild(list);
        list.AddChild(new Label {
            Name = "WheelInstructions",
            Text = "轮盘操作说明\n点击选项或按对应格子的按键发送。鼠标右键选项，或按 Shift＋该格按键，可替换指令。点击轮盘中心、外部空白或按 Esc 关闭。\n下方可修改自己的轮盘、鼠标下怪物轮盘、回应及各格按键。",
            CustomMinimumSize = new Vector2(560, 0),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        });
        foreach (var action in KeyBindings.Actions)
        {
            if (action == "单机测试" && !BuildFeatures.SoloTestingEnabled) continue;
            var row = new HBoxContainer(); list.AddChild(row);
            row.AddChild(new Label { Text = action, CustomMinimumSize = new Vector2(300,0) });
            var button = AddButton(row, keys.Values[action].ToString(), () => { pendingConflict = null; allowConflict?.Hide(); capturingKey = action; keyNotice.Text = "请按「" + action + "」的新按键；Esc 取消"; });
            button.CustomMinimumSize = new Vector2(230,0); keyButtons[action] = button;
        }
        var footer = new HBoxContainer(); body.AddChild(footer);
        allowConflict = AddButton(footer, "仍然使用此按键", () => {
            if (capturingKey != null && pendingConflict != null) {
                if (keys.Replace(capturingKey,pendingConflict,out var message)) { capturingKey = null; RefreshLabels(); }
                keyNotice.Text = message;
            }
            pendingConflict = null; allowConflict.Hide();
        });
        allowConflict.Hide();
        AddButton(footer, "恢复默认", () => { pendingConflict = null; allowConflict.Hide(); capturingKey = null; keys.Reset(out var message); keyNotice.Text = message; RefreshLabels(); });
        AddButton(footer, "关闭", () => { Close(); });
        entry.Pressed += Open;
        Root.TreeExiting += () => { if (ReferenceEquals(Active,this)) Active = null; };
    }
    public void Open() { opening(); capturingKey = null; pendingConflict = null; allowConflict.Hide(); Active = this; Root.Show(); }
    public void Close() { capturingKey = null; pendingConflict = null; allowConflict.Hide(); Root.Hide(); if (ReferenceEquals(Active,this)) Active = null; }
    private void RefreshLabels() { foreach (var pair in keyButtons) pair.Value.Text = keys.Values[pair.Key].ToString(); changed(); }
    public bool Handle(InputEventKey key)
    {
        if (!IsOpen) return false;
        if (key.Echo) return true;
        if (key.Keycode == Key.Escape) { if (capturingKey != null) { capturingKey = null; pendingConflict = null; allowConflict.Hide(); keyNotice.Text = "已取消录入"; } else Close(); return true; }
        var code = key.PhysicalKeycode == Key.None ? key.Keycode : key.PhysicalKeycode;
        if (capturingKey == null || code is Key.Alt or Key.Ctrl or Key.Shift or Key.Meta) return true;
        if (key.MetaPressed) { keyNotice.Text = "暂不支持 Meta 组合"; return true; }
        var chord = new KeyChord(code,key.AltPressed,key.CtrlPressed,key.ShiftPressed);
        var collisions = InputMap.GetActions().Where(a => InputMap.ActionGetEvents(a).OfType<InputEventKey>().Any(e =>
            (e.PhysicalKeycode == Key.None ? e.Keycode : e.PhysicalKeycode) == code && e.AltPressed == key.AltPressed && e.CtrlPressed == key.CtrlPressed && e.ShiftPressed == key.ShiftPressed)).Select(a => a.ToString()).Take(3).ToArray();
        if (collisions.Length > 0) { pendingConflict = chord; allowConflict.Show(); keyNotice.Text = "与已注册操作冲突：" + string.Join("、",collisions) + "。可换键，或点击仍然使用。"; return true; }
        pendingConflict = null; allowConflict.Hide();
        if (keys.Replace(capturingKey, chord, out var message)) { capturingKey = null; RefreshLabels(); }
        keyNotice.Text = message; return true;
    }
}
