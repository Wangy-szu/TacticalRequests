using Godot;
using TacticalRequests.Core;

namespace TacticalRequests;

internal sealed partial class CombatPanel
{
    private int? replySelection { get => Presentation.ReplySelection; set => Presentation.ReplySelection = value; }
    private bool CanReply() => CanUseCombatOverlay() && (IsHost || seenRevision >= 0)
        && state?.CurrentSide == MegaCrit.Sts2.Core.Combat.CombatSide.Player;
    private Request[] ReplyCandidates() => displayed.Where(r => r.Active)
        .GroupBy(r => (r.Target, r.Need)).Where(g => g.Any(r => r.Author != Viewer))
        .Select(g => g.OrderBy(r => r.Id).First()).OrderBy(r => r.Id).ToArray();

    private bool HandleReplyKey(InputEventKey key)
    {
        if (key.Echo || view.IsOpen || PartyOpen
            || !CanReply() || ui.GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return false;
        var code = key.PhysicalKeycode == Key.None ? key.Keycode : key.PhysicalKeycode;
        if (!KeyBindings.Actions.Take(4).Any(a => view.Keys.Is(a, key))) return false;
        var candidates = ReplyCandidates();
        if (view.Keys.Is("选择回应", key))
        {
            if (candidates.Length == 0) { replySelection = null; return false; }
            int index = Array.FindIndex(candidates, r => r.Id == replySelection);
            var selected = candidates[(index + 1) % candidates.Length];
            replySelection = selected.Id;
            var target = state?.Creatures.FirstOrDefault(creature => creature.CombatId == selected.Target);
            Presentation.NoticeRequest = selected.Id;
            Presentation.NoticeText = "当前回应 · " + (target?.Name ?? "目标") + " · " + Needs.ShortText(selected.Need)
                + " · " + PlayerName(selected.Author);
            Presentation.NoticeColor = PlayerColor(selected.Author);
            Presentation.NoticeUntil = Now + 3;
            Render(); return true;
        }
        var request = candidates.FirstOrDefault(r => r.Id == replySelection) ?? candidates.FirstOrDefault();
        if (request == null)
        {
            replySelection = null;
            Presentation.NoticeRequest = null;
            Presentation.NoticeText = "当前没有可回应的申请";
            Presentation.NoticeColor = new Color("ffd07d");
            Presentation.NoticeUntil = Now + 4;
            UpdateRequestNotice();
            return true;
        }
        if (replySelection != request.Id) { replySelection = request.Id; Render(); }
        var reply = view.Keys.Is("回应：我来／同意", key) ? Reply.Take : view.Keys.Is("回应：做不到／打打看", key) ? Reply.Cannot : Reply.Wait;
        var group = displayed.Where(r => r.Active && r.Target == request.Target && r.Need == request.Need);
        if (reply == Reply.Take && !Needs.IsTeam(request.Need) && group.Any(r => r.Author != Viewer && r.Owner.HasValue && r.Owner != Viewer)) return true;
        Submit(new(Needs.IsSelf(request.Need) ? Operation.Respond : Operation.RespondGroup, request.Id, default, 0, reply));
        return true;
    }
}
