using Godot;
using System.Text.Json;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Logging;
using TacticalRequests.Core;

namespace TacticalRequests;

public sealed class TacticalChatMessage : INetMessage
{
    public string Run { get; set; } = "";
    public string Text { get; set; } = "";
    public string Tag { get; set; } = "";
    public ulong Sender { get; set; }
    public int Sequence { get; set; }
    public bool ShouldBroadcast => false;
    public bool ShouldBuffer => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public void Serialize(PacketWriter writer)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this);
        if (bytes.Length > 4096) throw new InvalidDataException("Chat message too large");
        writer.WriteUShort((ushort)bytes.Length); writer.WriteBytes(bytes, bytes.Length);
    }
    public void Deserialize(PacketReader reader)
    {
        int count = reader.ReadUShort();
        if (count > 4096) throw new InvalidDataException("Chat message too large");
        var bytes = new byte[count];
        for (int index = 0; index < count; index++) bytes[index] = reader.ReadByte();
        var message = JsonSerializer.Deserialize<TacticalChatMessage>(bytes) ?? throw new InvalidDataException("Missing chat message");
        Run = message.Run; Text = message.Text; Tag = message.Tag; Sender = message.Sender; Sequence = message.Sequence;
    }
}

internal sealed class TacticalChat
{
    internal static TacticalChat? Active;
    private readonly Control root;
    private readonly PanelContainer panel;
    private readonly Button toggle;
    private readonly VBoxContainer messages;
    private readonly ScrollContainer scroll;
    private readonly LineEdit input;
    private readonly CheckButton associate;
    private readonly Label peek;
    private readonly AudioStreamPlayer audio;
    private readonly Dictionary<ulong, double> received = new();
    private readonly List<TacticalChatMessage> history = new();
    private INetGameService? net;
    private string run = "";
    private int sequence, seen, unread;
    private double nextSend, peekUntil, lastSound = double.NegativeInfinity;
    private double Now => Time.GetTicksMsec() / 1000.0;
    internal static void Ensure(NGame game)
    {
        if (!game.HasNode("TacticalChat")) _ = new TacticalChat(game);
    }
    private TacticalChat(NGame game)
    {
        Active = this;
        var layer = new CanvasLayer { Name = "TacticalChat", Layer = 25 }; game.AddChild(layer);
        root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = new Theme { DefaultFontSize = 20 } }; layer.AddChild(root);
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        toggle = new Button { Text = "聊天" }; root.AddChild(toggle);
        panel = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(440, 260) };
        panel.AddThemeStyleboxOverride("panel", WheelView.BoxStyle()); root.AddChild(panel);
        var body = new VBoxContainer(); panel.AddChild(body);
        var heading = new HBoxContainer(); body.AddChild(heading);
        heading.AddChild(new Label { Text = "队伍交流 · 消息带场景/请求标签", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var close = new Button { Text = "×" }; heading.AddChild(close); close.Pressed += Close;
        scroll = new ScrollContainer { CustomMinimumSize = new Vector2(420, 120), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.AddChild(scroll);
        messages = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; scroll.AddChild(messages);
        associate = new CheckButton { Text = "关联当前申请", TooltipText = "勾选后附带当前回应目标或需求" }; body.AddChild(associate);
        var compose = new HBoxContainer(); body.AddChild(compose);
        input = new LineEdit { PlaceholderText = "输入消息，Enter 发送", MaxLength = 160, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        compose.AddChild(input);
        var send = new Button { Text = "发送" }; compose.AddChild(send);
        send.Pressed += Send; input.TextSubmitted += _ => Send();
        input.GuiInput += ev => { if (ev is InputEventKey { Pressed: true, Keycode: Key.Escape }) { input.AcceptEvent(); Close(); } };
        toggle.Pressed += () =>
        {
            if (panel.Visible) Close();
            else
            {
                CombatPanel.Active?.SuspendForChat();
                panel.Show(); unread = 0; input.GrabFocus();
            }
        };
        peek = new Label { Visible = false, CustomMinimumSize = new Vector2(430, 0), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = Control.MouseFilterEnum.Ignore }; root.AddChild(peek);
        audio = new AudioStreamPlayer { VolumeDb = -16, Stream = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = ReplyMusic.SampleRate, Data = ReplyMusic.CreateChat() } };
        layer.AddChild(audio);
        var timer = new Godot.Timer { WaitTime = 0.1, Autostart = true }; layer.AddChild(timer); timer.Timeout += Tick;
        layer.TreeExiting += () => { net?.UnregisterMessageHandler<TacticalChatMessage>(Receive); if (ReferenceEquals(Active, this)) Active = null; };
    }
    private void Close() { input.ReleaseFocus(); panel.Hide(); }
    private void Tick()
    {
        var current = RunManager.Instance.NetService;
        string nextRun = RunManager.Instance.DebugOnlyGetState()?.Rng.Seed.ToString() ?? "";
        if (!ReferenceEquals(current, net) || nextRun != run)
        {
            net?.UnregisterMessageHandler<TacticalChatMessage>(Receive); net = current; run = nextRun;
            net?.RegisterMessageHandler<TacticalChatMessage>(Receive);
            sequence = seen = unread = 0; received.Clear(); history.Clear(); Render(); Close(); audio.Stop(); peekUntil = 0;
        }
        root.Visible = run.Length > 0 && net?.IsConnected == true && MegaCrit.Sts2.Core.Nodes.NGame.Instance?.GetInspectCardScreen().Visible != true;
        var size = root.GetViewportRect().Size;
        toggle.Position = new Vector2(280, size.Y - 52);
        panel.Position = new Vector2(24, Math.Max(100, size.Y - panel.Size.Y - 130));
        toggle.Text = unread == 0 ? "聊天" : $"聊天 ({unread})";
        peek.Position = new Vector2(24, size.Y - 96);
        peek.Visible = !panel.Visible && Now < peekUntil;
        associate.TooltipText = CombatPanel.Active?.ChatTag(true) ?? "事件/奖励";
    }
    private void Send()
    {
        if (net?.IsConnected != true || run.Length == 0 || string.IsNullOrWhiteSpace(input.Text)) return;
        if (Now < nextSend) { peek.Text = "发送较快，请稍候"; peek.Modulate = Colors.White; peekUntil = Now + 2; return; }
        var message = new TacticalChatMessage { Run = run, Text = input.Text, Tag = CombatPanel.Active?.ChatTag(associate.ButtonPressed) ?? "事件/奖励" };
        nextSend = Now + 0.7;
        if (net.Type != NetGameType.Client) Receive(message, net.NetId); else net.SendMessage(message);
        input.Text = ""; input.GrabFocus();
    }
    private void Receive(TacticalChatMessage message, ulong sender)
    {
        if (net == null || message.Run != run || message.Text == null || message.Tag == null
            || message.Text.Length > 160 || message.Tag.Length > 120 || string.IsNullOrWhiteSpace(message.Text)) return;
        message.Text = string.Concat(message.Text.Where(character => !char.IsControl(character))).Trim();
        message.Tag = string.Concat(message.Tag.Where(character => !char.IsControl(character))).Trim();
        if (message.Text.Length == 0) return;
        var players = RunManager.Instance.DebugOnlyGetState()?.Players.Select(player => player.NetId).ToHashSet() ?? new();
        if (net is NetHostGameService host) players.IntersectWith(host.ConnectedPeers.Select(peer => peer.peerId).Append(host.NetId));
        if (net.Type != NetGameType.Client)
        {
            if (!players.Contains(sender) || message.Sequence != 0 || received.TryGetValue(sender, out var last) && Now - last < 0.4) return;
            received[sender] = Now;
            message.Sender = sender; message.Sequence = ++sequence;
            if (net.Type != NetGameType.Singleplayer) net.SendMessage(message);
        }
        else if (net is not NetClientGameService client || sender != client.HostNetId || !players.Contains(message.Sender) || message.Sequence <= seen) return;
        seen = message.Sequence;
        history.Add(message); if (history.Count > 80) history.RemoveAt(0); Render();
        if (message.Sender != net.NetId)
        {
            if (!panel.Visible) unread++;
            peek.Text = Display(message); peek.Modulate = SenderColor(message.Sender); peekUntil = Now + 6;
            if (Now - lastSound > 0.4) { lastSound = Now; audio.Play(); }
        }
    }
    private Color SenderColor(ulong sender) => CombatPanel.PlayerSlotColor(
        RunManager.Instance.DebugOnlyGetState()?.Players.ToList().FindIndex(player => player.NetId == sender) ?? -1);
    private string SenderName(ulong sender)
    {
        var players = RunManager.Instance.DebugOnlyGetState()?.Players.ToList();
        string id = "P" + ((players?.FindIndex(player => player.NetId == sender) ?? -1) + 1);
        string name = id;
        try
        {
            string nickname = string.Concat(MegaCrit.Sts2.Core.Platform.PlatformUtil.GetPlayerNameRaw(net!.Platform, sender).Where(character => !char.IsControl(character))).Trim();
            if (nickname.Length > 24) nickname = nickname[..24] + "…";
            if (nickname.Length > 0) name = nickname + " · " + id;
        }
        catch { }
        return name;
    }
    private string Display(TacticalChatMessage message) => $"{SenderName(message.Sender)} [{message.Tag}]  {message.Text}";
    private void Render()
    {
        foreach (Node child in messages.GetChildren()) { messages.RemoveChild(child); child.QueueFree(); }
        foreach (var message in history)
        {
            var line = new RichTextLabel { FitContent = true, ScrollActive = false, BbcodeEnabled = false,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(400, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
            messages.AddChild(line);
            line.PushColor(SenderColor(message.Sender)); line.AddText(SenderName(message.Sender)); line.Pop();
            line.AddText($" [{message.Tag}]  {message.Text}");
        }
        scroll.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, int.MaxValue);
    }
}

internal sealed partial class CombatPanel
{
    internal void SuspendForChat()
    {
        view.Close(); ClosePartyInspector(); keyDialog.Close();
    }

    internal string? ChatTag(bool includeRequest = false)
    {
        if (state == null || !MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsInProgress) return null;
        var request = includeRequest ? displayed.FirstOrDefault(request => request.Active && request.Id == replySelection) : null;
        return request == null ? "战斗" : Needs.ShortText(request.Need) + " · " + state.Creatures.FirstOrDefault(creature => creature.CombatId == request.Target)?.Name;
    }
}
