using Godot;
using TacticalRequests.Core;

namespace TacticalRequests;

internal sealed partial class CombatPanel
{
    private sealed class RequestPresentation
    {
        public uint? FocusTarget;
        public int? ReplySelection;
        public int? NoticeRequest;
        public string NoticeText = "";
        public Color NoticeColor = Colors.White;
        public double NoticeUntil;
        public double LastReplySound;
        public readonly Dictionary<int, ulong?> KnownRequests = new();
        public readonly Dictionary<(int Request, ulong Player), Reply> KnownReplies = new();
        public readonly HashSet<int> ConfirmedAttacks = new();
    }

    private RequestPresentation normalPresentation = new();
    private RequestPresentation testPresentation = new();
    private RequestPresentation Presentation => testing ? testPresentation : normalPresentation;
    private Label requestNotice = null!;
    private static Color ReplyFeedbackColor(Reply reply) => new(reply == Reply.Take ? "7de0b3" : reply == Reply.Cannot ? "ff887d" : "ffd07d");
    private AudioStreamPlayer? replyAudio;
    private readonly Dictionary<Reply, AudioStreamWav> replyMusic = new();
    private void PlayReplySound(Reply reply)
    {
        if (Now - Presentation.LastReplySound < 0.4) return;
        Presentation.LastReplySound = Now;
        replyAudio ??= new AudioStreamPlayer { VolumeDb = -16 };
        if (replyAudio.GetParent() == null) view.Root.AddChild(replyAudio);
        if (!replyMusic.TryGetValue(reply, out var stream))
        {
            stream = new AudioStreamWav
            {
                Format = AudioStreamWav.FormatEnum.Format16Bits,
                MixRate = ReplyMusic.SampleRate,
                Data = ReplyMusic.Create(reply)
            };
            replyMusic.Add(reply, stream);
        }
        replyAudio.Stream = stream;
        replyAudio.Play();
    }
    private void UpdateRequestNotice()
    {
        if (requestNotice == null)
        {
            requestNotice = new Label { Name = "RequestNotice", Position = new Vector2(700,62), MouseFilter = Control.MouseFilterEnum.Ignore };
            requestNotice.Modulate = new Color("73dfcc"); view.Root.AddChild(requestNotice);
        }
        var presentation = Presentation;
        var knownRequests = presentation.KnownRequests;
        var knownReplies = presentation.KnownReplies;
        void Show(Request request, string text, Color color)
        {
            presentation.NoticeRequest = request.Id;
            presentation.NoticeText = text;
            presentation.NoticeColor = color;
            presentation.NoticeUntil = Now + 3;
        }
        foreach (var request in displayed.Where(r => r.Active))
        {
            if (!knownRequests.TryGetValue(request.Id,out var owner))
            {
                if (request.Author != Viewer)
                    Show(request, PlayerName(request.Author) + "：" + Needs.ShortText(request.Need) + " · 快捷键直接回应高亮申请", PlayerColor(request.Author));
            }
            else if (request.Owner.HasValue && owner != request.Owner)
                Show(request, PlayerName(request.Owner.Value) + " 已接手：" + Needs.ShortText(request.Need), PlayerColor(request.Owner.Value));
            knownRequests[request.Id] = request.Owner;
            foreach (var reply in request.Replies)
            {
                var key = (request.Id, reply.Key);
                bool changed = !knownReplies.TryGetValue(key, out var previous) || previous != reply.Value;
                knownReplies[key] = reply.Value;
                if (changed && request.Need == Need.Focus && reply.Value == Reply.Take && reply.Key != request.Author)
                    focusTarget = request.Target;
                if (!changed) continue;
                var color = reply.Value == Reply.Cannot ? new Color("ff887d") : reply.Value == Reply.Wait ? new Color("ffd07d") : PlayerColor(reply.Key);
                Show(request, (reply.Key == Viewer ? "已回应" : PlayerName(reply.Key)) + " · " + Needs.ShortText(request.Need) + " · " + Needs.ShortReplyText(request.Need, reply.Value), color);
                if (reply.Key != Viewer) PlayReplySound(reply.Value);
            }
        }
        foreach (int id in knownRequests.Keys.Where(id => !displayed.Any(r => r.Id == id)).ToArray()) knownRequests.Remove(id);
        foreach (var key in knownReplies.Keys.Where(key => !displayed.Any(request => request.Id == key.Request && request.Active)).ToArray()) knownReplies.Remove(key);
        presentation.ConfirmedAttacks.RemoveWhere(id => !displayed.Any(r => r.Id == id));
        foreach (var request in displayed.Where(r => r.AttackConfirmed))
            if (presentation.ConfirmedAttacks.Add(request.Id)) focusTarget = request.Target;
        if (presentation.NoticeRequest is int notice && !displayed.Any(r => r.Id == notice && r.Active))
            presentation.NoticeUntil = 0;
        requestNotice.Text = presentation.NoticeText;
        requestNotice.Modulate = presentation.NoticeColor;
        requestNotice.Visible = Now < presentation.NoticeUntil;
    }
}
