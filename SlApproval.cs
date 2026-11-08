using TacticalRequests.Core;

namespace TacticalRequests;

internal sealed record SlApproval(string Key, HashSet<ulong> Members, HashSet<ulong> Accepted)
{
    internal bool Unanimous => Members.Count > 1 && Accepted.SetEquals(Members);
    internal static HashSet<ulong> Acceptances(ulong author, Dictionary<ulong, Reply> replies) =>
        replies.Where(reply => reply.Value == Reply.Take).Select(reply => reply.Key).Append(author).ToHashSet();
}

internal sealed partial class CombatPanel
{
    internal IEnumerable<SlApproval> SlApprovals()
    {
        if (disposed || failed || testing || state == null || net?.IsConnected != true
            || !MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsInProgress) return Array.Empty<SlApproval>();
        return displayed.Where(request => request.Active && request.Need == Need.RequestSl)
            .Select(request => new SlApproval($"combat:{context}:{round}:{request.Id}", Players(), SlApproval.Acceptances(request.Author, request.Replies))).ToArray();
    }
    internal void SlNotice(string text)
    {
        Presentation.NoticeRequest = null; Presentation.NoticeText = text;
        Presentation.NoticeColor = new Godot.Color("ffd07d"); Presentation.NoticeUntil = Now + 6;
        UpdateRequestNotice();
    }
}
