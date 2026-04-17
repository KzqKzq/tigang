using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using TigangReminder_App.Models;
using TigangReminder_App.Services;

namespace TigangReminder_App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly StorageService _storageService;
    private readonly NotificationService _notificationService;
    private readonly AiPlanService _aiPlanService;
    private readonly DispatcherQueue _dispatcherQueue;

    private DispatcherQueueTimer? _sessionTimer;
    private CancellationTokenSource? _persistDebounceCts;
    private DateTimeOffset _sessionStartedAt;
    private DateTimeOffset _phaseStartedAt;
    private SessionPhase _sessionPhase = SessionPhase.Ready;
    private AiPlanSuggestion? _lastSuggestion;
    private bool _isInitializing;

    public MainViewModel(StorageService storageService, NotificationService notificationService, AiPlanService aiPlanService, DispatcherQueue dispatcherQueue)
    {
        _storageService = storageService;
        _notificationService = notificationService;
        _aiPlanService = aiPlanService;
        _dispatcherQueue = dispatcherQueue;

        Plans.CollectionChanged += PlansOnCollectionChanged;

        AddPlanCommand = new RelayCommand(AddPlan);
        DeletePlanCommand = new RelayCommand(DeletePlan, () => SelectedPlan is not null);
        AddReminderTimeCommand = new RelayCommand(AddReminderTime, () => SelectedPlan is not null);
        RemoveReminderTimeCommand = new RelayCommand<ReminderTime>(RemoveReminderTime);
        SaveNowCommand = new AsyncRelayCommand(SaveNowAsync);
        TestToastCommand = new RelayCommand(TestToast, () => SelectedPlan is not null);
        StartTrainingCommand = new RelayCommand(StartTraining, () => SelectedPlan is not null);
        StopTrainingCommand = new RelayCommand(StopTraining, () => IsTrainingActive);
        GenerateAiPlanCommand = new AsyncRelayCommand(GenerateAiPlanAsync, () => !IsBusy);
        ApplyAiPlanCommand = new RelayCommand(ApplyAiPlan, () => _lastSuggestion is not null);
    }

    public ObservableCollection<TrainingPlan> Plans { get; } = [];

    public ObservableCollection<UpcomingReminder> UpcomingReminders { get; } = [];

    public ObservableCollection<SessionRecord> SessionHistory { get; } = [];

    public AppSettings Settings { get; private set; } = new();

    [ObservableProperty]
    private TrainingPlan? selectedPlan;

    [ObservableProperty]
    private SectionKind currentSection = SectionKind.Overview;

    [ObservableProperty]
    private string nextReminderSummary = "读取中...";

    [ObservableProperty]
    private int activePlanCount;

    [ObservableProperty]
    private int totalSessionsCompleted;

    [ObservableProperty]
    private int totalMinutesCompleted;

    [ObservableProperty]
    private int currentStreakDays;

    public string TotalMinutesSummary => $"累计 {TotalMinutesCompleted} 分钟";

    [ObservableProperty]
    private string lastCompletedText = "还没有完成过训练。";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusText = "准备就绪";

    [ObservableProperty]
    private string newReminderTimeText = "09:00";

    [ObservableProperty]
    private string aiGoalPrompt = "久坐办公，想建立稳定、不过度用力的提肛习惯";

    [ObservableProperty]
    private string aiExperienceLevel = "入门";

    [ObservableProperty]
    private int aiAvailableMinutes = 6;

    [ObservableProperty]
    private string aiOutput = "AI 会基于目标、经验和时长生成适合日常坚持的训练计划。";

    [ObservableProperty]
    private string aiStatus = "未生成";

    [ObservableProperty]
    private bool isTrainingActive;

    [ObservableProperty]
    private string currentPhaseText = "待开始";

    [ObservableProperty]
    private string currentPhaseHint = "先选择一个计划，再开始训练。";

    [ObservableProperty]
    private string remainingText = "00:00";

    [ObservableProperty]
    private string cycleText = "0 / 0";

    [ObservableProperty]
    private double sessionProgress;

    [ObservableProperty]
    private double pulseScale = 1.0;

    [ObservableProperty]
    private double haloScale = 1.18;

    [ObservableProperty]
    private double haloOpacity = 0.18;

    public IRelayCommand AddPlanCommand { get; }

    public IRelayCommand DeletePlanCommand { get; }

    public IRelayCommand AddReminderTimeCommand { get; }

    public IRelayCommand<ReminderTime> RemoveReminderTimeCommand { get; }

    public IAsyncRelayCommand SaveNowCommand { get; }

    public IRelayCommand TestToastCommand { get; }

    public IRelayCommand StartTrainingCommand { get; }

    public IRelayCommand StopTrainingCommand { get; }

    public IAsyncRelayCommand GenerateAiPlanCommand { get; }

    public IRelayCommand ApplyAiPlanCommand { get; }

    public async Task InitializeAsync()
    {
        if (_isInitializing)
        {
            return;
        }

        _isInitializing = true;
        try
        {
            Settings = await _storageService.LoadAsync();
            OnPropertyChanged(nameof(Settings));

            Plans.Clear();
            foreach (var plan in Settings.Plans)
            {
                Plans.Add(plan);
            }

            SelectedPlan = Plans.FirstOrDefault();
            RefreshHistory();
            SyncMeta();
            RefreshUpcoming();
            RescheduleNotifications();
            StatusText = "提醒计划已加载";
        }
        finally
        {
            _isInitializing = false;
        }
    }

    public void SetSection(SectionKind section)
    {
        CurrentSection = section;
    }

    public void StartTrainingFromShell()
    {
        StartTraining();
    }

    private void AddPlan()
    {
        var plan = new TrainingPlan
        {
            Name = $"新计划 {Plans.Count + 1}",
            Description = "可自定义提醒时间、收紧/放松节奏和训练轮数。",
            ReminderTimes = [new ReminderTime { TimeText = "09:00" }]
        };

        Plans.Add(plan);
        SelectedPlan = plan;
        QueuePersist();
    }

    private void DeletePlan()
    {
        if (SelectedPlan is null)
        {
            return;
        }

        Plans.Remove(SelectedPlan);

        if (Plans.Count == 0)
        {
            AddPlan();
        }
        else
        {
            SelectedPlan = Plans.FirstOrDefault();
        }

        QueuePersist();
    }

    private void AddReminderTime()
    {
        if (SelectedPlan is null)
        {
            return;
        }

        var value = string.IsNullOrWhiteSpace(NewReminderTimeText) ? "09:00" : NewReminderTimeText.Trim();
        SelectedPlan.ReminderTimes.Add(new ReminderTime { TimeText = value });
        NewReminderTimeText = "09:00";
        SelectedPlan.NotifyDerivedStateChanged();
        QueuePersist();
    }

    private void RemoveReminderTime(ReminderTime? reminderTime)
    {
        if (SelectedPlan is null || reminderTime is null)
        {
            return;
        }

        SelectedPlan.ReminderTimes.Remove(reminderTime);
        SelectedPlan.EnsureValid();
        SelectedPlan.NotifyDerivedStateChanged();
        QueuePersist();
    }

    private async Task SaveNowAsync()
    {
        await PersistNowAsync();
        StatusText = "计划已保存并同步通知";
    }

    private void TestToast()
    {
        if (SelectedPlan is null)
        {
            return;
        }

        _notificationService.ShowInstantReminder(SelectedPlan, "这是测试通知，确认 Win11 弹窗是否正常。");
        StatusText = "已发送测试通知";
    }

    private async Task GenerateAiPlanAsync()
    {
        try
        {
            IsBusy = true;
            AiStatus = "生成中...";
            var suggestion = await _aiPlanService.GenerateAsync(Settings.AiSettings, AiGoalPrompt, AiExperienceLevel, AiAvailableMinutes, CancellationToken.None);
            _lastSuggestion = suggestion;
            ApplyAiPlanCommand.NotifyCanExecuteChanged();
            AiStatus = suggestion.IsFallback ? "本地建议" : "AI 已生成";
            AiOutput =
                $"{suggestion.Name}\n\n" +
                $"{suggestion.Summary}\n\n" +
                $"节奏：收紧 {suggestion.ContractSeconds}s / 放松 {suggestion.RelaxSeconds}s\n" +
                $"轮数：{suggestion.Cycles}\n" +
                $"建议提醒：{string.Join(" · ", suggestion.SuggestedTimes)}\n\n" +
                $"{suggestion.Note}";
        }
        catch (Exception ex)
        {
            AiStatus = "生成失败";
            AiOutput = $"AI 请求失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyAiPlan()
    {
        if (_lastSuggestion is null)
        {
            return;
        }

        var target = SelectedPlan;
        if (target is null)
        {
            AddPlan();
            target = SelectedPlan;
        }

        if (target is null)
        {
            return;
        }

        target.Name = _lastSuggestion.Name;
        target.Description = _lastSuggestion.Summary;
        target.ContractSeconds = _lastSuggestion.ContractSeconds;
        target.RelaxSeconds = _lastSuggestion.RelaxSeconds;
        target.Cycles = _lastSuggestion.Cycles;
        target.ReminderTimes.Clear();
        foreach (var time in _lastSuggestion.SuggestedTimes)
        {
            target.ReminderTimes.Add(new ReminderTime { TimeText = time });
        }

        target.EnsureValid();
        target.NotifyDerivedStateChanged();
        StatusText = "AI 计划已应用到当前计划";
        QueuePersist();
    }

    private void StartTraining()
    {
        if (SelectedPlan is null)
        {
            return;
        }

        StopTraining();

        SelectedPlan.EnsureValid();
        IsTrainingActive = true;
        _sessionPhase = SessionPhase.Contract;
        _sessionStartedAt = DateTimeOffset.Now;
        _phaseStartedAt = _sessionStartedAt;
        CycleText = $"1 / {SelectedPlan.Cycles}";

        _sessionTimer = _dispatcherQueue.CreateTimer();
        _sessionTimer.Interval = TimeSpan.FromMilliseconds(90);
        _sessionTimer.Tick += SessionTimerOnTick;
        _sessionTimer.Start();
        UpdateTrainingVisuals(DateTimeOffset.Now);
    }

    private void StopTraining()
    {
        if (_sessionTimer is not null)
        {
            _sessionTimer.Stop();
            _sessionTimer.Tick -= SessionTimerOnTick;
            _sessionTimer = null;
        }

        IsTrainingActive = false;
        _sessionPhase = SessionPhase.Ready;
        CurrentPhaseText = "待开始";
        CurrentPhaseHint = "选择计划后可再次开始。";
        RemainingText = "00:00";
        CycleText = SelectedPlan is null ? "0 / 0" : $"0 / {SelectedPlan.Cycles}";
        SessionProgress = 0;
        PulseScale = 1;
        HaloScale = 1.18;
        HaloOpacity = Settings.ReduceMotion ? 0.12 : 0.18;
        StopTrainingCommand.NotifyCanExecuteChanged();
        StartTrainingCommand.NotifyCanExecuteChanged();
    }

    private async void SessionTimerOnTick(DispatcherQueueTimer sender, object args)
    {
        var now = DateTimeOffset.Now;
        UpdateTrainingVisuals(now);

        if (SelectedPlan is null)
        {
            return;
        }

        var totalPhaseSeconds = _sessionPhase == SessionPhase.Contract
            ? SelectedPlan.ContractSeconds
            : SelectedPlan.RelaxSeconds;

        if ((now - _phaseStartedAt).TotalSeconds < totalPhaseSeconds)
        {
            return;
        }

        if (_sessionPhase == SessionPhase.Contract)
        {
            _sessionPhase = SessionPhase.Relax;
            _phaseStartedAt = now;
            return;
        }

        var elapsedCycles = (int)Math.Floor((now - _sessionStartedAt).TotalSeconds / (SelectedPlan.ContractSeconds + SelectedPlan.RelaxSeconds)) + 1;
        if (elapsedCycles >= SelectedPlan.Cycles)
        {
            await CompleteTrainingAsync();
            return;
        }

        _sessionPhase = SessionPhase.Contract;
        _phaseStartedAt = now;
        CycleText = $"{elapsedCycles + 1} / {SelectedPlan.Cycles}";
    }

    private async Task CompleteTrainingAsync()
    {
        StopTraining();
        _sessionPhase = SessionPhase.Complete;
        CurrentPhaseText = "已完成";
        CurrentPhaseHint = "这组训练结束了，休息片刻再开始下一组。";
        SessionProgress = 1;
        RemainingText = "00:00";

        Settings.CompletedSessions += 1;
        Settings.LastCompletedSessionAt = DateTimeOffset.Now;
        Settings.SessionHistory.Insert(0, new SessionRecord
        {
            PlanName = SelectedPlan?.Name ?? "训练",
            DurationSeconds = SelectedPlan?.TotalDurationSeconds ?? 0,
            Cycles = SelectedPlan?.Cycles ?? 0,
            CompletedAt = DateTimeOffset.Now
        });
        Settings.SessionHistory = Settings.SessionHistory
            .OrderByDescending(static item => item.CompletedAt)
            .Take(120)
            .ToList();
        RefreshHistory();
        SyncMeta();

        if (SelectedPlan is not null)
        {
            _notificationService.ShowInstantReminder(SelectedPlan, "本次训练已完成。");
        }

        await PersistNowAsync();
    }

    private void UpdateTrainingVisuals(DateTimeOffset now)
    {
        if (SelectedPlan is null)
        {
            return;
        }

        var cycleDuration = SelectedPlan.ContractSeconds + SelectedPlan.RelaxSeconds;
        var totalDuration = SelectedPlan.TotalDurationSeconds;
        var elapsedTotal = Math.Clamp((now - _sessionStartedAt).TotalSeconds, 0, totalDuration);
        SessionProgress = elapsedTotal / totalDuration;

        var phaseDuration = _sessionPhase == SessionPhase.Contract ? SelectedPlan.ContractSeconds : SelectedPlan.RelaxSeconds;
        var phaseElapsed = Math.Clamp((now - _phaseStartedAt).TotalSeconds, 0, phaseDuration);
        var phaseProgress = phaseDuration == 0 ? 0 : phaseElapsed / phaseDuration;
        var invert = _sessionPhase == SessionPhase.Relax;
        var eased = invert ? 1 - phaseProgress : phaseProgress;

        CurrentPhaseText = _sessionPhase == SessionPhase.Contract ? "收紧" : "放松";
        CurrentPhaseHint = _sessionPhase == SessionPhase.Contract ? "轻轻上提，保持呼吸" : "完全松开，恢复自然";
        RemainingText = TimeSpan.FromSeconds(Math.Max(0, totalDuration - elapsedTotal)).ToString(@"mm\:ss");

        if (Settings.ReduceMotion)
        {
            PulseScale = 1;
            HaloScale = 1.15;
            HaloOpacity = 0.12;
        }
        else
        {
            PulseScale = 0.94 + (0.24 * eased);
            HaloScale = 1.08 + (0.28 * eased);
            HaloOpacity = 0.08 + (0.28 * eased);
        }

        var currentCycle = Math.Min(SelectedPlan.Cycles, Math.Max(1, ((int)(elapsedTotal / cycleDuration)) + 1));
        CycleText = $"{currentCycle} / {SelectedPlan.Cycles}";
    }

    private async Task PersistNowAsync()
    {
        Settings.Plans = Plans.ToList();
        await _storageService.SaveAsync(Settings);
        RescheduleNotifications();
        RefreshUpcoming();
    }

    private void QueuePersist()
    {
        if (_isInitializing)
        {
            return;
        }

        SyncMeta();
        RefreshUpcoming();

        _persistDebounceCts?.Cancel();
        _persistDebounceCts = new CancellationTokenSource();
        var token = _persistDebounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(700, token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                await _dispatcherQueue.EnqueueAsync(async () =>
                {
                    await PersistNowAsync();
                    StatusText = "已自动同步计划";
                });
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void RefreshUpcoming()
    {
        UpcomingReminders.Clear();
        var upcoming = _notificationService.BuildUpcomingReminders(Plans, DateTimeOffset.Now, 7).Take(6).ToList();

        foreach (var (plan, when) in upcoming)
        {
            UpcomingReminders.Add(new UpcomingReminder
            {
                PlanName = plan.Name,
                WhenText = when.ToString("MM-dd HH:mm"),
                RelativeText = ToRelative(when - DateTimeOffset.Now)
            });
        }

        NextReminderSummary = upcoming.Count == 0
            ? "没有待触发的提醒"
            : $"{upcoming[0].Plan.Name} · {upcoming[0].When:MM-dd HH:mm}";
    }

    private void RescheduleNotifications()
    {
        try
        {
            _notificationService.Reschedule(Plans);
        }
        catch (Exception ex)
        {
            StatusText = $"通知同步失败：{ex.Message}";
        }
    }

    private void SyncMeta()
    {
        ActivePlanCount = Plans.Count(static plan => plan.IsEnabled);
        TotalSessionsCompleted = Settings.CompletedSessions;
        TotalMinutesCompleted = Settings.SessionHistory.Sum(static item => (int)Math.Ceiling(item.DurationSeconds / 60d));
        CurrentStreakDays = CalculateStreak(Settings.SessionHistory);
        OnPropertyChanged(nameof(TotalMinutesSummary));
        LastCompletedText = Settings.LastCompletedSessionAt is null
            ? "还没有完成过训练。"
            : $"最近一次完成：{Settings.LastCompletedSessionAt.Value:MM-dd HH:mm}";
    }

    private void RefreshHistory()
    {
        SessionHistory.Clear();
        foreach (var item in Settings.SessionHistory.OrderByDescending(static record => record.CompletedAt).Take(12))
        {
            SessionHistory.Add(item);
        }
    }

    private void AttachPlan(TrainingPlan plan)
    {
        plan.EnsureValid();
        plan.NotifyDerivedStateChanged();
        plan.PropertyChanged += PlanOnPropertyChanged;
        plan.ReminderTimes.CollectionChanged += ReminderTimesOnCollectionChanged;

        foreach (var reminder in plan.ReminderTimes)
        {
            reminder.PropertyChanged += ReminderOnPropertyChanged;
        }
    }

    private void DetachPlan(TrainingPlan plan)
    {
        plan.PropertyChanged -= PlanOnPropertyChanged;
        plan.ReminderTimes.CollectionChanged -= ReminderTimesOnCollectionChanged;

        foreach (var reminder in plan.ReminderTimes)
        {
            reminder.PropertyChanged -= ReminderOnPropertyChanged;
        }
    }

    private void PlansOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (TrainingPlan plan in e.OldItems)
            {
                DetachPlan(plan);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (TrainingPlan plan in e.NewItems)
            {
                AttachPlan(plan);
            }
        }

        QueuePersist();
        DeletePlanCommand.NotifyCanExecuteChanged();
    }

    private void ReminderTimesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ReminderTime reminder in e.OldItems)
            {
                reminder.PropertyChanged -= ReminderOnPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ReminderTime reminder in e.NewItems)
            {
                reminder.PropertyChanged += ReminderOnPropertyChanged;
            }
        }

        SelectedPlan?.NotifyDerivedStateChanged();
        QueuePersist();
    }

    private void ReminderOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        SelectedPlan?.NotifyDerivedStateChanged();
        QueuePersist();
    }

    private void PlanOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is TrainingPlan plan)
        {
            plan.NotifyDerivedStateChanged();
        }

        DeletePlanCommand.NotifyCanExecuteChanged();
        TestToastCommand.NotifyCanExecuteChanged();
        StartTrainingCommand.NotifyCanExecuteChanged();
        QueuePersist();
    }

    partial void OnSelectedPlanChanged(TrainingPlan? value)
    {
        DeletePlanCommand.NotifyCanExecuteChanged();
        AddReminderTimeCommand.NotifyCanExecuteChanged();
        TestToastCommand.NotifyCanExecuteChanged();
        StartTrainingCommand.NotifyCanExecuteChanged();
        StopTrainingCommand.NotifyCanExecuteChanged();
        if (!IsTrainingActive)
        {
            CycleText = value is null ? "0 / 0" : $"0 / {value.Cycles}";
        }
    }

    private static string ToRelative(TimeSpan delta)
    {
        if (delta.TotalHours >= 24)
        {
            return $"{Math.Floor(delta.TotalDays)} 天后";
        }

        if (delta.TotalHours >= 1)
        {
            return $"{Math.Floor(delta.TotalHours)} 小时后";
        }

        return $"{Math.Max(1, Math.Floor(delta.TotalMinutes))} 分钟后";
    }

    private static int CalculateStreak(IEnumerable<SessionRecord> records)
    {
        var days = records
            .Select(static record => record.CompletedAt.LocalDateTime.Date)
            .Distinct()
            .OrderByDescending(static day => day)
            .ToList();

        if (days.Count == 0)
        {
            return 0;
        }

        var today = DateTime.Now.Date;
        var cursor = days[0] == today || days[0] == today.AddDays(-1) ? days[0] : DateTime.MinValue;
        if (cursor == DateTime.MinValue)
        {
            return 0;
        }

        var streak = 0;
        foreach (var day in days)
        {
            if (day == cursor)
            {
                streak++;
                cursor = cursor.AddDays(-1);
                continue;
            }

            if (day < cursor)
            {
                break;
            }
        }

        return streak;
    }
}

internal static class DispatcherQueueExtensions
{
    public static Task EnqueueAsync(this DispatcherQueue dispatcherQueue, Func<Task> callback)
    {
        var tcs = new TaskCompletionSource<object?>();
        dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await callback();
                tcs.SetResult(null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }
}
