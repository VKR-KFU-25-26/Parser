using CourtParser.Models.Entities;

namespace CourtParser.Common.Kafka.Messages;

/// <summary>
/// Сообщение для топика
/// </summary>
public class CourtCaseMessage : CourtCase
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}