using ECommons.GameHelpers;
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
///         A class is picked when it is unlocked, its research is complete (ICE's reading, as upstream), the relic guard (item 1)
///         lets its tool go (the class can wear what the hand-in gives) and it can be worn for the hand-in (a gear set, the relic
///         battle job, or it is the class worn). ICE's own job goes first. One class per hub visit; ICE comes back for the next.
///     </para>
///     <para>
///         A hand-in that reached the NPC's end but left the research stage where it was was refused: logged with ICE's reading
///         (dalamud.log), and that class is not tried again while its research reads the same (or for 30 minutes), so a misread
///         can never loop.
///     </para>
/// </summary>
internal static class ForkRelicQueue
{
    private const string Tag = "[Fork: Relic queue]";
    private static readonly TimeSpan RefusalHold = TimeSpan.FromMinutes(30);

    private static uint picked;
    private static (uint Job, int Stage, string Reading, bool Ended)? pending;
    private static readonly Dictionary<uint, (string Reading, DateTime At)> refused = [];

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
                Log($"relic hand-in due for {Abbr(job)} ({Reading(research[job])}).");
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
        pending = research.TryGetValue(job, out var info) ? (job, info.Stage_Current, Reading(info), false) : null;
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
        var reading = Reading(info);
        if (refused.TryGetValue(job, out var no) && no.Reading == reading && DateTime.Now - no.At < RefusalHold)
            return "the NPC refused it last time and its research reads the same since";
        if (ForkRelicGuard.Blocker(job) is { } blocked) return blocked;
        if ((uint)Player.Job != job && !BattleJobUsable() && !ForkJobSwapGuard.CanSwapTo(job))
            return $"no gear set for {Abbr(job)} and no usable relic battle job to hand it in from";
        return null;
    }

    /// <summary>Upstream's "research complete": a next stage, and every kind of data at or above what it needs.</summary>
    private static bool ResearchDone(CosmicHelper.ClassInfo info) =>
        info.Stage_Current < info.Stage_Next && info.CurrentExp.Count > 0 && info.CurrentExp.Values.All(e => e.Current >= e.Needed);

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
            refused.Remove(p.Job);
            Log($"{Abbr(p.Job)} relic handed in: research stage {p.Stage} -> {info.Stage_Current}.");
            return;
        }
        refused[p.Job] = (Reading(info), DateTime.Now);
        IceLogging.Warning($"The NPC took no relic for {Abbr(p.Job)}: research stage still {p.Stage}. ICE read it as complete: " +
                           $"{p.Reading}. Not tried again while it reads the same (or for {RefusalHold.TotalMinutes:F0} min).", Tag);
    }

    private static string Reading(CosmicHelper.ClassInfo info) =>
        $"stage {info.Stage_Current}->{info.Stage_Next}; " +
        string.Join(", ", info.CurrentExp.Select(e => $"{e.Value.Name} {e.Value.Current}/{e.Value.Needed} (max {e.Value.Max})"));

    private static string Abbr(uint job) =>
        Svc.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(job)?.Abbreviation.ExtractText() ?? job.ToString();

    // dalamud.log as well: upstream's Info lines only reach ICE's own log window.
    private static void Log(string message)
    {
        IceLogging.Info(message, Tag);
        PluginLog.Information($"{Tag} {message}");
    }
}
