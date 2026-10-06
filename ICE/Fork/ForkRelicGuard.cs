using ICE.Utilities.Cosmic_Helper;
using FFXIVClientStructs.FFXIV.Client.Game;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.Reflection;

namespace ICE.Fork;

/// <summary>
///     Fork item 1: a Cosmic relic tool is never handed in for its next stage while the class cannot wear that stage yet.
///     Upstream hands it in as soon as the research is complete (Task_CheckState.HubActivityCheck, no level check): on a
///     Lv 57 Blacksmith that traded Prototype v0.3 (Lv 50) for v0.4 (Lv 60), every mission craft failed afterwards.
///     The class's stage is its best Cosmic "Prototype" main tool owned (worn, armoury chest, bags); the next stage is the
///     next Prototype equip level the game data has for that class. When in doubt (inventory not loaded, no tool found
///     below the top level) the hand-in is held. Checked when ICE decides to go to the hub, when the hand-in starts and
///     right before each irreversible click at the NPC.
/// </summary>
internal static class ForkRelicGuard
{
    private const string Tag = "[Fork: Relic guard]";

    private static readonly InventoryType[] Sources =
    [
        InventoryType.EquippedItems, InventoryType.ArmoryMainHand,
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
    ];

    // The Prototype tools per class (item id -> equip level), read once from the game data (English names: language-independent).
    private static readonly Dictionary<uint, Dictionary<uint, int>> prototypes = [];

    /// <summary>Why a relic hand-in for this class must wait; null when it may go ahead.</summary>
    public static unsafe string? Blocker(uint job)
    {
        if (job is < 8 or > 18) return $"job {job} is not a crafter or gatherer";
        var classJob = Svc.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(job);
        var abbr = classJob?.Abbreviation.ExtractText() ?? job.ToString();

        var manager = InventoryManager.Instance();
        if (manager is null) return "the inventory is not loaded yet";
        foreach (var type in Sources)
        {
            var container = manager->GetInventoryContainer(type);
            if (container is null || !container->IsLoaded) return "the inventory is not loaded yet";
        }

        var tools = Prototypes(job, abbr);
        if (tools.Count == 0) return $"no Cosmic Prototype tool for {abbr} in the game data";
        var level = Player.GetLevel((Job)job);
        var top = tools.Values.Max();

        var owned = 0;
        foreach (var type in Sources)
        {
            var container = manager->GetInventoryContainer(type);
            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot is null || slot->ItemId == 0) continue;
                if (tools.TryGetValue(slot->ItemId % 1_000_000, out var equip) && equip > owned) owned = equip;
            }
        }

        if (owned == 0)
            return level >= top ? null : $"no Cosmic Prototype tool for {abbr} found in the worn gear, armoury chest or bags (Lv {level})";
        var next = tools.Values.Where(l => l > owned).DefaultIfEmpty(0).Min();
        if (next == 0) return null; // the last Prototype: what follows is Lv {top}, which the class already reached to wear this one
        return level < next ? $"{abbr} Lv {level} cannot wear its next Cosmic tool stage (Lv {next}; it holds the Lv {owned} one)" : null;
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

    private static unsafe void CloseMenus()
    {
        foreach (var name in new[] { "SelectYesno", "SelectIconString", "SelectString" })
            if (GenericHelpers.TryGetAddonByName<AtkUnitBase>(name, out var addon) && addon->IsVisible)
                addon->Close(true);
    }

    private static Dictionary<uint, int> Prototypes(uint job, string abbr)
    {
        if (prototypes.TryGetValue(job, out var known)) return known;
        var allows = typeof(ClassJobCategory).GetProperty(abbr, BindingFlags.Public | BindingFlags.Instance);
        var tools = new Dictionary<uint, int>();
        foreach (var item in Svc.Data.GetExcelSheet<Item>(Dalamud.Game.ClientLanguage.English))
        {
            var name = item.Name.ExtractText();
            if (!name.StartsWith("Cosmic ", StringComparison.Ordinal) || !name.Contains("Prototype", StringComparison.Ordinal)) continue;
            if (item.EquipSlotCategory.ValueNullable?.MainHand != 1) continue;
            if (item.ClassJobCategory.ValueNullable is not { } category || allows?.GetValue(category) is not true) continue;
            tools[item.RowId] = item.LevelEquip;
        }
        return prototypes[job] = tools;
    }
}
