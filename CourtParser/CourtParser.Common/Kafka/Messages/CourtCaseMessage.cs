using CourtParser.Models.Entities;

namespace CourtParser.Common.Kafka.Messages;

public class CourtCaseMessage : CourtCase
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}