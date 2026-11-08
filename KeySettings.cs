using Godot;

namespace TacticalRequests;

internal sealed partial class CombatPanel
{
    private KeySettingsDialog keyDialog = null!;
    private bool KeySettingsOpen => keyDialog?.IsOpen == true;
    private void BuildKeySettings() => keyDialog = new(view.Root, view.Keys, RefreshKeyLabels, () => { view.Close(); ClosePartyInspector(); }, besideDrawPile: true);
    private void RefreshKeyLabels()
    {
        partyOpen.Text = "手牌 · " + view.Keys.Values["查看队友"];
    }
    private bool HandleKeySettings(InputEventKey key) => keyDialog.Handle(key);
}
