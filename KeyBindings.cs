using Godot;
using System.Text.Json;

namespace TacticalRequests;

internal sealed record KeyChord(Key Code, bool Alt = false, bool Ctrl = false, bool Shift = false)
{
    public bool Matches(InputEventKey e) => !e.Echo && !e.MetaPressed && Code == (e.PhysicalKeycode == Key.None ? e.Keycode : e.PhysicalKeycode)
        && Alt == e.AltPressed && Ctrl == e.CtrlPressed && Shift == e.ShiftPressed;
    public override string ToString() => (Ctrl ? "Ctrl+" : "") + (Alt ? "Alt+" : "") + (Shift ? "Shift+" : "") + OS.GetKeycodeString(Code);
}

internal sealed class KeyBindings
{
    private const string Path = "user://tactical_requests_keys.json";
    public static readonly string[] Actions = ["选择回应", "回应：我来／同意", "回应：做不到／打打看", "回应：等一下", "自己的轮盘", "怪物轮盘", "查看队友", "单机测试", ..Enumerable.Range(1,8).Select(i => "轮盘第" + i + "格")];
    public Dictionary<string, KeyChord> Values = Defaults();
    private static Dictionary<string, KeyChord> Defaults()
    {
        Key[] codes = [Key.R, Key.Key1, Key.Key2, Key.Key3, Key.Q, Key.W, Key.H, Key.T, Key.Key1, Key.Key2, Key.Key3, Key.Key4, Key.Key5, Key.Key6, Key.Key7, Key.Key8];
        return Actions.Select((a,i) => (a, key: new KeyChord(codes[i], Ctrl: i < 8))).ToDictionary(x => x.a, x => x.key);
    }
    public KeyBindings()
    {
        try {
            if (!Godot.FileAccess.FileExists(Path)) return;
            using var file = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Read);
            var saved = JsonSerializer.Deserialize<Dictionary<string,KeyChord>>(file.GetAsText());
            if (saved == null || Actions.Any(a => !saved.ContainsKey(a)) || saved.Values.Any(v => v == null)
                || saved.Values.Distinct().Count() != Actions.Length || saved.Count != Actions.Length) return;
            foreach (var action in Actions) if (!Valid(action, saved[action], out _)) return;
            foreach (var action in Actions.Take(8))
            {
                var replacement = Values[action];
                var previous = saved[action];
                bool wasDefault = previous == new KeyChord(replacement.Code, Alt: true)
                    || action == "怪物轮盘" && previous == new KeyChord(Key.E, Alt: true);
                if (wasDefault && !saved.Any(pair => pair.Key != action && pair.Value == replacement))
                    saved[action] = replacement;
            }
            Values = saved;
        } catch { /* Invalid local settings recover defaults. */ }
    }
    public bool Is(string action, InputEventKey e) => Values[action].Matches(e);
    private static bool Valid(string action, KeyChord value, out string error)
    {
        error = "";
        if (!Enum.IsDefined(value.Code) || value.Code is Key.None or Key.Escape or Key.Alt or Key.Ctrl or Key.Shift or Key.Meta
            || value.Code == Key.F4 && value.Alt) error = "该按键保留，或不是有效按键";
        else if (!action.StartsWith("轮盘第") && !value.Alt && !value.Ctrl) error = "全局操作请使用 Alt 或 Ctrl 组合，避免误触出牌";
        else if (action.StartsWith("轮盘第") && (value.Alt || value.Ctrl || value.Shift)) error = "轮盘格使用单键；Shift+该键保留用于替换指令";
        return error.Length == 0;
    }
    public bool Replace(string action, KeyChord value, out string error)
    {
        if (!Valid(action,value,out error)) return false;
        var duplicate = Values.FirstOrDefault(p => p.Key != action && p.Value == value);
        if (duplicate.Key != null) { error = "与「" + duplicate.Key + "」冲突，请先改绑该项"; return false; }
        var next = new Dictionary<string,KeyChord>(Values) { [action] = value };
        return Save(next, out error);
    }
    public bool Reset(out string error) => Save(Defaults(), out error);
    private bool Save(Dictionary<string,KeyChord> next, out string error)
    {
        try {
            using var file = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Write);
            if (file == null) throw new IOException("无法写入设置");
            file.StoreString(JsonSerializer.Serialize(next));
            if (file.GetError() != Error.Ok) throw new IOException("设置写入失败");
            Values = next; error = "已保存，下次启动自动使用"; return true;
        } catch (Exception e) { error = "保存失败：" + e.Message; return false; }
    }
}
