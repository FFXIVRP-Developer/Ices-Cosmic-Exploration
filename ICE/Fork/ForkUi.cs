using ECommons.GameHelpers;

namespace ICE.Fork;

/// <summary>The fork's settings, drawn under upstream's "Turnin if relic is complete" (Mission_Setup).</summary>
internal static class ForkUi
{
    public static void Draw()
    {
        var job = (uint)Player.Job;
        if (job is >= 8 and <= 18)
        {
            var why = ForkRelicGuard.Blocker(job);
            ImGui.TextDisabled(why is null ? "Fork: relic hand-in allowed for this class." : $"Fork: relic hand-in held: {why}.");
        }

        var retainers = C.Fork_RetainersBetweenMissions;
        if (ImGui.Checkbox("Retainers between missions (AutoRetainer, nearby bell)##Fork", ref retainers))
        {
            C.Fork_RetainersBetweenMissions = retainers;
            C.Save();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("?");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Fork: between missions, if AutoRetainer has a retainer ready and a summoning bell is within 150 m,\n" +
                             "ICE walks there and lets AutoRetainer collect them. While ICE runs, AutoRetainer is suppressed and its\n" +
                             "Artisan integration is held off (it would stop Artisan mid-mission); both come back when ICE stops.");
    }
}
