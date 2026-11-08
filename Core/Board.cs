namespace TacticalRequests.Core;

public enum Need { Vulnerable, Weak, Block, Focus, BreakBlock, HoldFire, BurstTarget, Strength, Draw, Energy, Heal, BurstSupport, Dexterity, Protect, ReduceStrength, Amplify, GiveCards, Plasma, Souls, RequestSl }
public enum Reply { Take, Cannot, Wait }
public enum Status { Open, Claimed, Done, Cancelled, Expired }
public enum Operation { Create, Respond, Cancel, Complete, RespondGroup }
public sealed record Command(Operation Op, int RequestId, Need Need, uint Target, Reply Reply);
public sealed record Target(uint Id, bool Ally, bool Alive, int Vulnerable, int Weak, int Block, ulong? PlayerId = null, int Strength = 0)
{
    public int Value(Need need) => need switch { Need.Vulnerable => Vulnerable, Need.Weak => Weak, Need.Block or Need.BreakBlock => Block, Need.Strength or Need.ReduceStrength => Strength, _ => 0 };
}
public static class Needs
{
    public static readonly Need[] Enemy = [Need.Focus, Need.Vulnerable, Need.Weak, Need.ReduceStrength, Need.Amplify, Need.HoldFire];
    public static readonly Need[] Self = [Need.RequestSl, Need.Block, Need.Strength, Need.Draw, Need.Energy, Need.Heal, Need.Dexterity, Need.BurstSupport];
    public static readonly Need[] EnemyPool = [..Enemy, Need.BreakBlock, Need.BurstTarget];
    public static readonly Need[] SelfPool = [..Self, Need.Protect, Need.GiveCards, Need.Plasma, Need.Souls];
    public static bool IsSelf(Need need) => SelfPool.Contains(need);
    public static bool AutoCompletes(Need need) => need is Need.Block or Need.Strength;
    public static bool IsTeam(Need need) => need is Need.Focus or Need.HoldFire or Need.BurstTarget or Need.BurstSupport or Need.RequestSl;
    public static string ReplyText(Need need, Reply reply) => reply switch {
        Reply.Take => need == Need.RequestSl ? "同意" : need == Need.Focus ? "好的" : "我来",
        Reply.Cannot => need == Need.RequestSl ? "打打看" : need == Need.Focus ? "暂时不行" : "做不到",
        _ => need == Need.Focus ? "想一下" : "等一下" };
    public static string ShortReplyText(Need need, Reply reply) => reply switch
    {
        Reply.Cannot when need != Need.RequestSl => "不行",
        Reply.Wait => need == Need.Focus ? "想想" : "稍等",
        _ => ReplyText(need, reply)
    };
    public static string Text(Need need) => need switch {
        Need.RequestSl => "申请 SL", Need.Vulnerable => "挂易伤", Need.Weak => "挂虚弱", Need.Block => "需要格挡",
        Need.Focus => "优先集火", Need.BreakBlock => "打破格挡", Need.HoldFire => "先别攻击",
        Need.BurstTarget => "爆发输出", Need.Strength => "需要力量", Need.Draw => "需要抽牌",
        Need.Energy => "需要能量", Need.Heal => "需要治疗", Need.BurstSupport => "需要爆发支援",
        Need.Dexterity => "需要敏捷", Need.Protect => "请帮我挡伤", Need.ReduceStrength => "降低力量", Need.Amplify => "协作增伤",
        Need.GiveCards => "需要补充手牌", Need.Plasma => "需要等离子", Need.Souls => "需要灵魂", _ => "未知需求"
    };
    public static string ShortText(Need need) => need switch
    {
        Need.RequestSl => "申请 SL", Need.Vulnerable => "易伤", Need.Weak => "虚弱", Need.Block => "格挡",
        Need.Focus => "集火", Need.BreakBlock => "破防", Need.HoldFire => "停手",
        Need.BurstTarget or Need.BurstSupport => "爆发", Need.Strength => "力量", Need.Draw => "抽牌",
        Need.Energy => "能量", Need.Heal => "治疗", Need.Dexterity => "敏捷", Need.Protect => "挡伤",
        Need.ReduceStrength => "降力", Need.Amplify => "增伤", Need.GiveCards => "补牌",
        Need.Plasma => "等离子", Need.Souls => "灵魂", _ => "未知"
    };
}
public sealed class Request
{
    public int Id { get; set; }
    public ulong Author { get; set; }
    public uint Target { get; set; }
    public Need Need { get; set; }
    public Status Status { get; set; }
    public ulong? Owner { get; set; }
    public int Baseline { get; set; }
    public bool AttackConfirmed { get; set; }
    public Dictionary<ulong, Reply> Replies { get; set; } = new();
    public bool Active => Status is Status.Open or Status.Claimed;
}

// Only the host mutates this board. Clients render complete, versioned snapshots.
public sealed class Board
{
    public const int Limit = 12;
    public List<Request> Requests { get; } = new();
    private int nextId;
    private readonly Dictionary<ulong, double> lastCreated = new();

