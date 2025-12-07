using System.Text.Json;
using Confluent.Kafka;
using CourtParser.Common.Kafka.Abstraction;
using CourtParser.Common.Kafka.KafkaHelpers;
using CourtParser.Common.Kafka.Messages;
using CourtParser.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CourtParser.Infrastructure.Producers;

public class KafkaCourtMessageProducer : IKafkaProducer
{
    private readonly IProducer<Null, string> _producer;
    private readonly KafkaOptions _config;
    private readonly ILogger<KafkaCourtMessageProducer> _logger;
    private readonly KafkaTopicHelpers _topicHelpers;
    private readonly HashSet<string> _verifiedTopics = [];

    public KafkaCourtMessageProducer(
        IOptions<KafkaOptions> config, 
        ILogger<KafkaCourtMessageProducer> logger,
        KafkaTopicHelpers topicHelpers)
    {
        _config = config.Value;
        _logger = logger;
        _topicHelpers = topicHelpers;

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = _config.BootstrapServers,
            ClientId = _config.ClientId,
            MessageSendMaxRetries = _config.MessageSendMaxRetries,
            RetryBackoffMs = _config.RetryBackoffMs,
        };

        _producer = new ProducerBuilder<Null, string>(producerConfig)
            .SetErrorHandler(OnProducerError)
            .Build();
    }

    public async Task ProduceAsync(string topic, CourtCaseMessage message)
    {
        await EnsureTopicExistsAsync(topic);
        
        try
        {
            var jsonMessage = JsonSerializer.Serialize(message, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var kafkaMessage = new Message<Null, string> 
            { 
                Value = jsonMessage,
                Timestamp = new Timestamp(DateTime.UtcNow)
            };

            var result = await _producer.ProduceAsync(topic, kafkaMessage);
            
            _logger.LogDebug("Сообщение отправлено в топик {Topic}, partition {Partition}, offset {Offset}",
                result.Topic, result.Partition.Value, result.Offset.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка отправки сообщения в топик {Topic}", topic);
            throw;
        }
    }

    public async Task ProduceBatchAsync(string topic, List<CourtCaseMessage> messages)
    {
        await EnsureTopicExistsAsync(topic);
        
        var sendTasks = messages.Select(message => ProduceAsync(topic, message));
        
        try
        {
            await Task.WhenAll(sendTasks);
            _logger.LogInformation("Успешно отправлено {Count} сообщений в топик {Topic}", 
                messages.Count, topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибки при отправке batch в топик {Topic}", topic);
            throw;
        }
    }

    public async Task ProduceBatchWithRetryAsync(string topic, List<CourtCaseMessage> messages, int maxRetries = 3)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await ProduceBatchAsync(topic, messages);
                return;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.LogWarning(ex, "Попытка {Attempt}/{MaxRetries} не удалась. Повтор через 2 секунды...", 
                    attempt, maxRetries);
                await Task.Delay(2000);
            }
        }
        
        throw new Exception($"Не удалось отправить batch после {maxRetries} попыток");
    }

    private async Task EnsureTopicExistsAsync(string topic, bool forceCheck = false)
    {
        if (_verifiedTopics.Contains(topic) && !forceCheck)
            return;

        await _topicHelpers.EnsureTopicExistsAsync(
            _config.BootstrapServers, 
            topic,
            numPartitions: 3,
            replicationFactor: 1);
        
        _verifiedTopics.Add(topic);
    }

    private void OnProducerError(IProducer<Null, string> producer, Error error)
    {
        _logger.LogError("Kafka producer error: {Reason} (Code: {Code})", error.Reason, error.Code);
    }
    
    public async Task ProduceSingleMockMessageAsync(string topic)
    {
        var random = new Random();
        
        var mockMessage = new CourtCaseMessage
        {
            Title = "Тестовое дело - ТЕСТ-001/2024",
            Link = "https://example.com/test/1",
            CaseNumber = $"ТЕСТ-{random.Next(1,9999)}",
            CourtType = "Тестовый суд",
            Description = "Тестовое дело для проверки работы Kafka",
            OriginalCaseLink = "https://test.sudrf.ru/test/1",
            Subject = "тестовое дело",
            HasDecision = true,
            DecisionLink = "https://example.com/test/decision/1",
            DecisionType = "Тестовое решение",
            FederalDistrict = "Тестовый федеральный округ",
            Region = "Тестовый регион",
            Plaintiff = "Тестовый истец",
            Defendant = "Тестовый ответчик",
            ThirdParties = "Тестовые третьи лица",
            Representatives = "ТЕстовые представители",
            CaseResult = "Типо результат",
            ReceivedDate = DateTime.UtcNow.AddDays(-1),
            CaseCategory = "Тестовая категория",
            CaseSubcategory = "Тестовая подкатегория",
            DecisionContent = "Это тестовое решение для проверки работы системы",
            JudgeName = "Тестовый судья",
            Timestamp = DateTime.UtcNow,
            CaseMovements =
            [
                new()
                {
                    EventName = "Регистрация иска в суде",
                    EventResult = "Иск принят к производству",
                    Basis = $"ст. {random.Next(1,9999)} ГПК РФ",
                    EventDate = DateTime.UtcNow.AddDays(-12)
                },

                new()
                {
                    EventName = "Передача материалов судье",
                    EventResult = "Материалы переданы судье Тестову А.А.",
                    Basis = "",
                    EventDate = DateTime.UtcNow.AddDays(-random.Next(1,3))
                },
                
                new()
                {
                    EventName = "Вынесение решения",
                    EventResult = "Иск удовлетворён",
                    Basis = "Решение суда от " + DateTime.UtcNow.AddDays(-3).ToShortDateString(),
                    EventDate = DateTime.UtcNow.AddDays(-3)
                }
            ]
        };

        try
        {
            await ProduceAsync(topic, mockMessage);
            _logger.LogInformation("✅ Успешно отправлено тестовое сообщение в топик {Topic}", topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка отправки тестового сообщения в топик {Topic}", topic);
            throw;
        }
    }

    public void Dispose()
    {
        _producer?.Flush(TimeSpan.FromSeconds(5));
        _producer?.Dispose();
    }
}