using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TigangReminder_App.Models;

namespace TigangReminder_App.Services;

public class AiPlanService
{
    private readonly HttpClient _httpClient = new();

    public async Task<AiPlanSuggestion> GenerateAsync(AiSettings settings, string goal, string experienceLevel, int availableMinutes, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Endpoint) ||
            string.IsNullOrWhiteSpace(settings.Model) ||
            string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return BuildFallback(goal, experienceLevel, availableMinutes, "未配置 API，已生成本地建议。");
        }

        var payload = new
        {
            model = settings.Model,
            temperature = 0.8,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = settings.SystemPrompt },
                new
                {
                    role = "user",
                    content = $"目标：{goal}\n经验等级：{experienceLevel}\n可用时长（分钟）：{availableMinutes}\n请给一个保守但可坚持的提肛训练计划。"
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(raw);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            return BuildFallback(goal, experienceLevel, availableMinutes, "AI 返回为空，已生成本地建议。");
        }

        var normalizedJson = ExtractJson(content);
        var suggestion = JsonSerializer.Deserialize<AiPlanSuggestion>(normalizedJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (suggestion is null)
        {
            return BuildFallback(goal, experienceLevel, availableMinutes, "AI 结果不可解析，已生成本地建议。");
        }

        suggestion.ContractSeconds = Math.Clamp(suggestion.ContractSeconds, 2, 15);
        suggestion.RelaxSeconds = Math.Clamp(suggestion.RelaxSeconds, 2, 15);
        suggestion.Cycles = Math.Clamp(suggestion.Cycles, 4, 50);
        suggestion.SuggestedTimes = suggestion.SuggestedTimes.Count == 0 ? ["09:00", "21:00"] : suggestion.SuggestedTimes;
        suggestion.Note = string.IsNullOrWhiteSpace(suggestion.Note) ? "AI 已生成节律建议。" : suggestion.Note;
        return suggestion;
    }

    private static AiPlanSuggestion BuildFallback(string goal, string experienceLevel, int availableMinutes, string note)
    {
        var cycles = experienceLevel.Contains("进阶", StringComparison.OrdinalIgnoreCase) ? 18 : 12;
        var contract = availableMinutes >= 8 ? 4 : 3;
        var relax = contract + 1;

        return new AiPlanSuggestion
        {
            Name = $"{experienceLevel} {goal[..Math.Min(goal.Length, 8)]}计划",
            Summary = $"围绕“{goal}”生成的本地建议，适合 {experienceLevel} 用户以低负担建立稳定训练。",
            ContractSeconds = contract,
            RelaxSeconds = relax,
            Cycles = cycles,
            SuggestedTimes = availableMinutes >= 8 ? ["08:30", "13:30", "21:00"] : ["09:00", "21:00"],
            IsFallback = true,
            Note = note
        };
    }

    private static string ExtractJson(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : trimmed;
    }
}
