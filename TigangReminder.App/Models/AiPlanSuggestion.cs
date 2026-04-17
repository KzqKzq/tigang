namespace TigangReminder_App.Models;

public class AiPlanSuggestion
{
    public string Name { get; set; } = "AI 节律计划";

    public string Summary { get; set; } = string.Empty;

    public int ContractSeconds { get; set; } = 3;

    public int RelaxSeconds { get; set; } = 3;

    public int Cycles { get; set; } = 12;

    public List<string> SuggestedTimes { get; set; } = ["09:00", "15:00", "21:00"];

    public bool IsFallback { get; set; }

    public string Note { get; set; } = string.Empty;
}
