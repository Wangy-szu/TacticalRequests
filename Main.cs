using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Platform;
using TacticalRequests.Core;

namespace TacticalRequests;

[ModInitializer(nameof(Initialize))]
public static class Main
{
    public static void Initialize()
    {
        new Harmony("Wanderer.TacticalRequests").PatchAll(Assembly.GetExecutingAssembly());
        if (MegaCrit.Sts2.Core.Nodes.NGame.Instance is { } game)
            Callable.From(() => AttachRewardSl.Ensure(game)).CallDeferred();
        GD.Print("[TacticalRequests] 0.6.25 multiplayer test; Ctrl shortcuts; solo simulation disabled by default");
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi._Ready))]
public static class AttachPanel
{
    public static void Postfix(NCombatUi __instance)
    {
        try { _ = new CombatPanel(__instance); }
        catch (Exception e) { GD.PushError($"[TacticalRequests] UI initialization failed: {e}"); }
    }
}

internal sealed partial class CombatPanel
{
    internal static CombatPanel? Active;
    private readonly WheelView view;
    private readonly TargetCallouts callouts;
    private readonly NCombatUi ui;
    private readonly Window window;
    private readonly SceneTree tree;
    private readonly Dictionary<Control, Control.GuiInputEventHandler> hitboxes = new();
    private Creature? wheelTarget;
    private readonly Board board = new();
    private readonly Dictionary<ulong, double> receivedAt = new();
    private INetGameService? net;
    private CombatState? state;
    private ulong context;
    private int round, revision, seenRevision = -1;
    private int renderedRevision = -1;
    private double heartbeat;
    private bool disposed, failed;
    private List<Request> displayed = new();
    private bool IsHost => net?.Type != NetGameType.Client;
    private double Now => Time.GetTicksMsec() / 1000.0;
    private static string NeedText(Need need) => Needs.Text(need);

    public CombatPanel(NCombatUi ui)
    {
        this.ui = ui; Active = this;
        window = ui.GetWindow();
        tree = ui.GetTree();
        var layer = new CanvasLayer { Layer = 20, Name = "TacticalRequests" };
        ui.AddChild(layer);
        view = new WheelView(layer, need => {
            if (CanOpenWheel() && wheelTarget is { CombatId: uint id }
                && (wheelTarget.IsAlive || wheelTarget.IsPlayer && need == Need.RequestSl))
                Submit(new(Operation.Create, 0, need, id, Reply.Take));
        });
        callouts = new TargetCallouts(view.Rows);
        BuildTestTools(); BuildPartyInspector(); BuildKeySettings(); RefreshKeyLabels();
        var timer = new Godot.Timer { WaitTime = 0.1, Autostart = true };
        layer.AddChild(timer); timer.Timeout += Tick;
        window.FocusExited += CloseWheelOnFocusLoss;
        window.WindowInput += HandleWindowInput;
        tree.ProcessFrame += PollShortcuts;
        layer.TreeExiting += Dispose;
    }

    private void CloseWheelOnFocusLoss()
    {
        StopPartyDrag();
        wheelTarget = null;
        view.Close();
        resetShortcutKeys = true;
    }

    private void HandleWindowInput(InputEvent input)
    {
        if (InterceptStockShortcut(input) || CloseOnEscape(input)) ui.GetViewport().SetInputAsHandled();
    }

