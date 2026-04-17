namespace TigangReminder_App.Models;

public class AppSettings
{
    public List<TrainingPlan> Plans { get; set; } = [];

    public List<SessionRecord> SessionHistory { get; set; } = [];

    public AiSettings AiSettings { get; set; } = new();

    public int CompletedSessions { get; set; }

    public DateTimeOffset? LastCompletedSessionAt { get; set; }

    public bool ReduceMotion { get; set; }

    public bool CloseToTray { get; set; } = true;

    public bool MinimizeToTray { get; set; } = true;
}
