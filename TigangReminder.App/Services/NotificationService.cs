using CommunityToolkit.WinUI.Notifications;
using Windows.UI.Notifications;
using TigangReminder_App.Models;

namespace TigangReminder_App.Services;

public class NotificationService
{
    public void ShowInstantReminder(TrainingPlan plan, string subtitle)
    {
        var toast = BuildToast(plan, subtitle);
        ToastNotificationManagerCompat.CreateToastNotifier().Show(new ToastNotification(toast.GetXml()));
    }

    public void Reschedule(IEnumerable<TrainingPlan> plans)
    {
        var notifier = ToastNotificationManagerCompat.CreateToastNotifier();

        foreach (var scheduled in notifier.GetScheduledToastNotifications())
        {
            notifier.RemoveFromSchedule(scheduled);
        }

        var now = DateTimeOffset.Now;

        foreach (var (plan, remindAt) in BuildUpcomingReminders(plans, now, 14))
        {
            var scheduledToast = new ScheduledToastNotification(BuildToast(plan, "按计划开始一组提肛训练").GetXml(), remindAt)
            {
                Tag = plan.Id,
                Group = "TigangReminder"
            };

            notifier.AddToSchedule(scheduledToast);
        }
    }

    public IReadOnlyList<(TrainingPlan Plan, DateTimeOffset When)> BuildUpcomingReminders(IEnumerable<TrainingPlan> plans, DateTimeOffset from, int days)
    {
        var results = new List<(TrainingPlan Plan, DateTimeOffset When)>();
        var endDate = from.Date.AddDays(days);

        foreach (var plan in plans.Where(static p => p.IsEnabled))
        {
            foreach (var day in EachDay(from.Date, endDate))
            {
                if (!plan.IsActiveOn(day.DayOfWeek))
                {
                    continue;
                }

                foreach (var reminder in plan.ReminderTimes)
                {
                    if (!reminder.TryGetTimeSpan(out var time))
                    {
                        continue;
                    }

                    var when = day.Add(time);
                    if (when <= from)
                    {
                        continue;
                    }

                    results.Add((plan, new DateTimeOffset(when, from.Offset)));
                }
            }
        }

        return results.OrderBy(static item => item.When).ToList();
    }

    private static IEnumerable<DateTime> EachDay(DateTime start, DateTime endExclusive)
    {
        for (var date = start; date < endExclusive; date = date.AddDays(1))
        {
            yield return date;
        }
    }

    private static ToastContent BuildToast(TrainingPlan plan, string subtitle)
    {
        return new ToastContentBuilder()
            .AddArgument("action", "open-training")
            .AddArgument("planId", plan.Id)
            .AddText(plan.Name)
            .AddText(subtitle)
            .AddText($"{plan.DurationSummary} · {plan.ActiveDaySummary}")
            .GetToastContent();
    }
}
