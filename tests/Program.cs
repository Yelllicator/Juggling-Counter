using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using STS2Count;

// Managed-only integration tests against the user's real v0.111.0 game assembly.
// Synthetic fixtures contain publicly played attack counts only. Constructors
// that initialize Godot, player RNG, save files or a running game are not invoked.
internal static class Program
{
    private static readonly List<object> Results = [];
    private static int _failed;
    private static int _assertions;
    private static readonly Stopwatch Timer = Stopwatch.StartNew();

    private static async Task<int> Main(string[] args)
    {
        Timer.Restart();
        Run("manifest_parses_with_native_loader_contract", () =>
        {
            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "STS2Count.json"));
            var manifest = ModManifest.ReadFromStream(stream, out var errors);
            True(manifest is not null, "native manifest parser returns a manifest");
            Eq("STS2Count", manifest!.id, "loader DLL id");
            True(manifest.hasDll && !manifest.hasPck && !manifest.affectsGameplay, "DLL-only presentation manifest flags");
            True(errors is null || errors.Count == 0, "native manifest parser reports no errors");
            True(manifest.dependencies is null || manifest.dependencies.Count == 0, "no external mod dependencies");
            Eq("0.111.0", manifest.minGameVersion, "tested game minimum");
        });

        Run("real_harmony_targets_and_idempotence", () =>
        {
            ModEntry.InstallPatches();
            MethodBase[] targets = [
                AccessTools.PropertyGetter(typeof(PowerModel), nameof(PowerModel.DisplayAmount)),
                AccessTools.Method(typeof(JugglingPower), nameof(JugglingPower.AfterApplied)),
                AccessTools.Method(typeof(JugglingPower), nameof(JugglingPower.BeforeCardPlayed)),
                AccessTools.Method(typeof(JugglingPower), nameof(JugglingPower.AfterSideTurnEnd)),
                AccessTools.PropertyGetter(typeof(NHandCardHolder), "ShouldGlowGold")];
            foreach (var target in targets)
                Eq(1, OwnedPostfixes(target), target.ToString()!);
            Eq(5, Harmony.GetAllPatchedMethods().Count(m => Harmony.GetPatchInfo(m)?.Owners.Contains(ModEntry.HarmonyId) == true), "exact target count");
            ModEntry.InstallPatches();
            foreach (var target in targets)
                Eq(1, OwnedPostfixes(target), "initialization must not duplicate " + target);
        });

        Run("patched_native_display_count_and_stack_preservation", () =>
        {
            foreach (var (played, expected) in new[] { (0, 3), (1, 2), (2, 1), (3, 0), (4, 0), (9, 0), (-1, 3) })
            {
                var power = Power(played, amount: 7);
                Eq(expected, ReadNativeDisplay(power), $"native DisplayAmount after {played} attacks");
                Eq(7, power.Amount, "gameplay copy count remains untouched");
                Eq(played, ReadCounter(power), "native attack counter remains untouched");
            }
            var canonical = Power(2, amount: 8, mutable: false);
            Eq(8, ReadNativeDisplay(canonical), "canonical display is preserved");
            var missingData = Power(2, amount: 6);
            Set(missingData, "_internalData", null);
            Eq(6, ReadNativeDisplay(missingData), "uninitialized mutable display is preserved");
            var unrelated = Uninitialized<StrengthPower>();
            Set(unrelated, "_amount", 11);
            Eq(11, ReadNativeDisplay(unrelated), "unrelated native power is preserved");
        });

        await RunAsync("native_attack_hooks_notify_without_mutating_gameplay", async () =>
        {
            var f = Fixture(0, 2);
            int notifications = 0;
            f.Power.DisplayAmountChanged += () => notifications++;
            await f.Power.BeforeCardPlayed(Play(f.Attack, f.Player));
            Eq(1, ReadCounter(f.Power), "native first attack increments");
            Eq(2, ReadNativeDisplay(f.Power), "patched first attack display");
            Eq(1, notifications, "first attack refresh");
            await f.Power.BeforeCardPlayed(Play(f.Attack, f.Player));
            Eq(2, ReadCounter(f.Power), "native second attack increments");
            Eq(1, ReadNativeDisplay(f.Power), "patched second attack display");
            Eq(2, notifications, "second attack refresh");
            await f.Power.BeforeCardPlayed(Play(f.Skill, f.Player));
            Eq(2, ReadCounter(f.Power), "skill is excluded by native hook");
            Eq(2, notifications, "skill does not issue refresh");
            var other = Fixture(0, 1);
            await f.Power.BeforeCardPlayed(Play(other.Attack, other.Player));
            Eq(2, ReadCounter(f.Power), "other player's attack is excluded");
            Eq(2, notifications, "other player's attack does not refresh");
            Eq(2, f.Power.Amount, "copy count unchanged across hooks");
            // Avoid the third-attack clone/animation path, which needs Godot.
            SetCounter(f.Power, 3);
            await f.Power.BeforeCardPlayed(Play(f.Attack, f.Player));
            Eq(4, ReadCounter(f.Power), "native fourth attack increments");
            Eq(0, ReadNativeDisplay(f.Power), "counter stays exhausted");
            Eq(2, notifications, "exhausted display does not refresh unnecessarily");
        });

        await RunAsync("native_turn_end_resets_and_emits_only_needed_refresh", async () =>
        {
            var f = Fixture(2, 3);
            int notifications = 0;
            f.Power.DisplayAmountChanged += () => notifications++;
            await f.Power.AfterSideTurnEnd(null!, CombatSide.Enemy, []);
            Eq(2, ReadCounter(f.Power), "non-participant does not reset");
            Eq(0, notifications, "non-participant does not refresh");
            await f.Power.AfterSideTurnEnd(null!, CombatSide.Player, [f.Creature]);
            Eq(0, ReadCounter(f.Power), "native participant turn-end resets");
            Eq(3, ReadNativeDisplay(f.Power), "next-turn display reset");
            Eq(1, notifications, "reset refresh reaches actual power event");
            await f.Power.AfterSideTurnEnd(null!, CombatSide.Player, [f.Creature]);
            Eq(1, notifications, "same display is not refreshed twice");
            Eq(3, f.Power.Amount, "stack amount survives reset");
        });

        await RunAsync("bad_display_subscriber_cannot_break_native_card_hook", async () =>
        {
            var f = Fixture(0, 2);
            f.Power.DisplayAmountChanged += () => throw new InvalidOperationException("synthetic UI subscriber failure");
            var oldError = Console.Error;
            using var capturedError = new StringWriter();
            try
            {
                Console.SetError(capturedError);
                await f.Power.BeforeCardPlayed(Play(f.Attack, f.Player));
            }
            finally { Console.SetError(oldError); }
            Eq(1, ReadCounter(f.Power), "game counter still advanced");
            Eq(2, f.Power.Amount, "copy count preserved");
            True(capturedError.ToString().Contains("Display refresh failed"), "subscriber failure was reported");
        });

        Run("highlight_public_state_boundaries", () =>
        {
            var f = Fixture(2, 2);
            True(JugglingDisplay.ShouldHighlight(f.Attack), "third attack in own hand highlights");
            True(!JugglingDisplay.ShouldHighlight(f.Skill), "skills never highlight");
            True(!JugglingDisplay.ShouldHighlight(null), "null card is safe");
            foreach (int played in new[] { 0, 1, 3, 4, 9 })
            {
                SetCounter(f.Power, played);
                True(!JugglingDisplay.ShouldHighlight(f.Attack), $"played={played} is not armed");
            }
            SetCounter(f.Power, 2);
            f.Combat.Phase = PlayerTurnPhase.None;
            True(!JugglingDisplay.ShouldHighlight(f.Attack), "outside player's play phase");
            f.Combat.Phase = PlayerTurnPhase.Play;
            HandCards(f.Hand).Remove(f.Attack);
            True(!JugglingDisplay.ShouldHighlight(f.Attack), "card outside hand is excluded");
            HandCards(f.Hand).Add(f.Attack);
            Set(f.Power, "_amount", 0);
            True(!JugglingDisplay.ShouldHighlight(f.Attack), "zero-stack effect is inactive");
            Set(f.Power, "_amount", 2);
            Set(f.Attack, "<IsMutable>k__BackingField", false);
            True(!JugglingDisplay.ShouldHighlight(f.Attack), "canonical card is excluded before owner lookup");
            Set(f.Attack, "<IsMutable>k__BackingField", true);
            var red = MakeCard<RedAttack>(f.Player, CardType.Attack);
            HandCards(f.Hand).Add(red);
            True(red.ShouldGlowRed, "fixture exercises actual native red-glow getter");
            True(!JugglingDisplay.ShouldHighlight(red), "red warning wins over juggling");
            var other = Fixture(0, 2);
            True(!JugglingDisplay.ShouldHighlight(other.Attack), "different owner uses its own power count");
            Powers(f.Creature).Clear();
            True(!JugglingDisplay.ShouldHighlight(f.Attack), "no power means no highlight");
        });

        Timer.Stop();
        string reportPath = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "verification.json"));
        var report = new
        {
            schema = "sts2count.offline-integration.v1",
            evidence_time_utc = DateTimeOffset.UtcNow,
            status = _failed == 0 ? "PASSED" : "FAILED",
            elapsed_seconds = Timer.Elapsed.TotalSeconds,
            groups_passed = Results.Count - _failed,
            groups_failed = _failed,
            assertions_passed = _assertions,
            game_assembly = typeof(PowerModel).Assembly.Location,
            game_sha256 = Hash(typeof(PowerModel).Assembly.Location),
            mod_sha256 = Hash(typeof(ModEntry).Assembly.Location),
            tests = Results,
            limitations = new[] { "No game process or Godot renderer was started.", "Third-attack cloning, AfterApplied history lookup, actual hand-holder getter invocation and visual glow were not executed; their patches were registered against the actual game DLL.", "Fixtures use synthetic public state with constructors bypassed; no save, RNG, hidden deck order or actual run data is read." }
        };
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{report.status}: {report.groups_passed}/{Results.Count} groups; {reportPath}");
        return _failed == 0 ? 0 : 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ReadNativeDisplay(PowerModel power) => power.DisplayAmount;
    private static int OwnedPostfixes(MethodBase target) => Harmony.GetPatchInfo(target)?.Postfixes.Count(p => p.owner == ModEntry.HarmonyId) ?? 0;
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static T Uninitialized<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Set(object value, string field, object? data) => (AccessTools.Field(value.GetType(), field) ?? throw new MissingFieldException(value.GetType().Name, field)).SetValue(value, data);
    private static object? Get(object value, string field) => (AccessTools.Field(value.GetType(), field) ?? throw new MissingFieldException(value.GetType().Name, field)).GetValue(value);
    private static void SetCounter(JugglingPower p, int count) => Set(Get(p, "_internalData")!, "attacksPlayedThisTurn", count);
    private static int ReadCounter(JugglingPower p) => (int)Get(Get(p, "_internalData")!, "attacksPlayedThisTurn")!;
    private static JugglingPower Power(int count, int amount, bool mutable = true)
    {
        var p = Uninitialized<JugglingPower>();
        Set(p, "<IsMutable>k__BackingField", mutable);
        Set(p, "_amount", amount);
        var data = RuntimeHelpers.GetUninitializedObject(typeof(JugglingPower).GetNestedType("Data", BindingFlags.NonPublic)!);
        Set(p, "_internalData", data);
        SetCounter(p, count);
        return p;
    }
    private static List<CardModel> HandCards(CardPile pile) => (List<CardModel>)Get(pile, "_cards")!;
    private static List<PowerModel> Powers(Creature creature) => (List<PowerModel>)Get(creature, "_powers")!;
    private static T MakeCard<T>(Player owner, CardType type) where T : CardModel
    {
        var c = Uninitialized<T>();
        Set(c, "<IsMutable>k__BackingField", true);
        Set(c, "<Type>k__BackingField", type);
        Set(c, "_owner", owner);
        return c;
    }
    private static (JugglingPower Power, Player Player, Creature Creature, PlayerCombatState Combat, CardPile Hand, CardModel Attack, CardModel Skill) Fixture(int count, int amount)
    {
        var p = Uninitialized<Player>();
        var creature = Uninitialized<Creature>();
        var combat = Uninitialized<PlayerCombatState>();
        var hand = new CardPile(PileType.Hand);
        var power = Power(count, amount);
        Set(p, "<Creature>k__BackingField", creature);
        Set(p, "<PlayerCombatState>k__BackingField", combat);
        Set(p, "_runPiles", Array.Empty<CardPile>());
        Set(creature, "<Player>k__BackingField", p);
        Set(creature, "_powers", new List<PowerModel> { power });
        Set(combat, "_piles", new[] { hand });
        combat.Phase = PlayerTurnPhase.Play;
        Set(power, "_owner", creature);
        var attack = MakeCard<StrikeSilent>(p, CardType.Attack);
        var skill = MakeCard<DefendSilent>(p, CardType.Skill);
        HandCards(hand).AddRange([attack, skill]);
        return (power, p, creature, combat, hand, attack, skill);
    }
    private static CardPlay Play(CardModel card, Player player) => new()
    {
        Card = card, Player = player, Target = null, ResultPile = PileType.Discard,
        Resources = default!, IsAutoPlay = false, PlayIndex = 0, PlayCount = 1
    };
    private static void True(bool actual, string message) { if (!actual) throw new InvalidOperationException(message); _assertions++; }
    private static void Eq<T>(T expected, T actual, string message) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{message}: expected {expected}, got {actual}"); _assertions++; }
    private static void Run(string name, Action body) => RunAsync(name, () => { body(); return Task.CompletedTask; }).GetAwaiter().GetResult();
    private static async Task RunAsync(string name, Func<Task> body)
    {
        try { await body(); Results.Add(new { name, status = "PASSED" }); Console.WriteLine("PASS " + name); }
        catch (Exception e) { _failed++; Results.Add(new { name, status = "FAILED", error = e.ToString() }); Console.Error.WriteLine("FAIL " + name + ": " + e); }
    }
}

// The instance is allocated without this constructor. Only this public-facing
// glow override is synthetic; the tested ShouldGlowRed getter belongs to sts2.dll.
internal sealed class RedAttack : CardModel
{
    private RedAttack() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }
    protected override bool ShouldGlowRedInternal => true;
}
