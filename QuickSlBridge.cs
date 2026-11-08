using Godot;
using System.Text.Json;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Logging;

namespace TacticalRequests;

public sealed class SlBridgeMessage : INetMessage
{
    public byte Kind { get; set; }
    public string Scope { get; set; } = "";
    public string Key { get; set; } = "";
    public ulong Vote { get; set; }
    public ulong[] Members { get; set; } = [];
    public string Signature { get; set; } = "";
    public bool Ready { get; set; }
    public bool ShouldBroadcast => false;
    public bool ShouldBuffer => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public void Serialize(PacketWriter writer)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(this);
        if (data.Length > 4096) throw new InvalidDataException("SL integration message too large");
        writer.WriteUShort((ushort)data.Length); writer.WriteBytes(data, data.Length);
    }
    public void Deserialize(PacketReader reader)
    {
        int count = reader.ReadUShort();
        if (count > 4096) throw new InvalidDataException("SL integration message too large");
        var data = new byte[count];
        for (int index = 0; index < count; index++) data[index] = reader.ReadByte();
        var parsed = JsonSerializer.Deserialize<SlBridgeMessage>(data) ?? throw new InvalidDataException("Missing integration message");
        Kind = parsed.Kind; Scope = parsed.Scope; Key = parsed.Key; Vote = parsed.Vote;
        Members = parsed.Members; Signature = parsed.Signature; Ready = parsed.Ready;
    }
}

internal sealed class QuickSlBridge
{
    internal static QuickSlBridge? Active;
    internal static string Status { get; private set; } = "正在检测 QuickSL";
    private INetGameService? net;
    private readonly QuickSlAdapter? adapter;
    private readonly Dictionary<ulong, (string Signature, double Seen)> capabilities = new();
    private readonly HashSet<ulong> acknowledgements = new();
    private readonly HashSet<string> attempted = new();
    private SlBridgeMessage? pending;
    private bool begun, acknowledged;
    private string scope = "";
    private object? runInstance;
    private double nextHeartbeat, deadline;
    private double Now => Time.GetTicksMsec() / 1000.0;
    private bool Host => net?.Type == NetGameType.Host;
    private HashSet<ulong> Members
    {
        get
        {
            var players = RunManager.Instance.DebugOnlyGetState()?.Players.Select(player => player.NetId).ToHashSet() ?? new();
            if (net is NetHostGameService host) players.IntersectWith(host.ConnectedPeers.Select(peer => peer.peerId).Append(host.NetId));
            return players;
        }
    }
    private IEnumerable<SlApproval> Approvals => (CombatPanel.Active?.SlApprovals() ?? [])
        .Concat(RewardSlPanel.Active?.SlApprovals() ?? []);
    private bool Consents(SlBridgeMessage message, bool all) => net != null && Members.SetEquals(message.Members)
        && Approvals.Any(approval => approval.Key == message.Key && approval.Members.SetEquals(message.Members)
            && (all ? approval.Unanimous : approval.Accepted.Contains(net.NetId)));

    internal static void Ensure(NGame game)
    {
        if (game.HasNode("TacticalQuickSlBridge")) return;
        _ = new QuickSlBridge(game);
    }
    private QuickSlBridge(NGame game)
    {
        Active = this;
        adapter = QuickSlAdapter.Detect();
        try { adapter?.InstallPopupHook(); }
        catch (Exception error) { adapter = null; GD.PushWarning("QuickSL adapter unavailable: " + error.Message); }
        var timer = new Godot.Timer { Name = "TacticalQuickSlBridge", WaitTime = 0.1, Autostart = true };
        game.AddChild(timer); timer.Timeout += Tick;
        timer.TreeExiting += () => { net?.UnregisterMessageHandler<SlBridgeMessage>(Receive); if (ReferenceEquals(Active, this)) Active = null; };
    }