    private void Tick()
    {
        if (disposed) return;
        if (failed)
        {
            var nextState = CombatManager.Instance.DebugOnlyGetState();
            if (nextState == null || (ReferenceEquals(nextState, state) && nextState.RoundNumber == round)) return;
            failed = false;
            state = null;
            renderedRevision = -1;
        }
        try
        {
            var manager = RunManager.Instance;
            var currentNet = manager.NetService;
            if (!ReferenceEquals(currentNet, net))
            {
                ClosePartyInspector(); ResetTest(); Unregister(); net = currentNet;
                if (net != null)
                {
                    net.RegisterMessageHandler<RequestMessage>(ReceiveRequest);
                    net.RegisterMessageHandler<SnapshotMessage>(ReceiveSnapshot);
                }
                seenRevision = -1; board.Clear(); displayed.Clear(); receivedAt.Clear();
                normalPresentation = new();
            }
            var currentState = CombatManager.Instance.DebugOnlyGetState();
            bool combatActive = CombatManager.Instance.IsInProgress && currentState != null;
            view.Root.Visible = combatActive && MegaCrit.Sts2.Core.Nodes.NGame.Instance?.GetInspectCardScreen().Visible != true;
            if (!combatActive) { ClosePartyInspector(); ResetTest(); view.Close(); return; }
            if (!ReferenceEquals(currentState, state))
            {
                ClosePartyInspector(); ResetTest(); normalPresentation = new();
                state = currentState; board.Clear(); displayed.Clear(); seenRevision = -1; round = -1;
                var run = manager.DebugOnlyGetState();
                var key = $"{run?.Rng.Seed}|{run?.RunLocation}|{state!.Encounter?.Id}";
                context = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            }
            if (state!.RoundNumber != round)
            {
                round = state.RoundNumber; board.Clear(); displayed.Clear(); receivedAt.Clear(); seenRevision = -1;
                testBoard.Clear(); revision++; view.Close(); Render();
            }
            if (testing && !CanTest) { ResetTest(); Render(); }
            UpdateTestTools(); TickPartyInspector();
            if (requestNotice != null) requestNotice.Visible = Now < Presentation.NoticeUntil;
            BindTargets();
            SyncCombatFont();
            callouts.Update();
            bool connected = net?.IsConnected == true;
            bool playerTurn = state.CurrentSide == CombatSide.Player;
            if (!CanOpenWheel() || wheelTarget is { IsAlive: false } && (!wheelTarget.IsPlayer || !view.SlOnly)) view.Close();
            if (testing && testBoard.Observe(TargetStates(), TestPlayers(), playerTurn)) RefreshTest();
            if (IsHost && connected)
            {
                if (board.Observe(TargetStates(), Players(), playerTurn)) { revision++; Broadcast(); }
                if (Now >= heartbeat) { heartbeat = Now + 1; Broadcast(); }
            }
        }
        catch (Exception e)
        {
            failed = true; view.Close(); view.Root.Hide();
            GD.PushError($"[TacticalRequests] stopped safely: {e}");
        }
    }

    private HashSet<ulong> Players()
    {
        var ids = state!.Players.Select(p => p.NetId).ToHashSet();
        if (net is NetHostGameService host)
            ids.IntersectWith(host.ConnectedPeers.Select(p => p.peerId).Append(host.NetId));
        return ids;
    }
    private Dictionary<uint, Target> TargetStates() => state!.Creatures.Where(c => c.CombatId.HasValue)
        .ToDictionary(c => c.CombatId!.Value, c => new Target(c.CombatId!.Value, c.IsPlayer, c.IsAlive,
            (int)(c.GetPower<VulnerablePower>()?.Amount ?? 0), (int)(c.GetPower<WeakPower>()?.Amount ?? 0), c.Block,
            c.Player?.NetId, (int)(c.GetPower<StrengthPower>()?.Amount ?? 0)));

