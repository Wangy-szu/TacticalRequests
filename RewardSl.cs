using Godot;
using HarmonyLib;
using System.Text.Json;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Combat;
using TacticalRequests.Core;

namespace TacticalRequests;

public sealed class RewardSlRequest
{
    public int Id { get; set; }
    public ulong Author { get; set; }
    public Dictionary<ulong, Reply> Votes { get; set; } = new();
}

public sealed class RewardSlMessage : INetMessage
{
    public string Context { get; set; } = "";
    public int Revision { get; set; }
    public byte Operation { get; set; }
    public ulong Author { get; set; }
    public Reply Reply { get; set; }
    public List<RewardSlRequest> Requests { get; set; } = new();
    public bool ShouldBroadcast => false;
    public bool ShouldBuffer => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public void Serialize(PacketWriter writer)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(this);
        if (data.Length > 8192) throw new InvalidDataException("Reward request too large");
        writer.WriteUShort((ushort)data.Length);
        writer.WriteBytes(data, data.Length);
    }
    public void Deserialize(PacketReader reader)
    {
        int count = reader.ReadUShort();
        if (count > 8192) throw new InvalidDataException("Reward request too large");
        var data = new byte[count];
        for (int index = 0; index < count; index++) data[index] = reader.ReadByte();
        var message = JsonSerializer.Deserialize<RewardSlMessage>(data) ?? throw new InvalidDataException("Missing reward request");
        Context = message.Context; Revision = message.Revision; Operation = message.Operation;
        Author = message.Author; Reply = message.Reply; Requests = message.Requests;
    }
}

[HarmonyPatch(typeof(NGame), nameof(NGame._Ready))]
internal static class AttachRewardSl
{
    static void Postfix(NGame __instance) => Ensure(__instance);
    internal static void Ensure(NGame game)
    {
        if (GodotObject.IsInstanceValid(game) && !game.HasNode("TacticalRewardSl")) _ = new RewardSlPanel(game);
        QuickSlBridge.Ensure(game);
        TacticalChat.Ensure(game);
    }
}

internal sealed class RewardSlPanel
{
    internal static RewardSlPanel? Active;
    private readonly Control root;
    private readonly VBoxContainer rows;
    private readonly Label integrationStatus;
    private readonly AudioStreamPlayer audio;
    private readonly Dictionary<Reply, AudioStreamWav> sounds = new();
    private double lastSound = double.NegativeInfinity;
    private INetGameService? net;
    private string context = "";
    private int revision, seen = -1;
    private int nextRequestId;
    private int renderedRevision = -1;
    private double heartbeat;
    private List<RewardSlRequest> requests = new();
    private readonly Dictionary<ulong, double> lastCommand = new();
    private bool Host => net?.Type != NetGameType.Client;
    private HashSet<ulong> Players
    {
        get
        {
            var players = RunManager.Instance.DebugOnlyGetState()?.Players.Select(player => player.NetId).ToHashSet() ?? new();
            if (net is NetHostGameService host)
                players.IntersectWith(host.ConnectedPeers.Select(peer => peer.peerId).Append(host.NetId));
            return players;
        }
    }
    private double Now => Time.GetTicksMsec() / 1000.0;

    public RewardSlPanel(NGame game)
    {
        Active = this;
        var layer = new CanvasLayer { Name = "TacticalRewardSl", Layer = 24 };
        game.AddChild(layer);
        root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        root.Theme = new Theme { DefaultFontSize = 30 };
        layer.AddChild(root); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        audio = new AudioStreamPlayer { VolumeDb = -16 }; layer.AddChild(audio);
        rows = new VBoxContainer { Position = new Vector2(24, 180), CustomMinimumSize = new Vector2(360, 0) };
        root.AddChild(rows);
        integrationStatus = new Label { MouseFilter = Control.MouseFilterEnum.Ignore }; root.AddChild(integrationStatus);
        var timer = new Godot.Timer { WaitTime = 0.2, Autostart = true };
        layer.AddChild(timer); timer.Timeout += Tick;
        layer.TreeExiting += () => { Unregister(); if (ReferenceEquals(Active, this)) Active = null; };
    }

