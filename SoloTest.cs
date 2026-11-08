using Godot;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using TacticalRequests.Core;

namespace TacticalRequests;

internal sealed partial class CombatPanel
{
    private readonly Board testBoard = new();
    private bool testing, testPeerView;
    private double testTime;
    private Control testTools = null!;
    private Button testToggle = null!;
    private Label testLabel = null!;
    private ulong TestPeer => net?.NetId == ulong.MaxValue ? ulong.MaxValue - 1 : ulong.MaxValue;
    private ulong? Viewer => testing && testPeerView ? TestPeer : net?.NetId;
    private bool CanTest => BuildFeatures.SoloTestingEnabled && net?.Type == NetGameType.Singleplayer && state?.Players.Count() == 1;

    private void BuildTestTools()
    {
        if (!BuildFeatures.SoloTestingEnabled) return;
        var box = new VBoxContainer { Name = "SoloTestTools", Position = new Vector2(280, 90) };
        view.Root.AddChild(box); testTools = box;
        testToggle = new Button { Text = "单机测试：关闭 · Alt+T", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin }; box.AddChild(testToggle);
        testToggle.Pressed += ToggleTest;
        testLabel = new Label(); box.AddChild(testLabel);
        var actions = new HBoxContainer { Name = "SoloTestActions" }; box.AddChild(actions);
        AddButton(actions, "切换操作身份", () => {
            if (!testing || !CanTest) return;
            ClosePartyInspector();
            replySelection = null;
            testPresentation.NoticeUntil = 0;
            testPresentation.KnownReplies.Clear();
            testPresentation.KnownRequests.Clear();
            replyAudio?.Stop();
            testPeerView = !testPeerView; view.Close(); RefreshTest();
        });
        AddButton(actions, "生成对方请求", SeedTest);
        AddButton(actions, "对方回应：我来", () => TestReply(Reply.Take));
        AddButton(actions, "对方回应：做不到", () => TestReply(Reply.Cannot));
        AddButton(actions, "对方回应：等一下", () => TestReply(Reply.Wait));
        AddButton(actions, "清空", () => { if (testing) { testBoard.Clear(); RefreshTest(); } });
        UpdateTestTools();
    }
    private void UpdateTestTools()
    {
        if (!BuildFeatures.SoloTestingEnabled) return;
        testTools.Visible = CanTest && CanUseCombatOverlay() && !view.IsOpen && !PartyOpen;
        testToggle.Text = (testing ? "退出单机测试 · " : "单机测试：关闭 · ") + view.Keys.Values["单机测试"];
        testLabel.Visible = testing;
        testTools.GetNode<Control>("SoloTestActions").Visible = testing;
        testLabel.Text = "模拟模式（不联网）· 当前操作：" + (testPeerView ? "模拟队友" : "本人")
            + "\n双方借用同一角色位置；轮盘发送、回应按钮均可操作。回合结束请求过期。";
    }
    private void ResetTest()
    {
        if (!testing) return;
        testing = testPeerView = false;
        testBoard.Clear();
        testPresentation = new();
        replyAudio?.Stop();
        displayed = board.Requests;
    }
    private void ToggleTest()
    {
        if (!CanTest || !CanUseCombatOverlay()) return;
        ClosePartyInspector();
        view.Close();
        replyAudio?.Stop();
        if (testing)
        {
            ResetTest();
            Render();
            UpdateTestTools();
        }
        else
        {
            testing = true;
            RefreshTest();
        }
    }
    private void RefreshTest()
    {
        displayed = testBoard.Requests; Render(); UpdateTestTools();
    }
    private HashSet<ulong> TestPlayers() => new() { net!.NetId, TestPeer };
    private bool ApplyTest(ulong actor, Command command)
    {
        if (!testing || !CanTest || (command.Op == Operation.Create ? !CanOpenWheel() : !CanReply())) return false;
        var targets = TargetStates();
        // Only the sandbox changes logical ownership; no game creature is altered.
        if (command.Op == Operation.Create && targets.TryGetValue(command.Target, out var target) && target.Ally)
            targets[command.Target] = target with { PlayerId = actor };
        testTime = Math.Max(Now, testTime + 1.1);
        bool changed = testBoard.Apply(actor, command, TestPlayers(), targets, testTime);
        if (changed) RefreshTest();
        return changed;
    }
    private void SeedTest()
    {
        if (!testing || !CanTest || !CanOpenWheel()) return;
        ulong other = Viewer == TestPeer ? net!.NetId : TestPeer;
        var enemy = state!.Creatures.FirstOrDefault(c => c.IsEnemy && c.IsAlive && c.CombatId.HasValue);
        var self = state.Creatures.FirstOrDefault(c => c.Player?.NetId == net!.NetId && c.IsAlive && c.CombatId.HasValue);
        if (enemy != null) ApplyTest(other, new(Operation.Create, 0, Need.Weak, enemy.CombatId!.Value, default));
        if (self != null) ApplyTest(other, new(Operation.Create, 0, Need.RequestSl, self.CombatId!.Value, default));
    }
    private void TestReply(Reply reply)
    {
        if (!testing || !CanTest) return;
        ulong other = Viewer == TestPeer ? net!.NetId : TestPeer;
        foreach (var request in testBoard.Requests.Where(r => r.Active && r.Author == Viewer).ToArray())
            ApplyTest(other, new(Operation.Respond, request.Id, default, 0, reply));
    }
}