    private void Submit(Command command)
    {
        if (net?.IsConnected != true || state?.CurrentSide != CombatSide.Player) return;
        if (testing) { ApplyTest(Viewer!.Value, command); return; }
        var message = new RequestMessage { Context = context, Round = round, Command = command };
        if (IsHost) ReceiveRequest(message, net.NetId);
        else net.SendMessage(message);
    }
    private void ReceiveRequest(RequestMessage message, ulong sender)
    {
        if (disposed || failed || !IsHost || state == null || message.Context != context || message.Round != round
            || state.CurrentSide != CombatSide.Player || !CombatManager.Instance.IsInProgress) return;
        // Bound incoming UI traffic; identity comes from the game's transport, not the request body.
        if (receivedAt.TryGetValue(sender, out var time) && Now - time < 0.15) return;
        receivedAt[sender] = Now;
        if (board.Apply(sender, message.Command, Players(), TargetStates(), Now)) { revision++; Broadcast(); }
    }
    private void ReceiveSnapshot(SnapshotMessage message, ulong sender)
    {
        if (disposed || failed || net is not NetClientGameService client || sender != client.HostNetId
            || message.Context != context || message.Round != round || message.Revision <= seenRevision) return;
        if (!SnapshotValidation.IsValid(message.Requests)) return;
        try
        {
            displayed = message.Requests; Render(); seenRevision = message.Revision;
        }
        catch (Exception e)
        {
            failed = true; view.Close(); view.Root.Hide();
            GD.PushError($"[TacticalRequests] snapshot UI paused until next round: {e}");
        }
    }
    private void Broadcast()
    {
        if (testing) return;
        displayed = board.Requests;
        var message = new SnapshotMessage { Context = context, Round = round, Revision = revision, Requests = displayed };
        if (net?.IsConnected == true && net.Type != NetGameType.Singleplayer) net.SendMessage(message);
        if (renderedRevision != revision) { Render(); renderedRevision = revision; }
    }
    private string PlayerName(ulong id, bool compact = false)
    {
        if (testing && id == TestPeer) return "模拟队友" + (Viewer == id ? "（当前操作）" : "");
        var players = state!.Players.ToList();
        int index = players.FindIndex(p => p.NetId == id);
        string name = "";
        try { if (net != null && net.Platform != PlatformType.None) name = PlatformUtil.GetPlayerNameRaw(net.Platform, id); }
        catch { /* A temporarily unavailable platform name must not disable combat UI. */ }
        name = string.Concat(name.Where(c => !char.IsControl(c))).Trim();
        if (name.Length > 24) name = name[..24] + "…";
        string identity = index >= 0 ? $"P{index + 1}" : $"ID {id}";
        if (string.IsNullOrWhiteSpace(name)) name = compact ? identity : index >= 0 ? players[index].Creature.Name : "玩家";
        if (compact) return name;
        return $"{name} · {identity}" + (id == Viewer ? "（你）" : "");
    }
    private bool CanUseCombatOverlay() => !KeySettingsOpen && !disposed && !failed
        && net?.IsConnected == true && state != null && CombatManager.Instance.IsInProgress && ui.IsVisibleInTree()
        && MegaCrit.Sts2.Core.Nodes.NGame.Instance?.GetInspectCardScreen().Visible != true;

