using CommunityToolkit.Mvvm.ComponentModel;

namespace TigangReminder_App.Models;

public partial class AiSettings : ObservableObject
{
    [ObservableProperty]
    private string endpoint = "https://your-openai-compatible-endpoint/v1/chat/completions";

    [ObservableProperty]
    private string model = "your-model";

    [ObservableProperty]
    private string apiKey = string.Empty;

    [ObservableProperty]
    private string systemPrompt = "你是一名盆底肌训练教练。返回严格 JSON，包含 name、summary、contractSeconds、relaxSeconds、cycles、suggestedTimes。";
}