    public bool Apply(ulong actor, Command command, IReadOnlySet<ulong> players,
        IReadOnlyDictionary<uint, Target> targets, double now)
    {
        if (!players.Contains(actor) || !Enum.IsDefined(command.Op)) return false;
        if (command.Op == Operation.Create)
        {
            if (!Enum.IsDefined(command.Need) || !targets.TryGetValue(command.Target, out var target)
                || (!target.Alive && !(target.Ally && command.Need == Need.RequestSl)) || target.Ally != Needs.IsSelf(command.Need)) return false;
            // A player can express only their own needs, even if a forged client targets an ally.
            if (target.Ally && target.PlayerId != actor) return false;
            if (lastCreated.TryGetValue(actor, out var previous) && now - previous < 1) return false;
            if (Requests.Any(r => r.Active && r.Author == actor && r.Target == command.Target && r.Need == command.Need)) return false;
            if (Requests.Count(r => r.Active && r.Author == actor) >= 3) return false;
            if (Requests.Count >= Limit)
            {
                var oldest = Requests.FirstOrDefault(r => !r.Active);
                if (oldest == null) return false;
                Requests.Remove(oldest);
            }
            var added = new Request { Id = ++nextId, Author = actor, Target = target.Id,
                Need = command.Need, Baseline = target.Value(command.Need) };
            // A later identical enemy request joins the existing agreement.
            if (!target.Ally)
            {
                var existing = Requests.FirstOrDefault(r => r.Active && r.Target == target.Id && r.Need == command.Need);
                if (existing != null)
                {
                    added.Replies = new(existing.Replies.Where(p => p.Key != actor));
                    if (existing.Owner != actor) added.Owner = existing.Owner;
                    if (added.Owner.HasValue) added.Status = Status.Claimed;
                }
            }
            Requests.Add(added);
            lastCreated[actor] = now;
            return true;
        }
        var request = Requests.Find(r => r.Id == command.RequestId);
        if (request == null || !request.Active) return false;
        if (command.Op == Operation.RespondGroup)
        {
            if (Needs.IsSelf(request.Need) || !Enum.IsDefined(command.Reply)) return false;
            var members = Requests.Where(r => r.Active && r.Target == request.Target && r.Need == request.Need && r.Author != actor).ToArray();
            if (!Needs.IsTeam(request.Need) && command.Reply == Reply.Take && members.Any(r => r.Owner.HasValue && r.Owner != actor)) return false;
            bool changed = false;
            foreach (var member in members)
                changed |= ApplyReply(member, actor, command.Reply);
            return changed;
        }
        if (command.Op == Operation.Complete)
        {
            if (request.Author != actor) return false;
            request.Status = Status.Done;
            return true;
        }
        if (command.Op == Operation.Cancel)
        {
            if (request.Author != actor) return false;
            request.Status = Status.Cancelled;
            return true;
        }
        if (!Needs.IsSelf(request.Need))
            return Apply(actor, command with { Op = Operation.RespondGroup }, players, targets, now);
        return ApplyReply(request, actor, command.Reply);
    }

    private static bool ApplyReply(Request request, ulong actor, Reply reply)
    {
        if (request.Author == actor || !Enum.IsDefined(reply)) return false;
        if (!Needs.IsTeam(request.Need) && reply == Reply.Take && request.Owner.HasValue && request.Owner != actor) return false;
        if (request.Replies.TryGetValue(actor, out var old) && old == reply) return false;
        request.Replies[actor] = reply;
        if (Needs.IsTeam(request.Need)) return true;
        if (reply == Reply.Take)
        {
            request.Owner = actor;
            request.Status = Status.Claimed;
        }
        else if (request.Owner == actor)
        {
            request.Owner = null;
            request.Status = Status.Open;
        }
        return true;
    }

    public bool RecordEnemyResult(ulong actor, uint target, Need need)
    {
        if (need is not (Need.Focus or Need.Vulnerable or Need.Weak or Need.ReduceStrength or Need.Amplify)) return false;
        var group = Requests.Where(r => r.Active && r.Target == target && r.Need == need).ToArray();
        if (!group.Any(r => r.Author != actor)) return false;
        foreach (var request in group)
        {
            request.Status = Status.Done;
            request.AttackConfirmed = need == Need.Focus;
        }
        return true;
    }

    public bool Observe(IReadOnlyDictionary<uint, Target> targets, IReadOnlySet<ulong> players, bool playerTurn)
    {
        bool changed = false;
        foreach (var r in Requests.Where(r => r.Active))
        {
            if (!playerTurn || !players.Contains(r.Author) || !targets.TryGetValue(r.Target, out var t) || (!t.Alive && !(t.Ally && r.Need == Need.RequestSl)))
            { r.Status = Status.Expired; changed = true; continue; }
            if (r.Owner is ulong owner && !players.Contains(owner))
            { r.Owner = null; r.Status = Status.Open; changed = true; }
            foreach (var peer in r.Replies.Keys.Where(id => !players.Contains(id)).ToArray())
            { r.Replies.Remove(peer); changed = true; }
            if (r.Need == Need.BreakBlock && t.Block <= 0
                || (Needs.AutoCompletes(r.Need) && t.Value(r.Need) > r.Baseline))
            { r.Status = Status.Done; changed = true; }
            else if (t.Value(r.Need) < r.Baseline) r.Baseline = t.Value(r.Need);
        }
        return changed;
    }

    public void Clear()
    {
        Requests.Clear();
        lastCreated.Clear();
        // IDs deliberately never repeat during this combat, including across rounds.
    }
}
