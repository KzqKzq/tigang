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
    private readonly DebugLogService _debugLogService;

    private DispatcherQueueTimer? _sessionTimer;
    private CancellationTokenSource? _persistDebounceCts;
    private DateTimeOffset _sessionStartedAt;
    private DateTimeOffset _phaseStartedAt;
    private SessionPhase _sessionPhase = SessionPhase.Ready;
    private AiPlanSuggestion? _lastSuggestion;
    private bool _isInitializing;
    private bool _isCompletingTraining;
    private bool _isUpdatingPlanDerivedState;

    public MainViewModel(StorageService storageService, NotificationService notificationService, AiPlanService aiPlanService, DispatcherQueue dispatcherQueue, DebugLogService debugLogService)
    {
        _storageService = storageService;
        _notificationService = notificationService;
        _aiPlanService = aiPlanService;
        _dispatcherQueue = dispatcherQueue;
        _debugLogService = debugLogService;

        Plans.CollectionChanged += PlansOnCollectionChanged;

        AddPlanCommand = new RelayCommand(AddPlan);
        DeletePlanCommand = new RelayCommand(DeletePlan, () => SelectedPlan is not null);
        AddReminderTimeCommand = new RelayCommand(AddReminderTime, () => SelectedPlan is not null);
        RemoveReminderTimeCommand = new RelayCommand<ReminderTime>(RemoveReminderTime);
        SaveNowCommand = new AsyncRelayCommand(SaveNowAsync);
        TestToastCommand = new RelayCommand(TestToast, () => SelectedPlan is not null);
        StartTrainingCommand = new RelayCommand(StartTraining, () => SelectedPlan is not null && !IsTrainingActive);
        StopTrainingCommand = new RelayCommand(StopTraining, () => IsTrainingActive);
        SetAnimationModeCommand = new RelayCommand<string>(SetAnimationMode);
        GenerateAiPlanCommand = new AsyncRelayCommand(GenerateAiPlanAsync, () => !IsBusy);
        ApplyAiPlanCommand = new RelayCommand(ApplyAiPlan, () => _lastSuggestion is not null);
    }

    private void LogDebug(string category, string message)
    {
        _ = _debugLogService.LogAsync(category, message);
    }

    private void LogDebugSync(string category, string message)
    {
        _debugLogService.Log(category, message);
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
    private int recentSevenDaySessions;

    [ObservableProperty]
    private string trendSummary = "最近 7 天还没有形成趋势。";

    [ObservableProperty]
    private string motivationSummary = "先完成今天的一组，节律会慢慢稳定下来。";

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

    [ObservableProperty]
    private TrainingAnimationMode selectedAnimationMode = TrainingAnimationMode.PulseOrb;

    [ObservableProperty]
    private double liftOffsetY;

    [ObservableProperty]
    private double liftScaleY = 1.0;

    [ObservableProperty]
    private double liftGlowScale = 1.0;

    [ObservableProperty]
    private double liftBodyScaleX = 1.0;

    [ObservableProperty]
    private double liftTopScale = 1.0;

    [ObservableProperty]
    private double liftBaseScale = 1.0;

    [ObservableProperty]
    private double liftTrailOpacity = 0.18;

    [ObservableProperty]
    private double liftCoreOpacity = 1.0;

    public IRelayCommand AddPlanCommand { get; }

    public IRelayCommand DeletePlanCommand { get; }

    public IRelayCommand AddReminderTimeCommand { get; }

    public IRelayCommand<ReminderTime> RemoveReminderTimeCommand { get; }

    public IAsyncRelayCommand SaveNowCommand { get; }

    public IRelayCommand TestToastCommand { get; }

    public IRelayCommand StartTrainingCommand { get; }

    public IRelayCommand StopTrainingCommand { get; }

    public IRelayCommand<string> SetAnimationModeCommand { get; }

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
            LogDebug("Initialize", $"Begin initialize. Log={_debugLogService.LogFilePath}");
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
            LogDebug("Initialize", $"Loaded plans={Plans.Count}, history={Settings.SessionHistory.Count}");
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

    public bool IsPulseOrbMode => SelectedAnimationMode == TrainingAnimationMode.PulseOrb;

    public bool IsVerticalLiftMode => SelectedAnimationMode == TrainingAnimationMode.VerticalLift;

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

        StopSessionTimer();
        _isCompletingTraining = false;
        IsTrainingActive = false;

        SelectedPlan.EnsureValid();
        IsTrainingActive = true;
        _sessionPhase = SessionPhase.Contract;
        _sessionStartedAt = DateTimeOffset.Now;
        _phaseStartedAt = _sessionStartedAt;
        CycleText = $"1 / {SelectedPlan.Cycles}";
        StatusText = $"训练日志：{_debugLogService.LogFilePath}";
        LogDebug("TrainingStart", $"plan={SelectedPlan.Name}, contract={SelectedPlan.ContractSeconds}, relax={SelectedPlan.RelaxSeconds}, cycles={SelectedPlan.Cycles}, total={SelectedPlan.TotalDurationSeconds}");

        _sessionTimer = _dispatcherQueue.CreateTimer();
        _sessionTimer.Interval = TimeSpan.FromMilliseconds(16);
        _sessionTimer.Tick += SessionTimerOnTick;
        _sessionTimer.Start();
        StopTrainingCommand.NotifyCanExecuteChanged();
        StartTrainingCommand.NotifyCanExecuteChanged();
        UpdateTrainingVisuals(DateTimeOffset.Now);
    }

    private void StopTraining()
    {
        StopSessionTimer();
        _isCompletingTraining = false;
        LogDebug("TrainingStop", $"manual stop. phase={_sessionPhase}, remaining={RemainingText}, cycle={CycleText}");

        IsTrainingActive = false;
        _sessionPhase = SessionPhase.Ready;
        CurrentPhaseText = "待开始";
        CurrentPhaseHint = "选择计划后可再次开始。";
        RemainingText = "00:00";
        StopTrainingCommand.NotifyCanExecuteChanged();
        StartTrainingCommand.NotifyCanExecuteChanged();
        CycleText = SelectedPlan is null ? "0 / 0" : $"0 / {SelectedPlan.Cycles}";
        SessionProgress = 0;
        PulseScale = 1;
        HaloScale = 1.22;
        HaloOpacity = Settings.ReduceMotion ? 0.12 : 0.18;
        LiftOffsetY = 0;
        LiftScaleY = 1;
        LiftGlowScale = 1;
        LiftBodyScaleX = 1;
        LiftTopScale = 1;
        LiftBaseScale = 1;
        LiftTrailOpacity = 0.18;
        LiftCoreOpacity = 1;
        StopTrainingCommand.NotifyCanExecuteChanged();
        StartTrainingCommand.NotifyCanExecuteChanged();
    }

    private void StopSessionTimer()
    {
        if (_sessionTimer is null)
        {
            return;
        }

        _sessionTimer.Stop();
        _sessionTimer.Tick -= SessionTimerOnTick;
        _sessionTimer = null;
    }

    private async void SessionTimerOnTick(DispatcherQueueTimer sender, object args)
    {
        try
        {
            if (_isCompletingTraining)
            {
                return;
            }

            var now = DateTimeOffset.Now;
            UpdateTrainingVisuals(now);

            if (SelectedPlan is null)
            {
                return;
            }

            var totalElapsedSeconds = (now - _sessionStartedAt).TotalSeconds;
            if (totalElapsedSeconds >= SelectedPlan.TotalDurationSeconds)
            {
                _isCompletingTraining = true;
                StopSessionTimer();
                LogDebugSync("TrainingTick", $"hit total duration. elapsed={totalElapsedSeconds:F3}, total={SelectedPlan.TotalDurationSeconds}, cycle={CycleText}, remaining={RemainingText}");
                await CompleteTrainingAsync();
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
                LogDebug("PhaseSwitch", $"to relax. elapsed={totalElapsedSeconds:F3}, cycle={CycleText}");
                return;
            }

            var completedCycles = (int)Math.Floor((now - _sessionStartedAt).TotalSeconds / (SelectedPlan.ContractSeconds + SelectedPlan.RelaxSeconds));
            if (completedCycles >= SelectedPlan.Cycles)
            {
                _isCompletingTraining = true;
                StopSessionTimer();
                LogDebugSync("TrainingTick", $"hit completed cycles. completed={completedCycles}, cycles={SelectedPlan.Cycles}, remaining={RemainingText}");
                await CompleteTrainingAsync();
                return;
            }

            _sessionPhase = SessionPhase.Contract;
            _phaseStartedAt = now;
            CycleText = $"{completedCycles + 1} / {SelectedPlan.Cycles}";
            LogDebug("PhaseSwitch", $"to contract. completed={completedCycles}, nextCycle={CycleText}, elapsed={totalElapsedSeconds:F3}");
        }
        catch (Exception ex)
        {
            StopSessionTimer();
            _isCompletingTraining = false;
            IsTrainingActive = false;
            StopTrainingCommand.NotifyCanExecuteChanged();
            StartTrainingCommand.NotifyCanExecuteChanged();
            StatusText = $"训练流程出错：{ex.Message}";
            LogDebug("TrainingError", ex.ToString());
        }
    }

    private async Task CompleteTrainingAsync()
    {
        _isCompletingTraining = true;
        LogDebugSync("TrainingComplete", "step 1 enter");
        StopSessionTimer();
        LogDebugSync("TrainingComplete", "step 2 timer stopped");
        IsTrainingActive = false;
        LogDebugSync("TrainingComplete", $"step 3 state before complete. phase={_sessionPhase}, remaining={RemainingText}, cycle={CycleText}");
        _sessionPhase = SessionPhase.Complete;
        LogDebugSync("TrainingComplete", "step 4 phase set complete");
        CurrentPhaseText = "已完成";
        CurrentPhaseHint = "这组训练结束了，休息片刻再开始下一组。";
        SessionProgress = 1;
        RemainingText = "00:00";
        CycleText = SelectedPlan is null ? "0 / 0" : $"{SelectedPlan.Cycles} / {SelectedPlan.Cycles}";
        StopTrainingCommand.NotifyCanExecuteChanged();
        StartTrainingCommand.NotifyCanExecuteChanged();
        LogDebugSync("TrainingComplete", "step 5 ui state updated");

        Settings.CompletedSessions += 1;
        Settings.LastCompletedSessionAt = DateTimeOffset.Now;
        LogDebugSync("TrainingComplete", "step 6 counters updated");
        Settings.SessionHistory.Insert(0, new SessionRecord
        {
            PlanName = SelectedPlan?.Name ?? "训练",
            DurationSeconds = SelectedPlan?.TotalDurationSeconds ?? 0,
            Cycles = SelectedPlan?.Cycles ?? 0,
            CompletedAt = DateTimeOffset.Now
        });
        LogDebugSync("TrainingComplete", "step 7 history inserted");
        Settings.SessionHistory = Settings.SessionHistory
            .OrderByDescending(static item => item.CompletedAt)
            .Take(120)
            .ToList();
        RefreshHistory();
        SyncMeta();
        LogDebugSync("TrainingComplete", "step 8 history/meta refreshed");

        if (SelectedPlan is not null)
        {
            try
            {
                LogDebugSync("TrainingComplete", "step 9 sending completion toast");
                _notificationService.ShowInstantReminder(SelectedPlan, "本次训练已完成。");
                LogDebugSync("TrainingComplete", "step 10 completion toast sent");
            }
            catch (Exception ex)
            {
                StatusText = $"训练已完成，但通知发送失败：{ex.Message}";
                LogDebugSync("TrainingToastError", ex.ToString());
            }
        }

        try
        {
            LogDebugSync("TrainingComplete", "step 11 persisting completion");
            await PersistNowAsync();
            LogDebugSync("TrainingComplete", "step 12 persist completed");
        }
        catch (Exception ex)
        {
            StatusText = $"训练已完成，但记录保存失败：{ex.Message}";
            LogDebugSync("TrainingPersistError", ex.ToString());
        }
        finally
        {
            _isCompletingTraining = false;
            LogDebugSync("TrainingComplete", "step 13 complete flow finished");
        }
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
        // Cosine easing removes the visible stepping from linear scale changes.
        var smoothProgress = 0.5 - (0.5 * Math.Cos(Math.PI * phaseProgress));
        var invert = _sessionPhase == SessionPhase.Relax;
        var eased = invert ? 1 - smoothProgress : smoothProgress;

        CurrentPhaseText = _sessionPhase == SessionPhase.Contract ? "收紧" : "放松";
        CurrentPhaseHint = _sessionPhase == SessionPhase.Contract ? "轻轻上提，保持呼吸" : "完全松开，恢复自然";
        var remainingSeconds = Math.Max(0, totalDuration - elapsedTotal);
        RemainingText = TimeSpan.FromSeconds(Math.Ceiling(remainingSeconds)).ToString(@"mm\:ss");

        if (Settings.ReduceMotion)
        {
            PulseScale = 1;
            HaloScale = 1.15;
            HaloOpacity = 0.12;
            LiftOffsetY = -8;
            LiftScaleY = 1.04;
            LiftGlowScale = 1.04;
            LiftBodyScaleX = 1.02;
            LiftTopScale = 1.04;
            LiftBaseScale = 1;
            LiftTrailOpacity = 0.18;
            LiftCoreOpacity = 0.96;
        }
        else
        {
            PulseScale = 0.86 + (0.38 * eased);
            HaloScale = 1.02 + (0.46 * eased);
            HaloOpacity = 0.06 + (0.34 * eased);
            LiftOffsetY = 34 - (68 * eased);
            LiftScaleY = 0.86 + (0.34 * eased);
            LiftGlowScale = 0.96 + (0.28 * eased);
            LiftBodyScaleX = 0.9 + (0.2 * eased);
            LiftTopScale = 0.88 + (0.28 * eased);
            LiftBaseScale = 1.08 - (0.18 * eased);
            LiftTrailOpacity = 0.08 + (0.24 * eased);
            LiftCoreOpacity = 0.72 + (0.28 * eased);
        }

        var currentCycle = Math.Min(SelectedPlan.Cycles, Math.Max(1, ((int)(elapsedTotal / cycleDuration)) + 1));
        CycleText = $"{currentCycle} / {SelectedPlan.Cycles}";
    }

    private async Task PersistNowAsync()
    {
        LogDebugSync("Persist", $"begin. plans={Plans.Count}, history={Settings.SessionHistory.Count}, completed={Settings.CompletedSessions}");
        Settings.Plans = Plans.ToList();
        LogDebugSync("Persist", "before save");
        await _storageService.SaveAsync(Settings);
        LogDebugSync("Persist", "after save");
        RescheduleNotifications();
        LogDebugSync("Persist", "after reschedule");
        RefreshUpcoming();
        LogDebugSync("Persist", "after refresh upcoming");
        LogDebugSync("Persist", "success");
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
            LogDebugSync("Reschedule", $"begin. plans={Plans.Count}");
            _notificationService.Reschedule(Plans);
            LogDebugSync("Reschedule", "success");
        }
        catch (Exception ex)
        {
            StatusText = $"通知同步失败：{ex.Message}";
            LogDebugSync("RescheduleError", ex.ToString());
        }
    }

    private void SyncMeta()
    {
        ActivePlanCount = Plans.Count(static plan => plan.IsEnabled);
        var actualCompletedSessions = Math.Max(Settings.CompletedSessions, Settings.SessionHistory.Count);
        var latestCompletedAt = Settings.SessionHistory.Count == 0
            ? Settings.LastCompletedSessionAt
            : Settings.SessionHistory.Max(static item => item.CompletedAt);
        var today = DateTime.Now.Date;
        var currentWindowStart = today.AddDays(-6);
        var previousWindowStart = currentWindowStart.AddDays(-7);
        var previousWindowEnd = currentWindowStart.AddDays(-1);
        var recentRecords = Settings.SessionHistory
            .Where(item => item.CompletedAt.LocalDateTime.Date >= currentWindowStart)
            .ToList();
        var previousRecords = Settings.SessionHistory
            .Where(item =>
            {
                var date = item.CompletedAt.LocalDateTime.Date;
                return date >= previousWindowStart && date <= previousWindowEnd;
            })
            .ToList();

        TotalSessionsCompleted = actualCompletedSessions;
        TotalMinutesCompleted = Settings.SessionHistory.Sum(static item => (int)Math.Ceiling(item.DurationSeconds / 60d));
        CurrentStreakDays = CalculateStreak(Settings.SessionHistory);
        RecentSevenDaySessions = recentRecords.Count;
        TrendSummary = BuildTrendSummary(recentRecords, previousRecords);
        MotivationSummary = BuildMotivationSummary(CurrentStreakDays, RecentSevenDaySessions);
        OnPropertyChanged(nameof(TotalMinutesSummary));
        LastCompletedText = latestCompletedAt is null
            ? "还没有完成过训练。"
            : $"最近一次完成：{latestCompletedAt.Value:MM-dd HH:mm}";
    }

    private static string BuildTrendSummary(IReadOnlyCollection<SessionRecord> recentRecords, IReadOnlyCollection<SessionRecord> previousRecords)
    {
        var recentMinutes = recentRecords.Sum(static item => (int)Math.Ceiling(item.DurationSeconds / 60d));
        var previousMinutes = previousRecords.Sum(static item => (int)Math.Ceiling(item.DurationSeconds / 60d));

        if (recentRecords.Count == 0)
        {
            return "最近 7 天还没有训练记录。";
        }

        if (previousRecords.Count == 0)
        {
            return $"最近 7 天完成 {recentRecords.Count} 组，正在建立新节律。";
        }

        if (recentRecords.Count > previousRecords.Count)
        {
            return $"比前 7 天多完成 {recentRecords.Count - previousRecords.Count} 组，趋势在上升。";
        }

        if (recentRecords.Count < previousRecords.Count)
        {
            return $"比前 7 天少了 {previousRecords.Count - recentRecords.Count} 组，可以把提醒窗口再固定一点。";
        }

        if (recentMinutes > previousMinutes)
        {
            return $"组数持平，但总时长多了 {recentMinutes - previousMinutes} 分钟。";
        }

        return "最近两周节律保持平稳，适合继续稳步推进。";
    }

    private static string BuildMotivationSummary(int streakDays, int recentSevenDaySessions)
    {
        if (streakDays >= 7)
        {
            return $"已经连续坚持 {streakDays} 天，当前最重要的是别断。";
        }

        if (recentSevenDaySessions >= 5)
        {
            return "这一周频率已经不错，再补一组就更稳了。";
        }

        if (recentSevenDaySessions >= 1)
        {
            return "你已经开始积累了，固定住时间点会更容易坚持。";
        }

        return "先把第一周跑起来，比一开始追求强度更重要。";
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
            var propertyName = e.PropertyName;
            if (!_isUpdatingPlanDerivedState &&
                propertyName is not nameof(TrainingPlan.DurationSummary) &&
                propertyName is not nameof(TrainingPlan.ReminderSummary) &&
                propertyName is not nameof(TrainingPlan.ActiveDaySummary))
            {
                _isUpdatingPlanDerivedState = true;
                try
                {
                    plan.NotifyDerivedStateChanged();
                }
                finally
                {
                    _isUpdatingPlanDerivedState = false;
                }
            }
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

    partial void OnSelectedAnimationModeChanged(TrainingAnimationMode value)
    {
        OnPropertyChanged(nameof(IsPulseOrbMode));
        OnPropertyChanged(nameof(IsVerticalLiftMode));
    }

    private void SetAnimationMode(string? mode)
    {
        if (!Enum.TryParse<TrainingAnimationMode>(mode, out var parsed))
        {
            return;
        }

        SelectedAnimationMode = parsed;
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
