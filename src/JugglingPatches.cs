using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace STS2Count;

[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.DisplayAmount), MethodType.Getter)]
internal static class JugglingAmountPatch
{
    private static void Postfix(PowerModel __instance, ref int __result)
    {
        if (__instance is JugglingPower power && JugglingDisplay.TryGetRemaining(power, out int remaining))
            __result = remaining;
    }
}

[HarmonyPatch]
internal static class JugglingRefreshPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(JugglingPower), nameof(JugglingPower.AfterApplied));
        yield return AccessTools.Method(typeof(JugglingPower), nameof(JugglingPower.BeforeCardPlayed));
        yield return AccessTools.Method(typeof(JugglingPower), nameof(JugglingPower.AfterSideTurnEnd));
    }

    private static void Prefix(JugglingPower __instance, out int __state)
    {
        __state = JugglingDisplay.TryGetRemaining(__instance, out int before) ? before : -1;
    }

    private static void Postfix(JugglingPower __instance, int __state)
    {
        // v0.111.0 updates its counter synchronously, before the first await even
        // when creating the third attack's copies. Refresh immediately, not when
        // the card-generation animation finishes. Never replace the returned Task.
        JugglingDisplay.RefreshIfChanged(__instance, __state);
    }
}

[HarmonyPatch(typeof(NHandCardHolder), "ShouldGlowGold", MethodType.Getter)]
internal static class JugglingHandGlowPatch
{
    private static void Postfix(NHandCardHolder __instance, NPlayerHand ____hand, ref bool __result)
    {
        // Selection dialogs keep their own highlight semantics. The existing UI
        // also retains red warning priority. Do not touch a CardModel's rules.
        if (__result || ____hand == null || ____hand.IsInCardSelection
            || ____hand.SelectModeGoldGlowOverride != null)
            return;

        // This marks the next-attack effect even if energy is currently insufficient;
        // CanPlay(), costs, targeting and input validation remain entirely native.
        __result = JugglingDisplay.ShouldHighlight(__instance.CardNode?.Model);
    }
}
