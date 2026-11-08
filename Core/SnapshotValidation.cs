namespace TacticalRequests.Core;

public static class SnapshotValidation
{
    public static bool IsValid(List<Request>? requests)
    {
        if (requests == null || requests.Count > Board.Limit) return false;
        var ids = new HashSet<int>();
        foreach (var r in requests)
        {
            if (r == null || r.Id <= 0 || !ids.Add(r.Id)
                || !Enum.IsDefined(r.Need) || !Enum.IsDefined(r.Status)
                || (r.AttackConfirmed && (r.Need != Need.Focus || r.Status != Status.Done))
                || r.Replies == null || r.Replies.Count > 16
                || r.Replies.Values.Any(reply => !Enum.IsDefined(reply))) return false;
        }
        return true;
    }
}