    private bool CanOpenWheel()
    {
        return CanUseCombatOverlay() && (IsHost || seenRevision >= 0)
            && state?.CurrentSide == CombatSide.Player && !ui.Hand.InCardPlay && !ui.Hand.IsInCardSelection
            && !NTargetManager.Instance.IsInSelection
            && tree.GetFrame() > NTargetManager.Instance.LastTargetingFinishedFrame + 1;
    }
    private void BindTargets()
    {
        var room = NCombatRoom.Instance;
        if (room == null || state == null) return;
        foreach (var creature in state.Creatures.Where(c => c.IsEnemy || c.IsPlayer))
        {
            var node = room.GetCreatureNode(creature);
            if (node == null || hitboxes.ContainsKey(node.Hitbox)) continue;
            Control.GuiInputEventHandler handler = input => {
                if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }
                    || !CanOpenWheel() || (!creature.IsAlive && !creature.IsPlayer)) return;
                if (creature.IsPlayer && creature.Player?.NetId != net?.NetId) { InspectTeammate(creature.Player!.NetId); node.Hitbox.AcceptEvent(); return; }
                ClosePartyInspector();
                // Target identity is captured from the clicked creature, never from the UI index.
                wheelTarget = creature;
                view.Open(creature.IsPlayer, creature.IsPlayer ? PlayerName(Viewer ?? creature.Player!.NetId, compact: true) : creature.Name,
                    node.Hitbox.GetGlobalTransformWithCanvas() * (node.Hitbox.Size / 2), creature.IsPlayer && !creature.IsAlive);
                node.Hitbox.AcceptEvent();
            };
            node.Hitbox.GuiInput += handler; hitboxes.Add(node.Hitbox, handler);
        }
    }
    internal bool CloseOnEscape(InputEvent input)
    {
        if (input is InputEventKey key)
        {
            var code = key.PhysicalKeycode == Key.None ? key.Keycode : key.PhysicalKeycode;
            if (!key.Pressed)
            {
                observedShortcutKeys.Remove(code);
                polledShortcutKeys.Remove(code);
            }
            else if (polledShortcutKeys.Contains(code)) return true;
        }
        ulong frame = Engine.GetProcessFrames();
        ulong eventId = input.GetInstanceId();
        if (lastHandledEvent == eventId && lastHandledFrame == frame) return true;
        bool handled = HandleCombatInput(input);
        if (input is InputEventKey { Pressed: true, Echo: false } shortcut && (shortcut.AltPressed || shortcut.CtrlPressed))
        {
            var code = shortcut.PhysicalKeycode == Key.None ? shortcut.Keycode : shortcut.PhysicalKeycode;
            if (KeyBindings.Actions.Take(8).Any(action => view.Keys.Values[action].Code == code))
                GD.Print($"[TacticalRequests] shortcut key={code} alt={shortcut.AltPressed} ctrl={shortcut.CtrlPressed} handled={handled} wheel={view.IsOpen} reply={replySelection} available={CanUseCombatOverlay()} focus={ui.GetViewport().GuiGetFocusOwner()?.GetType().Name}");
        }
        if (handled)
        {
            lastHandledEvent = eventId; lastHandledFrame = frame;
            if (input is InputEventKey { Pressed: true } handledKey)
            {
                observedShortcutKeys.Add(handledKey.PhysicalKeycode == Key.None ? handledKey.Keycode : handledKey.PhysicalKeycode);
                if (handledKey.AltPressed || handledKey.CtrlPressed)
                    reservedShortcutKeys.Add(handledKey.PhysicalKeycode == Key.None ? handledKey.Keycode : handledKey.PhysicalKeycode);
            }
        }
        return handled;
    }
    private ulong lastHandledEvent;
    private ulong lastHandledFrame;
    private bool HandleCombatInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true } key) return false;
        if (HandleKeySettings(key)) { ui.GetViewport().SetInputAsHandled(); return true; }
        if (PartyOpen && key.Keycode == Key.Escape) { ClosePartyInspector(); ui.GetViewport().SetInputAsHandled(); return true; }
        if (CanUseCombatOverlay() && ui.GetViewport().GuiGetFocusOwner() is not (LineEdit or TextEdit))
        {
            if (view.Keys.Is("查看队友", key)) { TogglePartyInspector(); ui.GetViewport().SetInputAsHandled(); return true; }
            if (CanTest && view.Keys.Is("单机测试", key)) { ToggleTest(); ui.GetViewport().SetInputAsHandled(); return true; }
        }
        if (HandleReplyKey(key)) { ui.GetViewport().SetInputAsHandled(); return true; }
        if (view.HandleKey(key)) { ui.GetViewport().SetInputAsHandled(); return true; }
        if (key.Echo) return false;
        if (ui.GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return false;
        bool selfShortcut = view.Keys.Is("自己的轮盘", key);
        bool enemyShortcut = view.Keys.Is("怪物轮盘", key);
        if (!selfShortcut && !enemyShortcut) return false;
        if (!CanOpenWheel())
        {
            if (!CanUseCombatOverlay()) return false;
            Presentation.NoticeRequest = null;
            Presentation.NoticeText = state?.CurrentSide != CombatSide.Player ? "等待玩家回合后打开轮盘"
                : !IsHost && seenRevision < 0 ? "正在同步队友请求，请稍候"
                : "请先完成或取消出牌、选牌和目标选择，再打开轮盘";
            Presentation.NoticeColor = new Color("ffd07d");
            Presentation.NoticeUntil = Now + 4;
            UpdateRequestNotice();
            ui.GetViewport().SetInputAsHandled();
            return true;
        }
        var room = NCombatRoom.Instance;
        var target = selfShortcut ? room?.CreatureNodes.FirstOrDefault(n => n.Entity.Player?.NetId == net?.NetId)
            : room?.CreatureNodes.FirstOrDefault(n => n.Entity.IsEnemy && n.Entity.IsAlive && n.Hitbox.GetGlobalRect().HasPoint(n.Hitbox.GetGlobalMousePosition()));
        if (target == null || (!target.Entity.IsAlive && !selfShortcut))
        {
            Presentation.NoticeRequest = null;
            Presentation.NoticeText = selfShortcut ? "当前角色暂不可用" : "请将鼠标移到存活怪物上，再打开怪物轮盘";
            Presentation.NoticeColor = new Color("ffd07d");
            Presentation.NoticeUntil = Now + 4;
            UpdateRequestNotice();
            ui.GetViewport().SetInputAsHandled();
            return true;
        }
        ClosePartyInspector(); wheelTarget = target.Entity;
        view.Open(target.Entity.IsPlayer, target.Entity.IsPlayer ? PlayerName(Viewer ?? target.Entity.Player!.NetId, compact: true) : target.Entity.Name,
            target.Hitbox.GetGlobalTransformWithCanvas() * (target.Hitbox.Size / 2), selfShortcut && !target.Entity.IsAlive);
        ui.GetViewport().SetInputAsHandled(); return true;
    }
    private void Render()
    {
        SyncCombatFont();
        if (replySelection == null || !displayed.Any(request => request.Active && request.Id == replySelection))
            replySelection = ReplyCandidates().FirstOrDefault()?.Id;
        UpdateRequestNotice();
        var rows = view.Rows;
        callouts.Clear();
        foreach (Node child in rows.GetChildren()) { rows.RemoveChild(child); child.QueueFree(); }
        UpdatePartyVisibility();
        if (focusTarget.HasValue && state?.Creatures.Any(creature => creature.CombatId == focusTarget && creature.IsAlive) != true)
            focusTarget = null;
        foreach (var group in displayed.Where(r => r.Active).GroupBy(r => r.Target).OrderBy(g => g.Key))
        {
        var targetCreature = state?.Creatures.FirstOrDefault(c => c.CombatId == group.Key);
        string title = targetCreature?.IsPlayer == true ? (testing ? "模拟角色位置 · " + string.Join("、", group.Select(r => PlayerName(r.Author)).Distinct()) : PlayerName(targetCreature.Player!.NetId)) : $"{targetCreature?.Name ?? "离场目标"} · #{group.Key}";
        bool focusAccepted = focusTarget == group.Key;
        var targetRows = callouts.Add(group.Key, title, targetCreature?.IsPlayer == true, group.Any(r => r.Active), group.Any(r => r.Id == replySelection), targetCreature?.IsPlayer == true ? PlayerColor(targetCreature.Player!.NetId) : null, focusAccepted);
        foreach (var merged in group.Where(r => r.Active).GroupBy(r => r.Need).OrderBy(g => g.Key == Need.RequestSl ? 0 : 1).ThenBy(g => g.Key))
        {
            var request = merged.First();
            var creature = state?.Creatures.FirstOrDefault(c => c.CombatId == request.Target);
            string target = creature?.IsPlayer == true ? PlayerName(creature.Player!.NetId) : $"{creature?.Name ?? "离场目标"} #{request.Target}";
            var box = new VBoxContainer(); targetRows.AddChild(box);
            bool enemyRow = targetCreature?.IsPlayer != true;
            var compact = enemyRow ? new HBoxContainer() : null;
            if (enemyRow) { compact!.AddThemeConstantOverride("separation", 2); box.AddChild(compact); }
            string authors = string.Join("、", merged.Select(r => PlayerName(r.Author)).Distinct());
            var caption = new Label { Text = Needs.ShortText(request.Need),
                CustomMinimumSize = new Vector2(enemyRow ? 60 : 300, 0),
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, TooltipText = NeedText(request.Need) + " · " + authors };
            if (request.Need == Need.RequestSl) caption.TooltipText += "\n" + QuickSlBridge.Status;
            if (!enemyRow && merged.Any(r => r.Id == replySelection)) caption.Text = "▶ " + caption.Text;
            caption.TooltipText += "\n直接回应高亮申请 · " + view.Keys.Values["回应：我来／同意"] + " 我来/同意 · " + view.Keys.Values["回应：做不到／打打看"] + " 做不到/打打看 · " + view.Keys.Values["回应：等一下"] + " 等一下 · " + view.Keys.Values["选择回应"] + " 切换申请";
            var authorIds=merged.Select(r => r.Author).Distinct().ToArray();
            caption.Modulate=authorIds.Length == 1 ? PlayerColor(authorIds[0]) : Colors.White;
            if (enemyRow)
            {
                compact!.AddChild(new Label { Name = "ReplySelectionMarker" + request.Id,
                    Text = merged.Any(r => r.Id == replySelection) ? "▶" : "",
                    CustomMinimumSize = new Vector2(24, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
                compact!.AddChild(caption);
                bool ownSelected = merged.Any(r => r.Author == Viewer) && merged.Any(r => r.Id == replySelection);
                if (!ownSelected) AddAuthorColors(compact,authorIds,()=>{ replySelection=request.Id;Render(); });
            }
            else { caption.Text += " · P" + (PlayerIndex(request.Author) + 1); box.AddChild(caption); }
            caption.MouseFilter = Control.MouseFilterEnum.Stop;
            caption.GuiInput += input => {
                if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                { replySelection = request.Id; caption.AcceptEvent(); Render(); }
            };
            string detail = request.Status switch
            {
                Status.Done => "✓ 已完成",
                Status.Cancelled => "已撤销", Status.Expired => "已过期",
                Status.Claimed => $"{PlayerName(request.Owner!.Value)} 负责", _ => Needs.IsTeam(request.Need) ? "队伍协作 · 可多人回应" : "等待队友回应"
            };
            string responses = string.Join("；", merged.SelectMany(r => r.Replies).Distinct().Select(reply => $"{PlayerName(reply.Key)}：{Needs.ReplyText(request.Need, reply.Value)}"));
            caption.TooltipText += "\n" + detail + "\n" + responses;
            if (request.Active && state?.CurrentSide == CombatSide.Player)
            {
                var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 2); if (enemyRow) compact!.AddChild(buttons); else box.AddChild(buttons);
                var own = merged.FirstOrDefault(r => r.Author == Viewer);
                if (own != null && (!enemyRow || merged.Any(r => r.Id == replySelection))) {
                    AddButton(buttons, "×", () => Submit(new(Operation.Cancel, own.Id, default, 0, default))).TooltipText = "撤销我的申请";
                    AddButton(buttons, "✓", () => Submit(new(Operation.Complete, own.Id, default, 0, default))).TooltipText = "我的需求已满足";
                }
                if (own != null && merged.All(r => r.Author == Viewer) && own.Replies.Count > 0)
                {
                    var latest = own.Replies.Last();
                    buttons.AddChild(new Label { Name = "RequestReplyFeedback", Text = Needs.ShortReplyText(own.Need, latest.Value),
                        Modulate = ReplyFeedbackColor(latest.Value), TooltipText = responses, MouseFilter = Control.MouseFilterEnum.Pass });
                }
                if (merged.Any(r => r.Author != Viewer)) foreach (var reply in Enum.GetValues<Reply>())
                {
                    var button = AddButton(buttons, Needs.ShortReplyText(request.Need, reply), () => Submit(new(targetCreature?.IsPlayer == true ? Operation.Respond : Operation.RespondGroup, request.Id, default, 0, reply)));
                    button.TooltipText = Needs.ReplyText(request.Need, reply);
                    button.Modulate = merged.Any(r => Viewer is ulong viewer && r.Replies.TryGetValue(viewer, out var recorded) && recorded == reply)
                        ? ReplyFeedbackColor(reply) : Colors.White;
                    button.Disabled = !Needs.IsTeam(request.Need) && reply == Reply.Take && merged.Any(r => r.Author != Viewer && r.Owner.HasValue && r.Owner != Viewer);
                }
            }
        }
        }
        if (focusTarget is uint focused && !displayed.Any(request => request.Active && request.Target == focused))
            callouts.Add(focused, "集火目标", false, true, focusAccepted: true, reticleOnly: true);
        callouts.Update();
    }
    private static Button AddButton(HBoxContainer parent, string text, Action action)
    {
        var button = new Button { Text = text }; parent.AddChild(button); button.Pressed += action; return button;
    }
    private void Unregister()
    {
        if (net == null) return;
        net.UnregisterMessageHandler<RequestMessage>(ReceiveRequest);
        net.UnregisterMessageHandler<SnapshotMessage>(ReceiveSnapshot);
    }
    private void Dispose() {
        disposed = true; Unregister();
        tree.ProcessFrame -= PollShortcuts;
        if (GodotObject.IsInstanceValid(window))
        {
            window.FocusExited -= CloseWheelOnFocusLoss;
            window.WindowInput -= HandleWindowInput;
        }
        foreach (var pair in hitboxes) if (GodotObject.IsInstanceValid(pair.Key)) pair.Key.GuiInput -= pair.Value;
        hitboxes.Clear(); if (ReferenceEquals(Active, this)) Active = null;
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi._Input))]
public static class WheelEscape
{
    public static bool Prefix(InputEvent __0) => CombatPanel.Active?.CloseOnEscape(__0) != true;
}







