using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace TacticalRequests;

internal sealed partial class CombatPanel
{
    private Control partyOverlay = null!;
    private VBoxContainer partyContent = null!;
    private HBoxContainer partyTabs = null!;
    private Label partyTitle = null!;
    private HBoxContainer partyPotions = null!;
    private ScrollContainer partyScroll = null!;
    private PanelContainer partyPanel = null!;
    private Button partyOpen = null!;
    private ulong? inspectedPlayer;
    private CardModel[] shownCards = [];
    private PotionModel?[] shownPotions = [];
    private readonly List<NCard> previewCards = new();
    private double nextPartyRefresh;
    private const string PartyPositionPath = "user://tactical_requests_inspector.json";
    private Vector2 partyPosition = new(300, 90);
    private Vector2 partyDragOffset;
    private bool partyDragging;
    private bool PartyOpen => partyOverlay?.Visible == true;

    private void BuildPartyInspector()
    {
        partyOpen = new Button { Name = "PartyInspectButton", Position = new Vector2(512, 90), TooltipText = "查看队友手牌和药水" };
        view.Root.AddChild(partyOpen); partyOpen.Pressed += TogglePartyInspector;
        partyOverlay = new Control { Name = "PartyInspector", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        view.Root.AddChild(partyOverlay); partyOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        LoadPartyPosition();
        var panel = new PanelContainer { Name = "PartyInspectPanel", Position = partyPosition }; partyPanel = panel;
        var style = WheelView.BoxStyle(); style.ContentMarginLeft = style.ContentMarginRight = 8; style.ContentMarginTop = style.ContentMarginBottom = 6;
        panel.AddThemeStyleboxOverride("panel", style); partyOverlay.AddChild(panel);
        var body = new VBoxContainer(); panel.AddChild(body);
        var heading = new HBoxContainer(); body.AddChild(heading);
        partyTitle = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(180,0),
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.Drag }; heading.AddChild(partyTitle);
        partyTitle.GuiInput += DragPartyInspector;
        partyPotions = new HBoxContainer(); heading.AddChild(partyPotions);
        AddButton(heading, "×", ClosePartyInspector).TooltipText = "关闭 · Esc";
        partyTabs = new HBoxContainer { Visible = false }; body.AddChild(partyTabs);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(580, 150), VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; partyScroll = scroll;
        body.AddChild(scroll);
        partyContent = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; scroll.AddChild(partyContent);
        partyOverlay.Resized += () => { if (PartyOpen) RefreshPartyInspector(true); };
        partyPanel.Resized += ClampPartyPosition;
    }
    private void ClosePartyInspector()
    {
        if (partyOverlay == null) return;
        StopPartyDrag();
        partyOverlay.Hide(); ClearPartyContent(); UpdatePartyVisibility(); UpdateTestTools();
    }
    private void ClearPartyContent()
    {
        previewCards.Clear(); shownCards = []; shownPotions = [];
        foreach (Node child in partyPotions.GetChildren()) { partyPotions.RemoveChild(child); child.QueueFree(); }
        foreach (Node child in partyContent.GetChildren()) { partyContent.RemoveChild(child); child.QueueFree(); }
    }
    private void TogglePartyInspector()
    {
        if (PartyOpen) { ClosePartyInspector(); return; }
        if (!CanUseCombatOverlay()) return;
        view.Close();
        foreach (Node child in partyTabs.GetChildren()) { partyTabs.RemoveChild(child); child.QueueFree(); }
        var peers = state!.Players.Where(p => p.NetId != net!.NetId).ToArray();
        if (peers.Length == 0) peers = state.Players.ToArray();
        if (!peers.Any(p => p.NetId == inspectedPlayer)) inspectedPlayer = peers.FirstOrDefault()?.NetId;
        foreach (var p in peers) AddButton(partyTabs, PlayerName(p.NetId), () => { inspectedPlayer = p.NetId; RefreshPartyInspector(true); });
        partyOverlay.Show(); UpdatePartyVisibility(); UpdateTestTools(); RefreshPartyInspector(true);
    }
    private void InspectTeammate(ulong id)
    {
        if (PartyOpen && inspectedPlayer == id) { ClosePartyInspector(); return; }
        if (!CanOpenWheel()) return;
        if (PartyOpen) ClosePartyInspector();
        inspectedPlayer = id; TogglePartyInspector();
    }
    private void UpdatePartyVisibility()
    {
        partyOpen.Visible = CanUseCombatOverlay() && !view.IsOpen && !PartyOpen;
        view.SetRequestCount(PartyOpen ? 0 : displayed.Count(r => r.Active));
    }
    private void TickPartyInspector()
    {
        UpdatePartyVisibility();
        if (!PartyOpen) return;
        ClampPartyPosition();
        if (net?.IsConnected != true || state == null) { ClosePartyInspector(); return; }
        if (Now >= nextPartyRefresh) { nextPartyRefresh = Now + 0.5; RefreshPartyInspector(false); }
    }
    private void RefreshPartyInspector(bool force)
    {
        Player? player = state?.Players.FirstOrDefault(p => p.NetId == inspectedPlayer);
        if (player == null) { ClosePartyInspector(); return; }
        var cards = player.PlayerCombatState?.Hand.Cards.ToArray() ?? [];
        var potions = player.PotionSlots.ToArray();
        partyTitle.Text = (state!.Players.Count() == 1 ? "单人预览 · " : "") + PlayerName(player.NetId) + $" · {cards.Length} 张";
        partyTitle.TooltipText = PlayerName(player.NetId) + "\n拖动标题移动面板并保存位置；点击牌面看详情，药水悬停看说明";
        if (!force && cards.SequenceEqual(shownCards) && potions.SequenceEqual(shownPotions))
        {
            foreach (var card in previewCards) card.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            return;
        }
        ClearPartyContent(); shownCards = cards; shownPotions = potions;
        partyPotions.AddChild(new Label { Text = $"药水 {potions.Count(p => p != null)}/{potions.Length}" });
        foreach (var potion in potions.Where(p => p != null).Cast<PotionModel>())
            partyPotions.AddChild(new TextureRect { Texture = potion.Image, CustomMinimumSize = new Vector2(26,26), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TooltipText = potion.Title.GetFormattedText() + "\n" + Regex.Replace(potion.DynamicDescription.GetFormattedText(), @"\[[^\]]*\]", "") });
        float width = Math.Min(780, partyOverlay.GetViewportRect().Size.X - 48);
        partyScroll.CustomMinimumSize = new Vector2(width, 150);
        partyPanel.Size = Vector2.Zero;
        if (cards.Length == 0) partyContent.AddChild(new Label { Text = "当前没有手牌" });
        var grid = new Control { CustomMinimumSize = new Vector2(width, 145) }; partyContent.AddChild(grid);
        float step = cards.Length > 1 ? Math.Min(116, (width - 116) / (cards.Length - 1)) : 116;
        for (int index = 0; index < cards.Length; index++)
        {
            var model = cards[index];
            var cell = new Control { Position = new Vector2(index * step, 0), Size = new Vector2(index == cards.Length - 1 ? 116 : step, 145), TooltipText = model.Title + "\n" + Regex.Replace(model.GetDescriptionForPile(PileType.Hand, null), @"\[[^\]]*\]", ""), MouseFilter = Control.MouseFilterEnum.Stop }; grid.AddChild(cell);
            cell.MouseEntered += () => cell.ZIndex = 10;
            cell.MouseExited += () => cell.ZIndex = 0;
            cell.GuiInput += input =>
            {
                if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) return;
                var snapshot = shownCards.ToList();
                int selected = snapshot.IndexOf(model);
                if (selected < 0) return;
                cell.AcceptEvent();
                ClosePartyInspector();
                MegaCrit.Sts2.Core.Nodes.NGame.Instance!.GetInspectCardScreen().Open(snapshot, selected, false);
            };
            var card = NCard.Create(model);
            if (card == null) { cell.AddChild(new Label { Text = model.Title }); continue; }
            cell.AddChild(card); card.Scale = new Vector2(0.32f, 0.32f); card.Position = new Vector2(58, 72);
            card.MouseFilter = Control.MouseFilterEnum.Ignore;
            foreach (var child in card.FindChildren("*", "Control", true, false).OfType<Control>()) child.MouseFilter = Control.MouseFilterEnum.Ignore;
            card.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal); previewCards.Add(card);
        }
    }

    private void DragPartyInspector(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (button.Pressed)
            {
                partyDragging = true;
                partyDragOffset = partyOverlay.GetGlobalTransformWithCanvas().AffineInverse() * button.GlobalPosition - partyPanel.Position;
            }
            else StopPartyDrag();
            partyTitle.AcceptEvent();
        }
        else if (input is InputEventMouseMotion motion && partyDragging)
        {
            partyPosition = partyOverlay.GetGlobalTransformWithCanvas().AffineInverse() * motion.GlobalPosition - partyDragOffset;
            ClampPartyPosition(); partyTitle.AcceptEvent();
        }
    }

    private void ClampPartyPosition()
    {
        var space = partyOverlay.GetViewportRect().Size - partyPanel.Size - new Vector2(8, 8);
        partyPosition = new Vector2(Mathf.Clamp(partyPosition.X, 8, Math.Max(8, space.X)),
            Mathf.Clamp(partyPosition.Y, 8, Math.Max(8, space.Y)));
        partyPanel.Position = partyPosition;
    }

    private void StopPartyDrag()
    {
        if (!partyDragging) return;
        partyDragging = false;
        using var file = Godot.FileAccess.Open(PartyPositionPath, Godot.FileAccess.ModeFlags.Write);
        if (file != null) file.StoreString(JsonSerializer.Serialize(new[] { partyPosition.X, partyPosition.Y }));
        else GD.PushWarning("[TacticalRequests] Could not save hand panel position.");
    }

    private void LoadPartyPosition()
    {
        try
        {
            using var file = Godot.FileAccess.Open(PartyPositionPath, Godot.FileAccess.ModeFlags.Read);
            var saved = file == null ? null : JsonSerializer.Deserialize<float[]>(file.GetAsText());
            if (saved is { Length: 2 } && saved.All(float.IsFinite)) partyPosition = new Vector2(saved[0], saved[1]);
        }
        catch { }
    }
}



