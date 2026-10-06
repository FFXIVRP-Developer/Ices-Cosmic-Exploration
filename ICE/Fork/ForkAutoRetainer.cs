using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation;
using ECommons.GameHelpers;
using ECommons.Reflection;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ICE.Utilities.Cosmic_Helper;
using System.Reflection;

namespace ICE.Fork;

/// <summary>
///     Fork item 3: ICE and AutoRetainer side by side.
///     - While ICE runs, AutoRetainer is held back: suppressed (its IPC: no RetainerSense, no multi mode) and its "Artisan
///       integration" off. That integration stops Artisan whenever ventures are ready and a bell is in reach (the moon
///       base has one), suppressed or not; mid-mission the next recipe failed and ICE looped. AutoRetainer has no IPC for
///       that setting, so it is switched on its live config and given back once ICE is idle (the value is kept in ICE's
///       config, so a crash or reload still gives it back).
///     - Between missions (ICE's hub check, no mission running), with a retainer ready and a summoning bell within
///       <see cref="BellMeters" />: walk there, open it, let AutoRetainer do the ready retainers, close it, carry on.
///       No travel beyond that. After a stop (done or not) the next one waits <see cref="CooldownMs" />.
/// </summary>
internal static class ForkAutoRetainer
{
    private const string Tag = "[Fork: Retainers]";
    private const string TaskPrefix = "Fork retainers: ";
    private const float BellMeters = 150f;
    private const long CooldownMs = 10 * 60_000;
    private const long StepTimeoutMs = 60_000;
    private const long ProcessTimeoutMs = 15 * 60_000;
    private const uint CityBellBaseId = 2000401;
    private const uint HousingBellBaseId = 196630;
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static bool suppressedByIce;
    private static long nextTickMs;
    private static long lastBreakMs = -CooldownMs;
    private static long stepStartMs;
    private static bool sawBusy;
    private static bool gaveUp;
    private static string? bellName;

