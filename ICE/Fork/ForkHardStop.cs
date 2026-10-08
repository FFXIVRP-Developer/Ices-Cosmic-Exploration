using ECommons.GameHelpers;
using ICE.Scheduler.Tasks;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation;
using ICE.Utilities.Cosmic_Helper;

namespace ICE.Fork;

/// <summary>
///     Fork item 7: the hard stop (user, 2026-10-08: "hard stop is when the character is walking to a new node or the character ends a
///     craft, it ends it all, abandons the mission by getting out of the crafter table and stops"). Asked (IPC ICE.HardStop), it
///     waits for a safe moment (never mid-craft, never at a node), then: Artisan's endurance off, the moon's recipe window closed
///     (out of the crafting table, as the turn-in does), ICE switched off, the mission abandoned, the retainer break ended. ICE
///     counts as busy until it is done. The soft stop stays upstream's "stop after current".
/// </summary>
public static class ForkHardStop
{
    public static bool Requested { get; private set; }

    public static void Request()
    {
        if (Requested) return;
        Requested = true;
        IceLogging.Info("Hard stop asked: at the next safe moment the mission is abandoned and ICE stops.", "[Fork: Hard stop]");
    }

    /// <summary>A start clears a hard stop never carried out.</summary>
    public static void Clear() => Requested = false;

    /// <summary>A safe moment: no craft action under way, the synthesis window down, not at a gathering node.</summary>
    public static bool SafeMoment(bool executingCraft, bool synthesisOpen, bool atNode) => !executingCraft && !synthesisOpen && !atNode;

    public static void Init() => Svc.Framework.Update += Tick;

    public static void Dispose() => Svc.Framework.Update -= Tick;

    private static unsafe void Tick(Dalamud.Plugin.Services.IFramework _)
    {
        if (!Requested || !EzThrottler.Throttle("ForkHardStop", 250)) return;
        var executing = Svc.Condition[ConditionFlag.ExecutingCraftingAction];
        var synthesis = GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>("Synthesis", out var s) && s->IsVisible;
        var atNode = Svc.Condition[ConditionFlag.Gathering] && (uint)Player.Job != 18; // a fisher stops fishing below
        if (!SafeMoment(executing, synthesis, atNode)) return;

        if ((uint)Player.Job == 18 && Svc.Condition[ConditionFlag.Gathering])
        {
            Task_DualClass.StopFishing();
            return;
        }
        try { if (P.Artisan.SetEnduranceStatus is { } endurance) endurance(false); } catch { /* Artisan not loaded */ }
        if (GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>("WKSRecipeNotebook", out var notebook) && notebook->IsVisible)
            GenericHandlers.FireCallback("WKSRecipeNotebook", true, -1); // out of the crafting table, as the turn-in does
        if (SchedulerMain.State != IceState.Idle)
        {
            P.TaskManager.Abort();
            SchedulerMain.DisablePlugin();
        }
        if (CosmicHelper.CurrentLunarMission != 0)
        {
            var wks = FFXIVClientStructs.FFXIV.Client.Game.WKS.WKSManager.Instance();
            if (wks is not null && wks->MissionModule is not null) wks->MissionModule->AbandonMission();
            IceLogging.Info("Hard stop: the mission is abandoned.", "[Fork: Hard stop]");
            return; // checked again next tick: done once the mission reads 0
        }
        Requested = false;
        IceLogging.Info("Hard stop done: no mission, ICE off.", "[Fork: Hard stop]");
    }
}
