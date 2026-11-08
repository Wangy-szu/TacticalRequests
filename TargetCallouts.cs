using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace TacticalRequests;

internal sealed class TargetCallouts
{
    private readonly Control root;
    private readonly Dictionary<uint, Vector2> anchors = new();
    private readonly Dictionary<uint, Vector2> anchorSizes = new();
    private Rect2[] protectedRects = Array.Empty<Rect2>();
    private readonly List<(uint Id, PanelContainer Panel, Control Highlight, Control? FocusRing, Color Color, bool Ally, bool ReticleOnly, ulong LayoutAfter)> items = new();
    public TargetCallouts(Control root) { this.root = root; }
    public void Clear()
    {
        foreach (var item in items.Where(item => item.ReticleOnly))
        {
            item.Highlight.GetParent().RemoveChild(item.Highlight);
            item.Highlight.QueueFree();
        }
        items.Clear();
    }
    public VBoxContainer Add(uint id, string title, bool ally, bool active, bool selected = false, Color? identityColor = null, bool focusAccepted = false, bool reticleOnly = false)
    {
        Color color = new(ally ? "73dfcc" : "ffcf70");
        if (!active) color = new Color("8b9a9c");
        var marker = new Control { Name = "TargetHighlight" + id, ZIndex = -1, MouseFilter = Control.MouseFilterEnum.Ignore };
        if (reticleOnly) root.GetParent().GetParent().AddChild(marker);
        else root.AddChild(marker);
        Control? focusRing = null;
        if (!ally && focusAccepted)
        {
            focusRing = new Control { Name = "FocusGroundRing", MouseFilter = Control.MouseFilterEnum.Ignore };
            marker.AddChild(focusRing);
            var ring = focusRing;
            ring.Draw += () =>
            {
                float radiusX = ring.Size.X / 2;
                float radiusY = ring.Size.Y / 2;
                var center = ring.Size / 2;
                var origin = marker.Position + ring.Position;
                var points = new Vector2[64];
                for (int index = 0; index < points.Length; index++)
                {
                    float angle = Mathf.Tau * index / points.Length;
                    points[index] = center + new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
                }
                var bounds = new Rect2(origin, ring.Size);
                float pulse = Mathf.Sin(Time.GetTicksMsec() / 450f);
                var reticleColor = new Color("ff3b45") { A = 0.90f + 0.08f * pulse };
                void Stroke(Vector2 from, Vector2 to, float width = 2)
                {
                    foreach (var segment in CalloutPlacement.ClearSegments(origin + from, origin + to, protectedRects))
                    {
                        ring.DrawLine(segment.Start - origin, segment.End - origin, new Color(0.12f, 0.01f, 0.02f, 0.85f), width + 2, true);
                        ring.DrawLine(segment.Start - origin, segment.End - origin, reticleColor, width, true);
                    }
                }
                if (!protectedRects.Any(rect => rect.Intersects(bounds)))
                {
                    ring.DrawPolyline([.. points, points[0]], new Color(0.12f, 0.01f, 0.02f, 0.85f), 5, true);
                    ring.DrawPolyline([.. points, points[0]], reticleColor, 2.5f, true);
                }
                else for (int index = 0; index < points.Length; index++)
                {
                    var next = points[(index + 1) % points.Length];
                    Stroke(points[index], next, 2.5f);
                }
                for (int index = 0; index < 16; index++)
                {
                    float angle = Mathf.Tau * index / 16;
                    var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    float inner = index % 4 == 0 ? 0.68f : 0.84f;
                    Stroke(center + direction * radiusX * inner, center + direction * radiusX * 0.96f);
                }
                for (int index = 0; index < 4; index++)
                {
                    float angle = Mathf.Tau * index / 4;
                    var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    Stroke(center + direction * 5, center + direction * Math.Max(10, radiusX * 0.36f));
                }
            };
        }
        var panel = new PanelContainer { Name = (reticleOnly ? "FocusAnchor" : "TargetRequest") + id, CustomMinimumSize = new Vector2(ally ? 340 : 380, 0), TooltipText = title };
        var style = WheelView.BoxStyle(); style.BorderColor = color;
        if (ally) { style.ContentMarginTop = style.ContentMarginBottom = 4; style.ContentMarginLeft = style.ContentMarginRight = 8; }
        style.BorderWidthBottom = style.BorderWidthTop = style.BorderWidthLeft = style.BorderWidthRight = selected ? 4 : 2;
        if (selected) style.BorderColor = Colors.White;
        panel.AddThemeStyleboxOverride("panel", style); root.AddChild(panel);
        var content = new VBoxContainer(); panel.AddChild(content);
        content.AddChild(new Label { Visible = false, Text = (selected ? "▶ " : "") + (ally ? title : "怪物目标 · " + title), Modulate = identityColor ?? color,
            CustomMinimumSize = new Vector2(ally ? 320 : 436,0), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            TooltipText = "▶ 为当前选中申请；快捷键可在「按键设置」中查看和修改" });
        var rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; content.AddChild(rows);
        rows.AddThemeConstantOverride("separation", 6);
        marker.Draw += () => {
            if (reticleOnly) return;
            float alpha = active ? 0.68f + 0.22f * Mathf.Sin(Time.GetTicksMsec() / 350f) : 0.3f;
            void Line(Vector2 from, Vector2 to, float width) {
                foreach (var segment in CalloutPlacement.ClearSegments(from,to,protectedRects))
                    marker.DrawLine(segment.Start-marker.Position,segment.End-marker.Position,new Color(color,alpha),width,true);
            }
            // Ground marker cannot cover the intent above a differently shaped monster.
            if (!ally && !focusAccepted) Line(marker.Position + new Vector2(0,marker.Size.Y-4),marker.Position+new Vector2(marker.Size.X,marker.Size.Y-4),3);
            var end = new Vector2(Mathf.Clamp(marker.Position.X + marker.Size.X / 2, panel.Position.X + 12, panel.Position.X + panel.Size.X - 12), panel.Position.Y + panel.Size.Y);
            if (ally)
            {
                int count = NCombatRoom.Instance?.CreatureNodes.Count(n => n.Entity.IsPlayer) ?? 1;
                var anchor = marker.Position + new Vector2(marker.Size.X / 2, 24);
                anchor.Y = Math.Max(anchor.Y, 150 + count * 70);
                var elbow = new Vector2(end.X, anchor.Y);
                Line(anchor,elbow,2);
                Line(elbow,end,2);
                if (!protectedRects.Any(r => r.HasPoint(anchor))) marker.DrawCircle(anchor - marker.Position, 3, color);
            }
            else
            {
                bool toLeft = marker.Position.X + marker.Size.X / 2 < panel.Position.X + panel.Size.X / 2;
                var anchor = marker.Position + new Vector2(toLeft ? marker.Size.X : 0, marker.Size.Y * 0.65f);
                var start = new Vector2(Mathf.Clamp(anchor.X, panel.Position.X + 8, panel.Position.X + panel.Size.X - 8), panel.Position.Y + panel.Size.Y);
                Line(start,anchor,2);
                if (!protectedRects.Any(r => r.HasPoint(anchor))) marker.DrawCircle(anchor - marker.Position, 5, color);
            }
        };
        items.Add((id, panel, marker, focusRing, color, ally, reticleOnly, Engine.GetProcessFrames() + 2));
        return rows;
    }
    public void Update()
    {
        var room = NCombatRoom.Instance;
        if (room == null) return;
        var occupied = new List<Rect2>();
        var party = room.CreatureNodes.Where(n => n.Entity.IsPlayer).OrderBy(n => n.GlobalPosition.Y).ThenBy(n => n.GlobalPosition.X).ToArray();
        float headTop = party.Length == 0 ? 400 : party.Min(n => n.Hitbox.GetGlobalTransformWithCanvas().Origin.Y);
        var screen = root.GetViewportRect().Size;
        var intents = room.CreatureNodes.Where(n => n.Entity.IsEnemy && n.Entity.IsAlive)
            .SelectMany(n => CalloutPlacement.IntentBounds(n.IntentContainer)).ToArray();
        var health = room.CreatureNodes.Select(n => n.GetNodeOrNull<Control>("%HealthBar"))
            .Where(c => c != null && c.IsVisibleInTree()).Select(c => CalloutPlacement.Bounds(c!).Grow(12)).ToArray();
        var damage = VisualProtection.DamageBounds();
        protectedRects = intents.Concat(health).Concat(damage).ToArray();
        foreach (var item in items.OrderBy(i => anchors.ContainsKey(i.Id) ? 0 : 1).ThenBy(i => i.Ally ? 0 : 1).ThenBy(i => i.Id))
        {
            var node = room.CreatureNodes.FirstOrDefault(n => n.Entity.CombatId == item.Id);
            bool visible = node != null && GodotObject.IsInstanceValid(node)
                && (node.Entity.IsAlive ? node.IsVisibleInTree() : item.Ally);
            item.Panel.Visible = item.Highlight.Visible = visible;
            if (item.ReticleOnly) item.Panel.Hide();
            if (!visible) continue;
            // Nested containers report provisional minimum sizes during rebuilding.
            // Keep the saved anchor until their first layout passes have completed.
            if (Engine.GetProcessFrames() < item.LayoutAfter) {
                if (anchors.TryGetValue(item.Id,out var settled)) item.Panel.Position = settled;
                else item.Panel.Hide();
                item.Highlight.Hide();
                continue;
            }
            item.Panel.Modulate = Colors.White;
            item.Panel.MouseFilter = Control.MouseFilterEnum.Stop;
            var box = node!.Hitbox;
            var transform = box.GetGlobalTransformWithCanvas();
            var rect = new Rect2(transform * Vector2.Zero, transform * box.Size - transform * Vector2.Zero).Abs();
            item.Highlight.Position = rect.Position - new Vector2(5, 5); item.Highlight.Size = rect.Size + new Vector2(10, 10);
            if (item.FocusRing is { } ring)
            {
                float radiusX = Mathf.Clamp(Math.Min(rect.Size.X, rect.Size.Y) * 0.42f, 24, 110);
                float radiusY = radiusX;
                ring.Size = new Vector2(radiusX * 2, radiusY * 2);
                var center = rect.GetCenter();
                // StateDisplay is a layout container. Its nested NHealthBar
                // supplies the actual bar bounds, including room for HP text.
                var targetHealth = node.FindChildren("*", "Control", true, false).OfType<NHealthBar>()
                    .Where(bar => bar.IsVisibleInTree()).Select(bar => CalloutPlacement.Bounds(bar).Grow(12));
                foreach (var bar in targetHealth)
                    center.Y = Math.Min(center.Y, bar.Position.Y - radiusY - 6);
                ring.Position = center - new Vector2(radiusX, radiusY) - item.Highlight.Position;
            }
            if (item.ReticleOnly)
            {
                item.FocusRing?.QueueRedraw();
                continue;
            }
            var size = item.Panel.Size.Max(item.Panel.GetCombinedMinimumSize());
            float x = Mathf.Clamp(rect.GetCenter().X - size.X / 2, 12, Math.Max(12, screen.X - size.X - 12));
            float y = Mathf.Clamp(rect.Position.Y - size.Y - 18, 100, Math.Max(100, screen.Y - size.Y - 250));
            if (item.Ally && party.Length >= 2)
            {
                int index = Array.FindIndex(party, n => n.Entity.CombatId == item.Id);
                int backCount = party.Length >= 3 ? party.Length - 2 : 0;
                bool back = index < backCount;
                int column = back ? index : index - backCount;
                x = 300 + column * 352;
                y = 90 + (party.Length >= 3 && !back ? 122 : 0);
                float center = party.Average(n => CalloutPlacement.Bounds(n.Hitbox).GetCenter().X);
                var enemyCenters = room.CreatureNodes.Where(n => n.Entity.IsEnemy && n.Entity.IsAlive)
                    .Select(n => CalloutPlacement.Bounds(n.Hitbox).GetCenter().X).ToArray();
                if (enemyCenters.Any(c => c < center) && enemyCenters.Any(c => c > center))
                {
                    x = Math.Max(party.Length >= 4 ? 704 : 668, center - 346) + column * 352;
                    y = 90 + (party.Length >= 3 && !back ? 122 : 0);
                }
            }
            var candidate = new Rect2(new Vector2(x, y), size);
            if (anchors.TryGetValue(item.Id, out var previous)) candidate.Position = previous;
            if (!item.Ally)
            {
                // Keep hostile requests on their target's side, outside the party column.
                var enemies = room.CreatureNodes.Where(n => n.Entity.IsEnemy && n.Entity.IsAlive)
                    .Select(n => CalloutPlacement.Bounds(n.Hitbox)).ToArray();
                float partyCenter = party.Length == 0 ? screen.X / 2 : party.Average(n => CalloutPlacement.Bounds(n.Hitbox).GetCenter().X);
                bool flanking = enemies.Any(r => r.GetCenter().X < partyCenter) && enemies.Any(r => r.GetCenter().X > partyCenter);
                float partyLeft = party.Length == 0 ? partyCenter : party.Min(n => CalloutPlacement.Bounds(n.Hitbox).Position.X);
                float partyRight = party.Length == 0 ? partyCenter : party.Max(n => CalloutPlacement.Bounds(n.Hitbox).End.X);
                if (flanking && !anchors.ContainsKey(item.Id))
                    candidate.Position = new Vector2(rect.GetCenter().X < partyCenter ? 275 : screen.X - size.X - 24, 100);
                var blocked = intents.Concat(occupied.Select(r => r.Grow(8)))
                    .Append(new Rect2(0, 90, 275, 100 + party.Length * 70)).ToArray();
                blocked = blocked.Concat(health).ToArray();
                if (flanking) blocked = blocked.Append(rect.GetCenter().X < partyCenter
                    ? new Rect2(screen.X / 2, 90, screen.X / 2, screen.Y - 90)
                    : new Rect2(0, 90, screen.X / 2, screen.Y - 90)).ToArray();
                // Leave extra space when choosing an anchor; small intent animations
                // may then move within that space without moving a reply button.
                var placementBlocked = blocked.Concat(intents.Select(r => r.Grow(20))).ToArray();
                var bodies = room.CreatureNodes.Where(n => n.Entity.IsAlive).Select(n => CalloutPlacement.Bounds(n.Hitbox)).ToArray();
                // Hitboxes omit protruding artwork; reserve a visual margin first.
                var visualBodies = enemies.Select(r => r.GrowIndividual(64, 96, 64, 24))
                    .Concat(party.Select(n => CalloutPlacement.Bounds(n.Hitbox)));
                var visual = CalloutPlacement.Find(candidate.Position, size, screen, placementBlocked.Concat(visualBodies));
                var exact = CalloutPlacement.Find(candidate.Position, size, screen, placementBlocked.Concat(bodies));
                // A generous art margin must not push a short request down onto health bars.
                var found = visual;
                if (exact.HasValue && (!visual.HasValue || visual.Value.Position.DistanceTo(candidate.Position) > exact.Value.Position.DistanceTo(candidate.Position) + 96))
                    found = exact;
                found ??= CalloutPlacement.Find(candidate.Position, size, screen,
                    placementBlocked.Concat(party.Select(player => CalloutPlacement.Bounds(player.Hitbox))));
                bool grew = !anchorSizes.TryGetValue(item.Id,out var savedSize) || size.X > savedSize.X + 0.5f || size.Y > savedSize.Y + 0.5f;
                var keepBlocked = grew ? placementBlocked : blocked;
                if (anchors.ContainsKey(item.Id) && candidate.Position.X >= 12 && candidate.Position.Y >= 90
                    && candidate.End.X <= screen.X - 12 && candidate.End.Y <= screen.Y - 240
                    && !keepBlocked.Concat(bodies).Any(r => r.Intersects(candidate))) found = candidate;
                if (found == null) { item.Panel.Position = new Vector2(-10000, -10000); item.Panel.Modulate = Colors.Transparent; item.Panel.MouseFilter = Control.MouseFilterEnum.Ignore; item.Highlight.Hide(); continue; }
                candidate = found.Value;
            }
            // Stack adjacent targets' bubbles upward, keeping a line to the exact target.
            for (int attempt = 0; item.Ally && attempt < 8 && occupied.Any(r => r.Grow(8).Intersects(candidate)); attempt++)
            {
                candidate.Position = new Vector2(candidate.Position.X, candidate.Position.Y - size.Y - 12);
                if (candidate.Position.Y < 100) candidate.Position = new Vector2(Mathf.Clamp(x + (attempt % 2 == 0 ? -1 : 1) * (size.X + 12), 12, Math.Max(12, screen.X - size.X - 12)), 100);
            }
            item.Panel.Position = candidate.Position; anchors[item.Id] = candidate.Position; anchorSizes[item.Id] = size; occupied.Add(candidate); item.Highlight.QueueRedraw();
            item.FocusRing?.QueueRedraw();
            // A passing damage number takes visual priority without relocating a reply.
            if (damage.Any(r => r.Intersects(candidate))) {
                item.Panel.Hide(); item.Panel.MouseFilter = Control.MouseFilterEnum.Ignore;
                item.Highlight.Hide();
            }
        }
    }
}


