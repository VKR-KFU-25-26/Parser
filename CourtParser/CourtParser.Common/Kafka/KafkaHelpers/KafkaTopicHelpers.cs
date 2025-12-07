using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;

namespace CourtParser.Common.Kafka.KafkaHelpers;

public class KafkaTopicHelpers(ILogger<KafkaTopicHelpers> logger)
{
    public async Task EnsureTopicExistsAsync(
        string bootstrapServers, 
        string topicName, 
        int numPartitions = 1, 
        short replicationFactor = 1)
    {
        try
        {
            using var adminClient = new AdminClientBuilder(new AdminClientConfig
            {
                BootstrapServers = bootstrapServers
            }).Build();

            var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(10));
            var topicExists = metadata.Topics.Any(t => t.Topic == topicName);

            if (topicExists)
            {
                logger.LogInformation("Топик {TopicName} уже существует", topicName);
                return;
            }

            var topicSpecification = new TopicSpecification
            {
                Name = topicName,
                NumPartitions = numPartitions,
                ReplicationFactor = replicationFactor,
                Configs = new Dictionary<string, string>
                {
                    { "retention.ms", "604800000" }
                }
            };

            await adminClient.CreateTopicsAsync([topicSpecification]);
            logger.LogInformation("Топик {TopicName} успешно создан", topicName);
        }
        catch (CreateTopicsException ex) when (ex.Results[0].Error.Code == ErrorCode.TopicAlreadyExists)
        {
            logger.LogInformation("Топик {TopicName} уже существует", topicName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при создании топика {TopicName}", topicName);
            throw;
        }
    }
}