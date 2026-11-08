namespace TacticalRequests.Core;

public sealed class WheelLoadout
{
    public Need[] Self { get; set; } = [..Needs.Self];
    public Need[] Enemy { get; set; } = [..Needs.Enemy];
    public void Validate()
    {
        // Migrate the previous default while preserving deliberately customized wheels.
        if (Self is not null && Self.SequenceEqual(new[] { Need.Block, Need.Strength, Need.Draw, Need.Energy, Need.Heal, Need.Dexterity, Need.Protect, Need.BurstSupport })) Self = [..Needs.Self];
        if (Self is not null && Self.SequenceEqual(new[] { Need.RequestSl, Need.Block, Need.Strength, Need.Draw, Need.Energy, Need.Heal, Need.Protect, Need.BurstSupport })) Self = [..Needs.Self];
        if (!Valid(Self, Needs.SelfPool, 8)) Self = [..Needs.Self];
        if (!Valid(Enemy, Needs.EnemyPool, 6)) Enemy = [..Needs.Enemy];
    }
    private static bool Valid(Need[]? values, Need[] pool, int count) => values?.Length == count
        && values.Distinct().Count() == count && values.All(pool.Contains);
    public void Replace(bool self, int slot, Need need)
    {
        var values = self ? Self : Enemy;
        var pool = self ? Needs.SelfPool : Needs.EnemyPool;
        if (slot < 0 || slot >= values.Length || !pool.Contains(need)) return;
        int existing = Array.IndexOf(values, need);
        if (existing >= 0) values[existing] = values[slot];
        values[slot] = need;
    }
}