    private void Unregister() => net?.UnregisterMessageHandler<RewardSlMessage>(Receive);
    private bool RewardOpen() => root.GetTree().Root.FindChildren("*", "Control", true, false)
        .OfType<Control>().Any(control => control is NRewardsScreen or NCardRewardSelectionScreen or NEventRoom && control.IsVisibleInTree());

    private void Tick()
    {
        var current = RunManager.Instance.NetService;
        if (!ReferenceEquals(net, current))
        {
            Unregister(); net = current; context = ""; seen = -1;
            net?.RegisterMessageHandler<RewardSlMessage>(Receive);
        }
        var run = RunManager.Instance.DebugOnlyGetState();
        string next = run == null ? "" : $"{run.Rng.Seed}|{run.RunLocation}";
        if (context != next)
        {
            context = next; requests.Clear(); lastCommand.Clear(); audio.Stop(); revision = 0; seen = -1; Render();
        }
        bool active = run != null && net?.IsConnected == true && !CombatManager.Instance.IsInProgress;
        bool open = active && RewardOpen();
        root.Visible = active && (open || requests.Count > 0);
        if (rows.GetChildCount() > 0) rows.GetChild<Control>(0).Visible = open;
        if (!active) return;
        if (Host && Now >= heartbeat)
        {
            heartbeat = Now + 1;
            var players = Players;
            bool changed = requests.RemoveAll(request => !players.Contains(request.Author)) > 0;
            foreach (var request in requests)
                foreach (var id in request.Votes.Keys.Where(id => !players.Contains(id)).ToArray())
                { request.Votes.Remove(id); changed = true; }
            if (changed) revision++;
            Broadcast();
        }
        rows.Position = new Vector2(24, Math.Max(130, root.GetViewportRect().Size.Y - 200 - requests.Count * 90));
        integrationStatus.Text = QuickSlBridge.Status;
        integrationStatus.Position = rows.Position - new Vector2(0, 38);
    }

    private void Send(byte operation, ulong author = 0, Reply reply = Reply.Wait)
    {
        if (net?.IsConnected != true || context.Length == 0) return;
        var message = new RewardSlMessage { Context = context, Operation = operation, Author = author, Reply = reply };
        if (Host) Receive(message, net.NetId); else net.SendMessage(message);
    }

    private void Receive(RewardSlMessage message, ulong sender)
    {
        if (net == null || message.Context != context || context.Length == 0) return;
        var players = Players;
        if (message.Operation == 3)
        {
            if (net is not NetClientGameService client || sender != client.HostNetId || message.Revision <= seen
                || message.Requests == null || message.Requests.Count > 4
                || message.Requests.Any(request => request == null || request.Id <= 0 || !players.Contains(request.Author) || request.Votes == null
                    || request.Votes.Count > 4 || request.Votes.Any(vote => vote.Key == request.Author || !players.Contains(vote.Key) || !Enum.IsDefined(vote.Value)))
                || message.Requests.Select(request => request.Author).Distinct().Count() != message.Requests.Count
                || message.Requests.Select(request => request.Id).Distinct().Count() != message.Requests.Count) return;
            if (message.Requests.Any(request => request.Author != net.NetId && !requests.Any(previous => previous.Author == request.Author)))
                PlayNotice(Reply.Wait);
            else
            {
                var own = message.Requests.FirstOrDefault(request => request.Author == net.NetId);
                var previous = requests.FirstOrDefault(request => request.Author == net.NetId);
                if (own != null)
                    foreach (var vote in own.Votes.Where(vote => previous == null || !previous.Votes.TryGetValue(vote.Key, out var old) || old != vote.Value))
                        PlayNotice(vote.Value);
            }
            seen = message.Revision; requests = message.Requests; Render(); return;
        }
        if (!Host || !players.Contains(sender) || CombatManager.Instance.IsInProgress
            || message.Operation > 2 || !Enum.IsDefined(message.Reply)) return;
        if (lastCommand.TryGetValue(sender, out var last) && Now - last < 0.15) return;
        lastCommand[sender] = Now;
        if (message.Operation == 0)
        {
            if (requests.Any(request => request.Author == sender) || requests.Count >= 4) return;
            requests.Add(new RewardSlRequest { Id = ++nextRequestId, Author = sender });
            if (sender != net.NetId) PlayNotice(Reply.Wait);
        }
        else
        {
            var request = requests.FirstOrDefault(request => request.Author == message.Author);
            if (request == null) return;
            if (message.Operation == 2) { if (request.Author != sender) return; requests.Remove(request); }
            else
            {
                if (request.Author == sender || request.Votes.TryGetValue(sender, out var old) && old == message.Reply) return;
                request.Votes[sender] = message.Reply;
                if (request.Author == net.NetId) PlayNotice(message.Reply);
            }
        }
        revision++; Broadcast();
    }

