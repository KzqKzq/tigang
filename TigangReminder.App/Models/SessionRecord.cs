namespace TigangReminder_App.Models;

public class SessionRecord
{
    public string PlanName { get; set; } = string.Empty;

    public int DurationSeconds { get; set; }

    public int Cycles { get; set; }

    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.Now;

    public string Summary => $"{PlanName} · {Math.Ceiling(DurationSeconds / 60d):0} 分钟 · {Cycles} 轮";

    public string CompletedText => CompletedAt.ToString("MM-dd HH:mm");
}
