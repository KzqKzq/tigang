using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TigangReminder_App.Models;

public partial class TrainingPlan : ObservableObject
{
    [ObservableProperty]
    private string id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string name = "晨间唤醒";

    [ObservableProperty]
    private string description = "适合开始建立习惯的轻量练习。";

    [ObservableProperty]
    private bool isEnabled = true;

    [ObservableProperty]
    private int contractSeconds = 3;

    [ObservableProperty]
    private int relaxSeconds = 3;

    [ObservableProperty]
    private int cycles = 12;

    [ObservableProperty]
    private bool monday = true;

    [ObservableProperty]
    private bool tuesday = true;

    [ObservableProperty]
    private bool wednesday = true;

    [ObservableProperty]
    private bool thursday = true;

    [ObservableProperty]
    private bool friday = true;

    [ObservableProperty]
    private bool saturday = true;

    [ObservableProperty]
    private bool sunday = true;

    public ObservableCollection<ReminderTime> ReminderTimes { get; set; } = [new ReminderTime()];

    public int TotalDurationSeconds => Math.Max(1, Cycles) * (Math.Max(1, ContractSeconds) + Math.Max(1, RelaxSeconds));

    public string DurationSummary => $"{Cycles} 轮 · 收紧 {ContractSeconds}s / 放松 {RelaxSeconds}s · 共 {Math.Ceiling(TotalDurationSeconds / 60d):0} 分钟";

    public string ReminderSummary => ReminderTimes.Count == 0
        ? "未设置提醒"
        : string.Join(" · ", ReminderTimes.Select(static t => t.DisplayText));

    public string ActiveDaySummary
    {
        get
        {
            var labels = new List<string>();
            if (Monday) labels.Add("一");
            if (Tuesday) labels.Add("二");
            if (Wednesday) labels.Add("三");
            if (Thursday) labels.Add("四");
            if (Friday) labels.Add("五");
            if (Saturday) labels.Add("六");
            if (Sunday) labels.Add("日");
            return labels.Count == 7 ? "每天" : $"周{string.Join("/", labels)}";
        }
    }

    public bool IsActiveOn(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => Monday,
        DayOfWeek.Tuesday => Tuesday,
        DayOfWeek.Wednesday => Wednesday,
        DayOfWeek.Thursday => Thursday,
        DayOfWeek.Friday => Friday,
        DayOfWeek.Saturday => Saturday,
        DayOfWeek.Sunday => Sunday,
        _ => false
    };

    public void EnsureValid()
    {
        ContractSeconds = Math.Clamp(ContractSeconds, 2, 20);
        RelaxSeconds = Math.Clamp(RelaxSeconds, 2, 20);
        Cycles = Math.Clamp(Cycles, 4, 60);

        if (ReminderTimes.Count == 0)
        {
            ReminderTimes.Add(new ReminderTime());
        }
    }

    public void NotifyDerivedStateChanged()
    {
        OnPropertyChanged(nameof(ReminderSummary));
        OnPropertyChanged(nameof(DurationSummary));
        OnPropertyChanged(nameof(ActiveDaySummary));
    }
}
