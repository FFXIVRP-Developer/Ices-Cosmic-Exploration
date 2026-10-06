using ICE.Utilities.Cosmic_Helper;
using FFXIVClientStructs.FFXIV.Client.Game;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using ECommons.EzSharedDataManager;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Fork;

/// <summary>
///     Fork item 1: a Cosmic relic tool is never handed in for its next stage while the class it belongs to cannot wear that
///     stage yet. Upstream hands it in as soon as the research is complete (Task_CheckState.HubActivityCheck, no level check):
///     on a Lv 57 Blacksmith that traded Prototype v0.3 (Lv 50) for v0.4 (Lv 60), every mission craft failed afterwards.
///     <para>
///         What the hand-in gives comes from the game data: WKSCosmoToolClass (row = job - 7) lists, per research stage, the
///         tool of that stage (v0.1 ... v0.8, Cosmic, v1.1-v1.4, Stellar, Stellar v1.x, Hyper, Hyper v1.x, of Stars). The research
///         stage the game reports counts the hand-ins done: at stage N the hand-in gives stage N's tool, and the class's
///         current tool (the highest stage tool on hand: worn, armoury chest, bags) must be exactly stage N-1's; the class
///         must be able to wear stage N's. No tool on hand is only allowed at research stage 0 (the hand-in gives the first
///         tool). Anything else (an older
///         tool on hand while the current one is with a retainer, in the armoire, a saddlebag ...; research or inventory not
///         loaded; no stage data) holds the hand-in.
///     </para>
///     <para>
///         The class is always the one whose relic is exchanged (user, 2026-10-05: "the handin must match the relic with the job
///         it belongs to"): never judged by the class worn alone. The NPC's class list is read by name (SelectRelicEntry), not
///         by counting positions, and the confirmation is refused when it names another class's tool. Checked when ICE decides
///         to go to the hub (ICE's job and the class worn), when the hand-in starts, before the class is picked and before the
///         confirmation.
///     </para>
/// </summary>
internal static class ForkRelicGuard
{
    private const string Tag = "[Fork: Relic guard]";

    private static readonly InventoryType[] Sources =
    [
        InventoryType.EquippedItems, InventoryType.ArmoryMainHand,
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
    ];

    // Per class: the tool item of each research stage, from the game data (WKSCosmoToolClass). Read once.
    private static readonly Dictionary<uint, List<uint>> stageTools = [];

    /// <summary>The tool item of each research stage for the class (index = stage); empty when the game data has none.</summary>
    public static List<uint> StageTools(uint job)
    {
        if (stageTools.TryGetValue(job, out var known)) return known;
        var tools = new List<uint>();
        if (job is >= 8 and <= 18 && Svc.Data.GetExcelSheet<WKSCosmoToolClass>().GetRowOrDefault(job - 7) is { } row)
            foreach (var stage in row.Stages)
            {
                if (stage.Item.RowId == 0) break;
                tools.Add(stage.Item.RowId);
            }
        return stageTools[job] = tools;
    }