    private void Tick()
    {
        try { Update(); }
        catch (Exception error) { Abort("QuickSL 联动失败，请使用原 MOD 确认"); GD.PushWarning(error.Message); }
    }
    private void Update()
    {
        var current = RunManager.Instance.NetService;
        var run = RunManager.Instance.DebugOnlyGetState();
        string nextScope = run == null ? "" : $"{run.Rng.Seed}|{run.RunLocation}";
        if (!ReferenceEquals(net, current) || !ReferenceEquals(runInstance, run) || scope != nextScope)
        {
            if (pending != null && Host) adapter?.Cancel(pending.Vote);
            net?.UnregisterMessageHandler<SlBridgeMessage>(Receive);
            net = current; runInstance = run; scope = nextScope;
            net?.RegisterMessageHandler<SlBridgeMessage>(Receive);
            pending = null; begun = acknowledged = false; capabilities.Clear(); acknowledgements.Clear(); attempted.Clear(); nextHeartbeat = 0;
        }
        if (net?.IsConnected != true || scope.Length == 0 || net.Type == NetGameType.Singleplayer) return;
        var members = Members;
        if (Now >= nextHeartbeat)
        {
            nextHeartbeat = Now + 1;
            string signature = adapter == null ? "" : QuickSlAdapter.Signature;
            if (Host)
            {
                capabilities[net.NetId] = (signature, Now);
                bool ready = members.Count > 1 && members.All(id => capabilities.TryGetValue(id, out var cap) && cap.Signature == QuickSlAdapter.Signature && Now - cap.Seen < 4);
                Status = ready ? "QuickSL 全员兼容，全员同意后自动读档" : adapter == null ? "主机未加载兼容 QuickSL" : "等待队友的 QuickSL 兼容检测";
                net.SendMessage(new SlBridgeMessage { Kind = 1, Scope = scope, Signature = signature, Ready = ready, Members = members.ToArray() });
                if (ready && pending == null && !adapter!.Busy)
                {
                    var approval = Approvals.FirstOrDefault(approval => approval.Unanimous && approval.Members.SetEquals(members) && !attempted.Contains(approval.Key));
                    if (approval != null)
                    {
                        attempted.Add(approval.Key);
                        pending = new SlBridgeMessage { Kind = 2, Scope = scope, Key = approval.Key, Vote = adapter.NextVote, Members = members.ToArray() };
                        deadline = Now + 5; begun = false; acknowledged = true;
                        acknowledgements.Clear(); acknowledgements.Add(net.NetId); net.SendMessage(pending);
                    }
                }
            }
            else net.SendMessage(new SlBridgeMessage { Kind = 0, Scope = scope, Signature = signature });
        }
        if (pending == null) return;
        if (begun && adapter != null && !adapter.Busy) { Abort("QuickSL 投票已结束，需要时请重新申请"); return; }
        if (!Consents(pending, Host))
        {
            if (!Host && !acknowledged && Now <= deadline && Members.SetEquals(pending.Members)) return;
            Abort("SL 申请或参与玩家发生变化，联动已取消"); return;
        }
        if (!begun && Now > deadline) { Abort("QuickSL 联动确认超时"); return; }
        if (!Host && !acknowledged && adapter != null && !adapter.Busy)
        {
            acknowledged = true;
            net.SendMessage(new SlBridgeMessage { Kind = 3, Scope = scope, Key = pending.Key, Vote = pending.Vote });
        }
        if (Host && !begun && acknowledgements.SetEquals(members))
        {
            if (adapter == null || adapter.Busy || adapter.NextVote != pending.Vote) { Abort("QuickSL 已有其他投票，请重新申请"); return; }
            begun = true;
            net.SendMessage(new SlBridgeMessage { Kind = 4, Scope = scope, Key = pending.Key, Vote = pending.Vote, Members = pending.Members });
            adapter.Start(); Status = "已交由 QuickSL 同步读档";
        }
        if (begun) Status = "QuickSL 正在处理本轮读档";
    }

    private void Receive(SlBridgeMessage message, ulong sender)
    {
        if (net == null || message.Scope != scope || message.Members == null || message.Members.Length > 4
            || message.Key == null || message.Key.Length > 1024 || message.Signature == null || message.Signature.Length > 100) return;
        if (Host)
        {
            if (!Members.Contains(sender)) return;
            if (message.Kind == 0) capabilities[sender] = (message.Signature, Now);
            else if (message.Kind == 3 && pending != null && message.Key == pending.Key && message.Vote == pending.Vote) acknowledgements.Add(sender);
            else if (message.Kind == 6 && pending != null && message.Key == pending.Key && message.Vote == pending.Vote) Abort("队友撤回了本轮 SL 确认");
            return;
        }
        if (net is not NetClientGameService client || sender != client.HostNetId) return;
        if (message.Kind == 1)
        {
            Status = message.Signature != QuickSlAdapter.Signature ? "主机未加载兼容 QuickSL"
                : adapter == null ? "本机未加载兼容 QuickSL" : message.Ready ? "QuickSL 全员兼容，全员同意后自动读档" : "等待队友的 QuickSL 兼容检测";
        }
        else if (message.Kind == 2 && pending == null && message.Vote != 0 && adapter != null && !adapter.Busy && Members.SetEquals(message.Members))
        {
            pending = message; begun = acknowledged = false; deadline = Now + 5;
        }
        else if (pending != null && message.Key == pending.Key && message.Vote == pending.Vote)
        {
            if (message.Kind == 4 && acknowledged && Consents(pending, false)) begun = true;
            else if (message.Kind == 5) { pending = null; begun = acknowledged = false; }
        }
    }

    internal bool AcceptBoundVote(ulong vote)
    {
        if (Host || adapter == null || pending == null || !begun || pending.Vote != vote || adapter.LocalVote != vote || !Consents(pending, false)) return false;
        try { adapter.Accept(); Status = "已交由 QuickSL 同步读档"; return true; }
        catch (Exception error) { GD.PushWarning("QuickSL vote acceptance failed: " + error.Message); return false; }
    }
    private void Abort(string reason)
    {
        if (pending != null && Host)
        {
            try { adapter?.Cancel(pending.Vote); } catch (Exception error) { GD.PushWarning(error.Message); }
            net?.SendMessage(new SlBridgeMessage { Kind = 5, Scope = scope, Key = pending.Key, Vote = pending.Vote });
        }
        else if (pending != null)
            net?.SendMessage(new SlBridgeMessage { Kind = 6, Scope = scope, Key = pending.Key, Vote = pending.Vote });
        pending = null; begun = acknowledged = false; Status = reason;
        CombatPanel.Active?.SlNotice(reason);
    }
}
