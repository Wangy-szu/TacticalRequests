using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using TacticalRequests.Core;

namespace TacticalRequests;

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPowerAmountChanged))]
internal static class DebuffFeedback
{
    // The game calls this with the effective change for both a new power and a stack.
    static void Prefix(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        Need? need = power switch
        {
            VulnerablePower when (int)amount > 0 && power.Amount > 0 => Need.Vulnerable,
            WeakPower when (int)amount > 0 && power.Amount > 0 => Need.Weak,
            StrengthPower when (int)amount < 0 => Need.ReduceStrength,
            KnockdownPower or FlankingPower when (int)amount > 0 && power.Amount > 1 => Need.Amplify,
            _ => null
        };
        if (!need.HasValue) return;
        // The multiplier excludes the original applier even if another effect changes its amount.
        if (need == Need.Amplify && power.Applier != null) applier = power.Applier;
        var player = applier?.Player ?? applier?.PetOwner ?? cardSource?.Owner;
        if (player != null)
            CombatPanel.Active?.ConfirmEnemyResult(player.NetId, power.Owner, need.Value);
    }
}
