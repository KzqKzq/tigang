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
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions) ?? CreateDefaultSettings();
        Normalize(settings);
        return settings;
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Normalize(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);

        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
    }

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
            plan.NotifyDerivedStateChanged();
        }

        settings.SessionHistory = settings.SessionHistory
            .OrderByDescending(static record => record.CompletedAt)
            .Take(120)
            .ToList();
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
}
