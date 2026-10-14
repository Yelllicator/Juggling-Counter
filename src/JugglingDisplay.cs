using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace STS2Count;

public static class JugglingDisplay
{
    private static bool _reportedNotificationError;
    private static readonly FieldInfo InternalData = AccessTools.Field(typeof(PowerModel), "_internalData")
        ?? throw new MissingFieldException(typeof(PowerModel).FullName, "_internalData");
    private static readonly Type CounterData = typeof(JugglingPower).GetNestedType("Data", BindingFlags.NonPublic)
        ?? throw new TypeLoadException("JugglingPower.Data is missing.");
    private static readonly FieldInfo AttacksPlayed = AccessTools.Field(CounterData, "attacksPlayedThisTurn")
        ?? throw new MissingFieldException(CounterData.FullName, "attacksPlayedThisTurn");
    private static readonly Action<PowerModel> NotifyDisplay = AccessTools.MethodDelegate<Action<PowerModel>>(
        AccessTools.Method(typeof(PowerModel), "InvokeDisplayAmountChanged")
        ?? throw new MissingMethodException(typeof(PowerModel).FullName, "InvokeDisplayAmountChanged"));

    public static void ValidateGameApi()
    {
        if (AttacksPlayed.FieldType != typeof(int))
            throw new NotSupportedException("Juggling's attack counter is no longer an integer.");
    }

    public static bool TryGetRemaining(JugglingPower power, out int remaining)
    {
        remaining = 0;
        // Only expose the existing count of publicly played attacks. Never write it,
        // read RNG/deck order, or keep a second counter that can drift from the game.
        if (!power.IsMutable || InternalData.GetValue(power) is not { } data)
            return false;

        int played = (int)AttacksPlayed.GetValue(data)!;
        remaining = played >= 3 ? 0 : 3 - Math.Max(0, played);
        return true;
    }

    public static bool ShouldHighlight(CardModel? card)
    {
        if (card is not { IsMutable: true, Type: CardType.Attack }
            || card.ShouldGlowRed
            || card.Owner?.PlayerCombatState?.Phase != PlayerTurnPhase.Play
            || card.Pile?.Type != PileType.Hand)
            return false;

        var power = card.Owner.Creature.GetPower<JugglingPower>();
        return power is { Amount: > 0 }
            && TryGetRemaining(power, out int remaining)
            && remaining == 1;
    }

    internal static void RefreshIfChanged(JugglingPower power, int before)
    {
        if (TryGetRemaining(power, out int after) && before != after)
        {
            try
            {
                NotifyDisplay(power);
            }
            catch (Exception exception)
            {
                // A broken UI subscriber must not turn a display mod into a failed
                // card play. Native gameplay exceptions are never caught here.
                if (!_reportedNotificationError)
                {
                    _reportedNotificationError = true;
                    Console.Error.WriteLine($"[STS2Count] Display refresh failed: {exception}");
                }
            }
        }
    }
}