    private void Broadcast()
    {
        if (net == null) return;
        if (net.Type != NetGameType.Singleplayer)
            net.SendMessage(new RewardSlMessage { Operation = 3, Context = context, Revision = revision, Requests = requests });
        if (renderedRevision != revision) Render();
    }

    private void PlayNotice(Reply reply)
    {
        if (Now - lastSound < 0.4) return;
        lastSound = Now;
        if (!sounds.TryGetValue(reply, out var stream))
        {
            stream = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits,
                MixRate = ReplyMusic.SampleRate, Data = ReplyMusic.Create(reply) };
            sounds.Add(reply, stream);
        }
        audio.Stream = stream; audio.Play();
    }

    private string Name(ulong id)
    {
        var players = RunManager.Instance.DebugOnlyGetState()?.Players.ToList();
        string identity = "P" + ((players?.FindIndex(player => player.NetId == id) ?? -1) + 1);
        try { return MegaCrit.Sts2.Core.Platform.PlatformUtil.GetPlayerNameRaw(net!.Platform, id) + " · " + identity; }
        catch { return identity; }
    }

    private void Render()
    {
        renderedRevision = revision;
        foreach (Node child in rows.GetChildren()) { rows.RemoveChild(child); child.QueueFree(); }
        void Button(HBoxContainer parent, string text, Action action)
        { var button = new Button { Text = text }; parent.AddChild(button); button.Pressed += action; }
        var entry = new HBoxContainer(); rows.AddChild(entry);
        entry.Visible = RewardOpen();
        Button(entry, "申请 SL", () => Send(0));
        foreach (var request in requests)
        {
            var group = new VBoxContainer(); rows.AddChild(group);
            var line = new HBoxContainer(); group.AddChild(line);
            line.AddChild(new Label { Text = Name(request.Author) + " 申请 SL", TooltipText = Name(request.Author),
                CustomMinimumSize = new Vector2(240, 0), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
            if (request.Author == net?.NetId) Button(line, "撤销", () => Send(2, request.Author));
            else foreach (var reply in new[] { Reply.Take, Reply.Wait, Reply.Cannot })
                Button(line, ReplyText(reply), () => Send(1, request.Author, reply));
            if (request.Votes.Count > 0)
                group.AddChild(new Label { Text = string.Join(" · ", request.Votes.Select(vote => Name(vote.Key) + " " + ReplyText(vote.Value))),
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, TooltipText = string.Join("\n", request.Votes.Select(vote => Name(vote.Key) + " " + ReplyText(vote.Value))) });
        }
    }
    private static string ReplyText(Reply reply) => reply switch
    { Reply.Take => "同意", Reply.Wait => "等一下", _ => "我比较好" };
    internal IEnumerable<SlApproval> SlApprovals() => requests.Select(request =>
        new SlApproval($"reward:{context}:{request.Id}", Players, SlApproval.Acceptances(request.Author, request.Votes))).ToArray();
}
