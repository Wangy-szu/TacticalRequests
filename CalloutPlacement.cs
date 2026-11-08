using Godot;
namespace TacticalRequests;

internal static class CalloutPlacement
{
    // Split a connector at protected rectangles, leaving intent/HP text clear.
    internal static IEnumerable<(Vector2 Start, Vector2 End)> ClearSegments(Vector2 start, Vector2 end, IEnumerable<Rect2> blocked)
    {
        var delta = end - start;
        var cuts = new List<(float From, float To)>();
        foreach (var rect in blocked)
        {
            float from = 0, to = 1;
            bool hit = true;
            for (int axis = 0; axis < 2; axis++)
            {
                float s = axis == 0 ? start.X : start.Y, d = axis == 0 ? delta.X : delta.Y;
                float low = axis == 0 ? rect.Position.X : rect.Position.Y, high = axis == 0 ? rect.End.X : rect.End.Y;
                if (Math.Abs(d) < 0.001f) { if (s < low || s > high) { hit = false; break; } }
                else { float a = (low-s)/d, b = (high-s)/d; from = Math.Max(from,Math.Min(a,b)); to = Math.Min(to,Math.Max(a,b)); }
            }
            if (hit && from <= to) cuts.Add((from,to));
        }
        float cursor = 0;
        foreach (var cut in cuts.OrderBy(c => c.From))
        {
            if (cut.From > cursor) yield return (start + delta*cursor, start + delta*cut.From);
            cursor = Math.Max(cursor,cut.To);
        }
        if (cursor < 1) yield return (start + delta*cursor,end);
    }
    internal static Rect2 Bounds(Control control)
    {
        var t = control.GetGlobalTransformWithCanvas();
        var points = new[] { t * Vector2.Zero, t * new Vector2(control.Size.X, 0), t * control.Size, t * new Vector2(0, control.Size.Y) };
        var min = new Vector2(points.Min(p => p.X), points.Min(p => p.Y));
        var max = new Vector2(points.Max(p => p.X), points.Max(p => p.Y));
        return new Rect2(min, max - min);
    }
    internal static IEnumerable<Rect2> IntentBounds(Control intent)
    {
        if (!intent.IsVisibleInTree()) yield break;
        var children = intent.FindChildren("*", "Control", true, false).OfType<Control>().Where(c => c.IsVisibleInTree()).ToArray();
        if (children.Length == 0) yield break;
        // The parent spans a wide layout strip, not the visible icon/damage footprint.
        foreach (var child in children.Where(c => !c.GetChildren().OfType<Control>().Any(x => x.IsVisibleInTree())))
            yield return Bounds(child).Grow(12);
    }
    internal static Rect2? Find(Vector2 preferred, Vector2 size, Vector2 screen, IEnumerable<Rect2> blocked)
    {
        var obstacles = blocked.ToArray();
        float right = screen.X - size.X - 12, bottom = screen.Y - size.Y - 240;
        if (right < 12 || bottom < 90) return null;
        var candidates = new List<Vector2> { new(Mathf.Clamp(preferred.X, 12, right), Mathf.Clamp(preferred.Y, 90, bottom)) };
        foreach (var r in obstacles)
        {
            candidates.Add(new(Mathf.Clamp(preferred.X, 12, right), r.Position.Y - size.Y - 8));
            candidates.Add(new(r.Position.X - size.X - 8, Mathf.Clamp(preferred.Y, 90, bottom)));
            candidates.Add(new(r.End.X + 8, Mathf.Clamp(preferred.Y, 90, bottom)));
        }
        for (float y = 90; y <= bottom; y += 24)
            for (float x = 12; x <= right; x += 24) candidates.Add(new(x,y));
        foreach (var p in candidates.OrderBy(p => p.DistanceSquaredTo(preferred)))
        {
            if (p.X < 12 || p.Y < 90 || p.X > right || p.Y > bottom) continue;
            var rect = new Rect2(p, size);
            if (!obstacles.Any(r => r.Intersects(rect))) return rect;
        }
        return null;
    }
}