    public static bool Available => Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == "AutoRetainer" && p.IsLoaded);

    /// <summary>ICE's own retainer stop is under way (its steps are in ICE's queue).</summary>
    public static bool InBreak =>
        (P.TaskManager.CurrentTask?.Name?.StartsWith(TaskPrefix) ?? false) || P.TaskManager.Tasks.Any(t => t.Name?.StartsWith(TaskPrefix) ?? false);

    public static void Init() => Svc.Framework.Update += Tick;

    public static void Dispose()
    {
        Svc.Framework.Update -= Tick;
        Release();
    }

    // ---- Holding AutoRetainer back while ICE runs ----------------------------------------------------------------

    private static void Tick(object _)
    {
        var now = Environment.TickCount64;
        if (now < nextTickMs) return;
        nextTickMs = now + 1000;
        if (!Available) return;
        try
        {
            if (SchedulerMain.State != IceState.Idle && !InBreak) Hold();
            else Release();
        }
        catch (Exception ex)
        {
            if (EzThrottler.Throttle("Fork retainers: tick error", 60_000))
                IceLogging.Error($"AutoRetainer hold: {ex.Message}", Tag);
        }
    }

    private static void Hold()
    {
        if (!suppressedByIce && !Svc.PluginInterface.GetIpcSubscriber<bool>("AutoRetainer.GetSuppressed").InvokeFunc())
        {
            Svc.PluginInterface.GetIpcSubscriber<bool, object>("AutoRetainer.SetSuppressed").InvokeAction(true);
            suppressedByIce = true;
            IceLogging.Info("ICE runs: AutoRetainer suppressed.", Tag);
        }
        if (ArtisanIntegration() is true)
        {
            C.Fork_ArtisanIntegrationHeld = true;
            C.Save();
            SetArtisanIntegration(false);
            IceLogging.Info("ICE runs: AutoRetainer's Artisan integration held off (it stops Artisan mid-mission).", Tag);
        }
    }

    private static void Release()
    {
        if (suppressedByIce)
        {
            suppressedByIce = false;
            GenericHelpers.Safe(() => Svc.PluginInterface.GetIpcSubscriber<bool, object>("AutoRetainer.SetSuppressed").InvokeAction(false));
            IceLogging.Info("AutoRetainer no longer suppressed.", Tag);
        }
        if (C.Fork_ArtisanIntegrationHeld is { } held && SetArtisanIntegration(held))
        {
            C.Fork_ArtisanIntegrationHeld = null;
            C.Save();
            IceLogging.Info($"AutoRetainer's Artisan integration back to its own setting ({(held ? "on" : "off")}).", Tag);
        }
    }

    /// <summary>AutoRetainer's live config (its plugin's "config" field); null when it cannot be reached.</summary>
    private static object? ArConfig()
    {
        if (!DalamudReflector.TryGetDalamudPlugin("AutoRetainer", out var plugin, false, true)) return null;
        return plugin.GetType().GetField("config", Any)?.GetValue(plugin);
    }

    private static bool? ArtisanIntegration() => ArConfig()?.GetType().GetField("ArtisanIntegration", Any)?.GetValue(ArConfig()) as bool?;

    private static bool SetArtisanIntegration(bool on)
    {
        if (ArConfig() is not { } config || config.GetType().GetField("ArtisanIntegration", Any) is not { } field) return false;
        field.SetValue(config, on);
        return true;
    }

    // ---- The retainer stop between missions ----------------------------------------------------------------------

    private static bool RetainersReady() =>
        Svc.PluginInterface.GetIpcSubscriber<bool>("AutoRetainer.PluginState.AreAnyRetainersAvailableForCurrentChara").InvokeFunc();

    private static bool ArBusy() => Svc.PluginInterface.GetIpcSubscriber<bool>("AutoRetainer.PluginState.IsBusy").InvokeFunc();

    /// <summary>Hub check hook: queues the retainer stop when it is due; true when it was queued (the check ends there).</summary>
    public static bool TryEnqueueBreak()
    {
        try
        {
            if (!C.Fork_RetainersBetweenMissions || !Available || CosmicHelper.CurrentLunarMission != 0) return false;
            if (Environment.TickCount64 - lastBreakMs < CooldownMs || !RetainersReady()) return false;
            if (FindBell() is not { } bell)
            {
                if (EzThrottler.Throttle("Fork retainers: no bell", 5 * 60_000))
                    IceLogging.Info($"A retainer is ready, but no summoning bell within {BellMeters:F0} m; not travelling for it.", Tag);
                return false;
            }

            lastBreakMs = Environment.TickCount64;
            gaveUp = false;
            IceLogging.Info($"A retainer is ready: to the summoning bell {Player.DistanceTo(bell.Position):F0} m away.", Tag);
            P.TaskManager.EnqueueMulti
            (
                new(() => Begin(), TaskPrefix + "start"),
                new(() => WalkToBell(), TaskPrefix + "walking to the bell"),
                new(() => Begin(), TaskPrefix + "start"),
                new(() => OpenBell(), TaskPrefix + "opening the bell"),
                new(() => Begin(), TaskPrefix + "start"),
                new(() => Process(), TaskPrefix + "AutoRetainer at work"),
                new(() => Begin(), TaskPrefix + "start"),
                new(() => CloseBell(), TaskPrefix + "closing the bell"),
                new(() => SchedulerMain.State = IceState.Start, "Swapping back to start")
            );
            return true;
        }
        catch (Exception ex)
        {
            IceLogging.Error($"Retainer stop not queued: {ex.Message}", Tag);
            return false;
        }
    }

    private static bool? Begin()
    {
        stepStartMs = Environment.TickCount64;
        sawBusy = false;
        return true;
    }

    private static bool TimedOut(long limit, string what)
    {
        if (Environment.TickCount64 - stepStartMs < limit) return false;
        IceLogging.Warning($"{what}: gave up after {limit / 1000} s; ICE carries on.", Tag);
        gaveUp = true;
        return true;
    }

    private static bool? WalkToBell()
    {
        if (gaveUp) return true;
        if (FindBell() is not { } bell) { gaveUp = true; return true; }
        if (Task_NavmeshMove.Task_NavTo(bell.Position, distance: 3f, npcLoc: bell.Position) == true) return true;
        return TimedOut(3 * StepTimeoutMs, "Walking to the bell");
    }

    private static unsafe bool? OpenBell()
    {
        if (gaveUp) return true;
        if (Svc.Condition[ConditionFlag.OccupiedSummoningBell] || ArBusy()) return true;
        if (TimedOut(StepTimeoutMs / 4, "Opening the bell")) return true;
        if (FindBell() is { } bell && EzThrottler.Throttle("Fork retainers: interact", 1500))
        {
            Utils.TargetgameObject(bell);
            Utils.InteractWithObject(bell);
        }
        return false;
    }

    /// <summary>AutoRetainer usually starts on its own at an opened bell; if not, "/autoretainer e". Done when none is ready.</summary>
    private static bool? Process()
    {
        if (gaveUp) return true;
        var busy = ArBusy();
        sawBusy |= busy;
        var waited = Environment.TickCount64 - stepStartMs;
        if (!busy && !RetainersReady() && waited > 3000)
        {
            IceLogging.Info("Retainers done.", Tag);
            return true;
        }
        if (!busy && waited > 5000 && EzThrottler.Throttle("Fork retainers: enable", 10_000))
            Chat.ExecuteCommand("/autoretainer e");
        // AutoRetainer never started: no point waiting the whole processing time.
        return TimedOut(sawBusy ? ProcessTimeoutMs : StepTimeoutMs / 2, "AutoRetainer at the bell");
    }

    private static unsafe bool? CloseBell()
    {
        if (ArBusy())
        {
            if (TimedOut(StepTimeoutMs, "Closing the bell"))
                GenericHelpers.Safe(() => Svc.PluginInterface.GetIpcSubscriber<object>("AutoRetainer.PluginState.AbortAllTasks").InvokeAction());
            return false;
        }
        if (EzThrottler.Throttle("Fork retainers: disable", 5000)) Chat.ExecuteCommand("/autoretainer d");
        if (!Svc.Condition[ConditionFlag.OccupiedSummoningBell]) return true;
        if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerList", out var list) && list->IsVisible && EzThrottler.Throttle("Fork retainers: close", 1000))
            list->Close(true);
        return TimedOut(StepTimeoutMs, "Closing the bell") ? true : false;
    }

    /// <summary>The nearest summoning bell within <see cref="BellMeters" /> (city or housing bell id, or the game's bell name).</summary>
    private static IGameObject? FindBell()
    {
        bellName ??= Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.EObjName>().GetRowOrDefault(CityBellBaseId)?.Singular.ExtractText() ?? "";
        return Svc.Objects
            .Where(o => o.IsTargetable && Player.DistanceTo(o.Position) <= BellMeters)
            .Where(o => o.BaseId is CityBellBaseId or HousingBellBaseId
                     || (bellName.Length > 0 && o.Name.TextValue.Equals(bellName, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(o => Player.DistanceTo(o.Position))
            .FirstOrDefault();
    }
}
