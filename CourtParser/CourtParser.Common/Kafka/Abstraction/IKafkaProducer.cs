using CourtParser.Common.Kafka.Messages;

namespace CourtParser.Common.Kafka.Abstraction;

/// <summary>
/// Интерфейс для взаимодействия с кафкой
/// </summary>
public interface IKafkaProducer
{
    Task ProduceAsync(string topic, CourtCaseMessage message);
    Task ProduceBatchAsync(string topic, List<CourtCaseMessage> messages);
    Task ProduceSingleMockMessageAsync(string topic);
}