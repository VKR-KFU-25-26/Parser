using CourtParser.Common.Kafka.Messages;

namespace CourtParser.Common.Kafka.Abstraction;

/// <summary>
/// Интерфейс для взаимодействия с кафкой
/// </summary>
public interface IKafkaProducer
{
    /// <summary>
    /// Метод для обработки одиночного сообщения 
    /// </summary>
    /// <param name="topic">Топик</param>
    /// <param name="message">Сообщение</param>
    Task ProduceAsync(string topic, CourtCaseMessage message);
    
    /// <summary>
    /// Метод для обработки коллекции сообщений
    /// </summary>
    /// <param name="topic">Топик</param>
    /// <param name="messages">Сообщения</param>
    Task ProduceBatchAsync(string topic, List<CourtCaseMessage> messages);
    
    /// <summary>
    /// Мок
    /// </summary>
    /// <param name="topic">Топик</param>
    Task ProduceSingleMockMessageAsync(string topic);
}