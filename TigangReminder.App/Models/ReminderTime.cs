using CommunityToolkit.Mvvm.ComponentModel;

namespace TigangReminder_App.Models;

public partial class ReminderTime : ObservableObject
{
    [ObservableProperty]
    private string timeText = "09:00";

    public bool TryGetTimeSpan(out TimeSpan time)
    {
        return TimeSpan.TryParse(TimeText, out time);
    }

    public string DisplayText => TimeText;
}
