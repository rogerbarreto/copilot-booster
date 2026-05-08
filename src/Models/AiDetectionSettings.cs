using System.Text.Json.Serialization;

namespace CopilotBooster.Models;

internal sealed class AiDetectionSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 300;

    [JsonPropertyName("confidenceThreshold")]
    public decimal ConfidenceThreshold { get; set; } = 0.5m;

    [JsonPropertyName("copilotPath")]
    public string CopilotPath { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";
}
