using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ICE.Utilities.Cosmic_Helper;
using Lumina.Excel.Sheets;
using System.Collections.Generic;

namespace ICE.Fork;

/// <summary>
///     Fork item 6: every class's relic tool is handed in once its research is complete, not only the class ICE is working on,
///     and the class handed in is the one judged, swapped to and picked. Upstream judged ICE's job (the agenda's current class)
///     but registered the class worn as the one to hand in, then swapped to ICE's job: 2026-10-08, Armorer reached its agenda
///     level, the agenda moved on to Goldsmith, ICE swapped to Goldsmith, the NPC refused ("anything less than a full dataset"),
///     ICE swapped back to Armorer and started over every 12 seconds.
///     <para>
///         This is the only way ICE goes to the research NPC (besides the debug button). A class is picked when it is unlocked,
///         its research is complete (every kind of data at or above what the next stage needs, and the next stage needs some:
///         a stage whose needs all read 0 is not data the game asks for), the relic guard (item 1) lets its tool go, it was not
///         refused at this same reading, and it can be worn for the hand-in (a gear set, the relic battle job, or worn already).
///         ICE's own job goes first. One class per hub visit; ICE comes back for the next.
///     </para>
///     <para>
///         A hand-in that reached the NPC's end but left the research stage where it was was refused: logged with the reading
///         (dalamud.log) and kept in the config, so that class is not tried again, even after a reload, until its reading
///         changes (research data, stage, level or the game's unlocked stage for it).
///     </para>
/// </summary>
internal static class ForkRelicQueue
{
    private const string Tag = "[Fork: Relic queue]";

    private static uint picked;
    private static (uint Job, int Stage, bool Ended)? pending;

    /// <summary>The hub decision: true when some class's tool is to be handed in now (that class is kept for the hand-in).</summary>
    public static bool Pick(uint selectedJob)
    {
        var research = CosmicHelper.Cosmic_ClassInfo();
        JudgePending(research);
        picked = 0;
        var jobs = new List<uint> { selectedJob };
        if (C.Fork_RelicAllClasses)
            for (uint job = 8; job <= 18; job++)
                if (job != selectedJob) jobs.Add(job);

        foreach (var job in jobs)
        {
            var why = WhyNot(job, research);
            if (why is null)
            {
                picked = job;
                Log($"relic hand-in due for {Abbr(job)} ({Reading(job, research[job])}).");
                return true;
            }
            if (why.Length > 0 && EzThrottler.Throttle($"Fork relic queue: held {job}", 60_000))
                Log($"relic hand-in for {Abbr(job)} held: {why}.");
        }
        return false;
    }

    /// <summary>The hand-in starts: the class it is for (the picked one; the class worn when nothing was picked, e.g. the debug button).</summary>
    public static uint StartHandIn()
    {
        var job = picked != 0 ? picked : (uint)Player.Job;
        picked = 0;
        var research = CosmicHelper.Cosmic_ClassInfo();
        pending = research.TryGetValue(job, out var info) ? (job, info.Stage_Current, false) : null;
        return job;
    }

    /// <summary>The hand-in went through the NPC to its end (refused or not: the next hub decision tells).</summary>
    public static void HandInEnded()
    {
        if (pending is { } p) pending = p with { Ended = true };
    }

    /// <summary>A hand-in aborted by the guard: not a refusal.</summary>
    public static void Forget() => pending = null;

    /// <summary>Why the class's tool is not handed in now; "" when there is simply nothing to hand in; null when it goes.</summary>
    private static string? WhyNot(uint job, Dictionary<uint, CosmicHelper.ClassInfo> research)
    {
        if (Player.GetLevel((Job)job) <= 0) return "";
        if (!research.TryGetValue(job, out var info) || !ResearchDone(info)) return "";
        if (C.Fork_RelicRefusals.TryGetValue(job, out var no) && no == Reading(job, info))
            return "the NPC refused it at this same reading; it waits until the research, level or unlocked stage changes";
        if (ForkRelicGuard.Blocker(job) is { } blocked) return blocked;
        if ((uint)Player.Job != job && !BattleJobUsable() && !ForkJobSwapGuard.CanSwapTo(job))
            return $"no gear set for {Abbr(job)} and no usable relic battle job to hand it in from";
        return null;
    }

    /// <summary>Research complete: a next stage, some data needed for it, and every kind at or above what it needs.</summary>
    private static bool ResearchDone(CosmicHelper.ClassInfo info) =>
        info.Stage_Current < info.Stage_Next && info.CurrentExp.Count > 0
        && info.CurrentExp.Values.Any(e => e.Needed > 0) && info.CurrentExp.Values.All(e => e.Current >= e.Needed);

    /// <summary>True when the hand-in is made from the relic battle job (upstream's first swap branch).</summary>
    public static bool BattleJobUsable() =>
        Char_Info.Relic_SwapJob && Char_Info.Relic_BattleJob != 0 && ForkJobSwapGuard.CanSwapTo(Char_Info.Relic_BattleJob);

    private static void JudgePending(Dictionary<uint, CosmicHelper.ClassInfo> research)
    {
        if (pending is not { } p) return;
        pending = null;
        if (!p.Ended || !research.TryGetValue(p.Job, out var info)) return;
        if (info.Stage_Current > p.Stage)
        {
            if (C.Fork_RelicRefusals.Remove(p.Job)) C.Save();
            Log($"{Abbr(p.Job)} relic handed in: research stage {p.Stage} -> {info.Stage_Current}.");
            return;
        }
        var reading = Reading(p.Job, info);
        C.Fork_RelicRefusals[p.Job] = reading;
        C.Save();
        IceLogging.Warning($"The NPC took no relic for {Abbr(p.Job)}: research stage still {p.Stage}. ICE read it as complete: {reading}. " +
                           "Not tried again until that reading changes.", Tag);
    }

    /// <summary>What the hand-in decision rests on for the class; a refusal is remembered against it.</summary>
    private static string Reading(uint job, CosmicHelper.ClassInfo info) =>
        $"stage {info.Stage_Current}->{info.Stage_Next}, unlocked stage {UnlockedStage(job)}, Lv {Player.GetLevel((Job)job)}; " +
        string.Join(", ", info.CurrentExp.Select(e => $"{e.Value.Name} {e.Value.Current}/{e.Value.Needed} (max {e.Value.Max})"));

    /// <summary>The game's unlocked research stage for the class (ICE's corrected research layout); -1 when not loaded.</summary>
    private static unsafe int UnlockedStage(uint job)
    {
        var wks = WKSManager.Instance();
        if (wks == null || job is < 8 or > 18) return -1;
        var module = (WKSResearchModuleCorrect*)wks->ResearchModule;
        return module == null || !module->IsLoaded ? -1 : module->UnlockedStages[job - 8];
    }

    private static string Abbr(uint job) =>
        Svc.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(job)?.Abbreviation.ExtractText() ?? job.ToString();

    // dalamud.log as well: upstream's Info lines only reach ICE's own log window.
    private static void Log(string message)
    {
        IceLogging.Info(message, Tag);
        PluginLog.Information($"{Tag} {message}");
    }
}
