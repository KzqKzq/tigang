using System.Text.Json;
using Windows.Storage;
using TigangReminder_App.Models;

namespace TigangReminder_App.Services;

public class StorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "settings.json");

    public string SettingsPath => _settingsPath;

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_settingsPath))
        {
            return CreateDefaultSettings();
        }

        await using var stream = File.OpenRead(_settingsPath);
        var persisted = await JsonSerializer.DeserializeAsync<PersistedAppSettings>(stream, JsonOptions);
        var settings = persisted is null ? CreateDefaultSettings() : FromPersisted(persisted);
        Normalize(settings);
        return settings;
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Normalize(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);

        var persisted = ToPersisted(settings);
        var json = JsonSerializer.Serialize(persisted, JsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json);
    }

    private static PersistedAppSettings ToPersisted(AppSettings settings) => new()
    {
        Plans = settings.Plans.Select(static plan => new PersistedTrainingPlan
        {
            Id = plan.Id,
            Name = plan.Name,
            Description = plan.Description,
            IsEnabled = plan.IsEnabled,
            ContractSeconds = plan.ContractSeconds,
            RelaxSeconds = plan.RelaxSeconds,
            Cycles = plan.Cycles,
            Monday = plan.Monday,
            Tuesday = plan.Tuesday,
            Wednesday = plan.Wednesday,
            Thursday = plan.Thursday,
            Friday = plan.Friday,
            Saturday = plan.Saturday,
            Sunday = plan.Sunday,
            ReminderTimes = plan.ReminderTimes.Select(static time => new PersistedReminderTime
            {
                TimeText = time.TimeText
            }).ToList()
        }).ToList(),
        SessionHistory = settings.SessionHistory.Select(static item => new PersistedSessionRecord
        {
            PlanName = item.PlanName,
            DurationSeconds = item.DurationSeconds,
            Cycles = item.Cycles,
            CompletedAt = item.CompletedAt
        }).ToList(),
        AiSettings = new PersistedAiSettings
        {
            Endpoint = settings.AiSettings.Endpoint,
            Model = settings.AiSettings.Model,
            ApiKey = settings.AiSettings.ApiKey,
            SystemPrompt = settings.AiSettings.SystemPrompt
        },
        CompletedSessions = settings.CompletedSessions,
        LastCompletedSessionAt = settings.LastCompletedSessionAt,
        ReduceMotion = settings.ReduceMotion,
        CloseToTray = settings.CloseToTray,
        MinimizeToTray = settings.MinimizeToTray
    };

    private static AppSettings FromPersisted(PersistedAppSettings persisted) => new()
    {
        Plans = persisted.Plans.Select(static plan => new TrainingPlan
        {
            Id = plan.Id,
            Name = plan.Name,
            Description = plan.Description,
            IsEnabled = plan.IsEnabled,
            ContractSeconds = plan.ContractSeconds,
            RelaxSeconds = plan.RelaxSeconds,
            Cycles = plan.Cycles,
            Monday = plan.Monday,
            Tuesday = plan.Tuesday,
            Wednesday = plan.Wednesday,
            Thursday = plan.Thursday,
            Friday = plan.Friday,
            Saturday = plan.Saturday,
            Sunday = plan.Sunday,
            ReminderTimes = [.. plan.ReminderTimes.Select(static time => new ReminderTime
            {
                TimeText = time.TimeText
            })]
        }).ToList(),
        SessionHistory = persisted.SessionHistory.Select(static item => new SessionRecord
        {
            PlanName = item.PlanName,
            DurationSeconds = item.DurationSeconds,
            Cycles = item.Cycles,
            CompletedAt = item.CompletedAt
        }).ToList(),
        AiSettings = new AiSettings
        {
            Endpoint = persisted.AiSettings.Endpoint,
            Model = persisted.AiSettings.Model,
            ApiKey = persisted.AiSettings.ApiKey,
            SystemPrompt = persisted.AiSettings.SystemPrompt
        },
        CompletedSessions = persisted.CompletedSessions,
        LastCompletedSessionAt = persisted.LastCompletedSessionAt,
        ReduceMotion = persisted.ReduceMotion,
        CloseToTray = persisted.CloseToTray,
        MinimizeToTray = persisted.MinimizeToTray
    };

    private static void Normalize(AppSettings settings)
    {
        settings.Plans ??= [];
        settings.SessionHistory ??= [];
        settings.AiSettings ??= new AiSettings();

        if (settings.Plans.Count == 0)
        {
            settings.Plans.Add(CreateDefaultPlan());
        }

        foreach (var plan in settings.Plans)
        {
            plan.ReminderTimes ??= [new ReminderTime()];
            plan.EnsureValid();
        }

        settings.SessionHistory = settings.SessionHistory
            .OrderByDescending(static record => record.CompletedAt)
            .Take(120)
            .ToList();

        settings.CompletedSessions = Math.Max(settings.CompletedSessions, settings.SessionHistory.Count);
        settings.LastCompletedSessionAt = settings.SessionHistory.Count == 0
            ? settings.LastCompletedSessionAt
            : settings.SessionHistory.Max(static record => record.CompletedAt);
    }

    private static AppSettings CreateDefaultSettings() => new()
    {
        Plans = [CreateDefaultPlan()],
        AiSettings = new AiSettings()
    };

    private static TrainingPlan CreateDefaultPlan() => new()
    {
        Name = "晨间唤醒",
        Description = "起床后做一组轻量节律，先稳住习惯。",
        ContractSeconds = 3,
        RelaxSeconds = 3,
        Cycles = 10,
        ReminderTimes = [new ReminderTime { TimeText = "08:30" }, new ReminderTime { TimeText = "21:30" }]
    };

    private sealed class PersistedAppSettings
    {
        public List<PersistedTrainingPlan> Plans { get; set; } = [];
        public List<PersistedSessionRecord> SessionHistory { get; set; } = [];
        public PersistedAiSettings AiSettings { get; set; } = new();
        public int CompletedSessions { get; set; }
        public DateTimeOffset? LastCompletedSessionAt { get; set; }
        public bool ReduceMotion { get; set; }
        public bool CloseToTray { get; set; } = true;
        public bool MinimizeToTray { get; set; } = true;
    }

    private sealed class PersistedTrainingPlan
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public int ContractSeconds { get; set; }
        public int RelaxSeconds { get; set; }
        public int Cycles { get; set; }
        public bool Monday { get; set; }
        public bool Tuesday { get; set; }
        public bool Wednesday { get; set; }
        public bool Thursday { get; set; }
        public bool Friday { get; set; }
        public bool Saturday { get; set; }
        public bool Sunday { get; set; }
        public List<PersistedReminderTime> ReminderTimes { get; set; } = [];
    }

    private sealed class PersistedReminderTime
    {
        public string TimeText { get; set; } = "09:00";
    }

    private sealed class PersistedSessionRecord
    {
        public string PlanName { get; set; } = string.Empty;
        public int DurationSeconds { get; set; }
        public int Cycles { get; set; }
        public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.Now;
    }

    private sealed class PersistedAiSettings
    {
        public string Endpoint { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string SystemPrompt { get; set; } = string.Empty;
    }
}
