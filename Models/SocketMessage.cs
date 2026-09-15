using System.Text.Json.Serialization;

namespace TgSpecialWatch.Models;

/// <summary>PC ↔ Android 行分隔 JSON 协议。</summary>
public sealed class SocketMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("preview")]
    public string? Preview { get; set; }

    [JsonPropertyName("rule")]
    public string? Rule { get; set; }

    [JsonPropertyName("level")]
    public string? Level { get; set; }

    [JsonPropertyName("at")]
    public string? At { get; set; }

    [JsonPropertyName("client")]
    public string? Client { get; set; }

    public static SocketMessage Alert(AlertItem item) => new()
    {
        Type = "alert",
        Id = item.Id.ToString("N"),
        Source = item.SourceTitle,
        Preview = item.MessagePreview,
        Rule = item.MatchedRuleName,
        Level = item.Level.ToString(),
        At = item.ReceivedAt.ToString("o")
    };

    public static SocketMessage Clear(Guid id) => new()
    {
        Type = "clear",
        Id = id.ToString("N")
    };

    public static SocketMessage Pong() => new() { Type = "pong" };

    public static SocketMessage Welcome() => new() { Type = "welcome" };
}
