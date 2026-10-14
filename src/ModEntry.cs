using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace STS2Count;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string HarmonyId = "STS2Count.JugglingCounter";
    private static bool _initialized;

    public static void Initialize()
    {
        try
        {
            InstallPatches();
            GD.Print("[STS2Count] Juggling counter loaded (3 -> 2 -> 1 -> 0).");
        }
        catch (Exception exception)
        {
            // Roll back this mod's patches only if the game API has changed.
            new Harmony(HarmonyId).UnpatchAll(HarmonyId);
            GD.PushError($"[STS2Count] Disabled: incompatible game API. {exception}");
        }
    }

    // Also exercised by the offline assembly smoke tests, without starting Godot.
    public static void InstallPatches()
    {
        if (_initialized)
            return;

        JugglingDisplay.ValidateGameApi();
        new Harmony(HarmonyId).PatchAll(typeof(ModEntry).Assembly);
        _initialized = true;
    }
}
