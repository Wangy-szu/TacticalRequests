using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using TacticalRequests.Core;
using MegaCrit.Sts2.Core.Hooks;

namespace TacticalRequests;

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterAttack))]
internal static class AttackFeedback
{
    static void Prefix(AttackCommand command)
    {
        var player = command.Attacker?.Player ?? command.Attacker?.PetOwner ?? command.CardPlay?.Card.Owner;
        if (player == null) return;
        foreach (var result in command.Results.SelectMany(hit => hit))
            CombatPanel.Active?.ConfirmEnemyResult(player.NetId, result.Receiver, Need.Focus);
    }
}

internal sealed partial class CombatPanel
{
    private uint? focusTarget { get => Presentation.FocusTarget; set => Presentation.FocusTarget = value; }

    internal void ConfirmEnemyResult(ulong player, Creature enemy, Need need)
    {
        if (disposed || failed || state?.CurrentSide != CombatSide.Player
            || !enemy.IsEnemy || enemy.CombatId is not uint target || !state.Creatures.Contains(enemy)) return;
        if (testing)
        {
            if (player == net?.NetId && testBoard.RecordEnemyResult(player, target, need)) RefreshTest();
            return;
        }
        if (!IsHost || !Players().Contains(player)) return;
        if (board.RecordEnemyResult(player, target, need)) { revision++; Broadcast(); }
    }
}