    /// <summary>Why a relic hand-in for this class must wait; null when it may go ahead.</summary>
    public static unsafe string? Blocker(uint job)
    {
        if (job is < 8 or > 18) return $"job {job} is not a crafter or gatherer";
        var abbr = Svc.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(job)?.Abbreviation.ExtractText() ?? job.ToString();

        var manager = InventoryManager.Instance();
        if (manager is null) return "the inventory is not loaded yet";
        foreach (var type in Sources)
        {
            var container = manager->GetInventoryContainer(type);
            if (container is null || !container->IsLoaded) return "the inventory is not loaded yet";
        }

        var tools = StageTools(job);
        if (tools.Count == 0) return $"no Cosmic tool stages for {abbr} in the game data";
        var research = CosmicHelper.Cosmic_ClassInfo().TryGetValue(job, out var info) ? info : null;
        if (research is null || research.Stage_Next == 0) return "the Cosmic research data is not loaded yet";
        var stage = (int)research.Stage_Current;
        var level = Player.GetLevel((Job)job);

        // The highest stage tool on hand (normal or high quality).
        var onHand = -1;
        foreach (var type in Sources)
        {
            var container = manager->GetInventoryContainer(type);
            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot is null || slot->ItemId == 0) continue;
                var index = tools.IndexOf(slot->ItemId % 1_000_000);
                if (index > onHand) onHand = index;
            }
        }

        // The research stage counts the hand-ins done: at stage N the current tool is stage N-1's and the hand-in gives stage
        // N's (stage 0, no tool yet: the first hand-in gives v0.1; ICE's old flat cap of 17 stages matched the 17 tools up to
        // the Hypersaw). The tool on hand must be exactly that current tool; anything else is held.
        if (stage >= tools.Count) return null; // every stage done: nothing more to exchange for
        if (onHand < 0 && stage != 0)
            return $"{abbr} is at research stage {stage} but its current Cosmic tool ({ItemName(tools[stage - 1])}) is not in the worn gear, armoury chest or bags";
        if (onHand >= 0 && onHand != stage - 1)
            return $"{abbr}'s Cosmic tool on hand ({ItemName(tools[onHand])}) is not its current one for research stage {stage} ({(stage > 0 ? ItemName(tools[stage - 1]) : "none yet")}); the current tool may be stored away";
        var gives = stage;
        var needs = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(tools[gives])?.LevelEquip ?? 255;
        return level < needs
            ? $"{abbr} Lv {level} cannot wear what the hand-in gives ({ItemName(tools[gives])}, Lv {needs})"
            : null;
    }

    /// <summary>True when a hand-in may go ahead for ICE's job and the class worn (the hand-in registers the worn one).</summary>
    public static bool AllowsHandIn(uint selectedJob)
    {
        var why = Blocker(selectedJob) ?? Blocker((uint)Player.Job);
        if (why is null) return true;
        if (EzThrottler.Throttle("Fork relic guard: held", 60_000))
            IceLogging.Info($"Relic hand-in held: {why}.", Tag);
        return false;
    }

    /// <summary>
    ///     The NPC's class list entry for this class, found by its text (the class's name, its abbreviation or one of its stage
    ///     tools' names), never by position: upstream counted the unlocked classes before it, and a list that differs from that
    ///     count picks another class's relic. -1 when no single entry matches.
    /// </summary>
    public static int SelectRelicEntry(SelectIconString menu, uint job)
    {
        var classJob = Svc.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(job);
        var names = new List<string>();
        if (classJob is { } c)
        {
            names.Add(c.Name.ExtractText());
            names.Add(c.NameEnglish.ExtractText());
            names.Add(c.Abbreviation.ExtractText());
        }
        names.AddRange(StageTools(job).Select(ItemName));
        names.RemoveAll(string.IsNullOrWhiteSpace);

        var match = -1;
        for (var i = 0; i < menu.Entries.Length; i++)
        {
            var text = menu.Entries[i].Text ?? "";
            if (!names.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase))) continue;
            if (match >= 0) return -1; // two entries match: not sure which one
            match = i;
        }
        return match;
    }

    /// <summary>The confirmation names a Cosmic tool of another class (the wrong relic is about to go); null when it does not.</summary>
    public static string? OtherClassInPrompt(string prompt, uint job)
    {
        if (string.IsNullOrEmpty(prompt)) return null;
        for (uint other = 8; other <= 18; other++)
        {
            if (other == job) continue;
            foreach (var tool in StageTools(other))
            {
                var name = ItemName(tool);
                if (name.Length > 0 && prompt.Contains(name, StringComparison.OrdinalIgnoreCase))
                    return $"the confirmation names {name}, not a tool of the class being handed in";
            }
        }
        return null;
    }

    // ---- YesAlready held during a hand-in ----------------------------------------------------------------------------
    // Upstream only stops YesAlready while a mission is taken or abandoned; a YesAlready set to confirm prompts could answer
    // the exchange's confirmation before the guard's last check. Held (its shared "YesAlready.StopRequests") while any of the
    // hand-in's steps is in ICE's queue, given back as soon as none is: a stopped or aborted hand-in gives it back by itself.

    private const string YesAlreadyHolder = "ICE fork: relic hand-in";
    private static readonly string[] HandInSteps =
    [
        "Register Job Swap Class", "Checking to see if we need to swap jobs", "Heading to the relic NPC for turnin",
        "Talk to researchway", "Selecting Report", "Selecting the class to turnin on",
    ];
    private static long nextTickMs;
    private static bool holdingYesAlready;

    public static void Init() => Svc.Framework.Update += Tick;

    public static void Dispose()
    {
        Svc.Framework.Update -= Tick;
        HoldYesAlready(false);
    }

    private static void Tick(object _)
    {
        var now = Environment.TickCount64;
        if (now < nextTickMs) return;
        nextTickMs = now + 250;
        try
        {
            var inHandIn = HandInSteps.Contains(P.TaskManager.CurrentTask?.Name ?? "")
                           || P.TaskManager.Tasks.Any(t => HandInSteps.Contains(t.Name ?? ""));
            HoldYesAlready(inHandIn);
        }
        catch (Exception ex)
        {
            IceLogging.Error($"Relic guard tick: {ex.Message}", Tag);
        }
    }

    private static void HoldYesAlready(bool hold)
    {
        if (hold == holdingYesAlready) return;
        if (!EzSharedData.TryGet<HashSet<string>>("YesAlready.StopRequests", out var requests)) return;
        if (hold) requests.Add(YesAlreadyHolder);
        else requests.Remove(YesAlreadyHolder);
        holdingYesAlready = hold;
        IceLogging.Debug(hold ? "YesAlready held for the relic hand-in." : "YesAlready given back after the relic hand-in.", Tag);
    }

    /// <summary>A hand-in under way that must not go on: ICE's queue stopped, the NPC's menus shut, ICE starts over.</summary>
    public static bool? AbortHandIn(string why)
    {
        IceLogging.ChatError($"Relic hand-in stopped: {why}.", "[I.C.E. fork]");
        Task_HubActivities.RelicTurnin = false;
        CloseMenus();
        P.TaskManager.Abort();
        SchedulerMain.State = IceState.Start;
        return true;
    }

    private static string ItemName(uint item) => Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(item)?.Name.ExtractText() ?? "";

    private static unsafe void CloseMenus()
    {
        foreach (var name in new[] { "SelectYesno", "SelectIconString", "SelectString" })
            if (GenericHelpers.TryGetAddonByName<AtkUnitBase>(name, out var addon) && addon->IsVisible)
                addon->Close(true);
    }
}
