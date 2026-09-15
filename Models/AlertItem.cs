namespace TgSpecialWatch.Models;

public sealed class AlertItem
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.Now;

    public string SourceTitle { get; init; } = "";

    public string MessagePreview { get; init; } = "";

    public string MatchedRuleName { get; init; } = "";

    public AlertLevel Level { get; init; } = AlertLevel.Strong;

    public long? PeerId { get; init; }

    public int? MessageId { get; init; }
}
