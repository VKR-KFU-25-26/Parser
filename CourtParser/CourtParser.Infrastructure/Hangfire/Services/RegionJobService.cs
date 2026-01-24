using CourtParser.Common.Interfaces;
using CourtParser.Common.Kafka.Abstraction;
using CourtParser.Common.Kafka.Messages;
using CourtParser.Common.Options;
using CourtParser.Infrastructure.Parsers;
using CourtParser.Models.Entities;
using CourtParser.Models.Regions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CourtParser.Infrastructure.Hangfire.Services;

public class RegionJobService : IRegionJobService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RegionJobService> _logger;
    private readonly KafkaOptions _kafkaConfig;
    private readonly WorkerOptions _workerOptions;

    public RegionJobService(
        IServiceProvider serviceProvider,
        ILogger<RegionJobService> logger,
        IOptions<KafkaOptions> kafkaConfig,
        IOptions<WorkerOptions> workerOptions)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _kafkaConfig = kafkaConfig.Value;
        _workerOptions = workerOptions.Value;
    }

    public async Task ProcessRegionAsync(string regionName)
    {
        _logger.LogInformation("▶️ Запущена задача для региона: {Region}", regionName);

        using var scope = _serviceProvider.CreateScope();

        var kafkaProducer = scope.ServiceProvider.GetRequiredService<IKafkaProducer>();
        var parsers = scope.ServiceProvider.GetServices<IParser>().ToList();
        var courtParser = parsers.FirstOrDefault(p => p is CourtDecisionsParser);

        if (courtParser == null)
        {
            _logger.LogWarning("❌ {ParserName} не найден", nameof(CourtDecisionsParser));
            return;
        }

        try
        {
            var federalDistrict = RussianRegions.GetFederalDistrictForRegion(regionName);
            _logger.LogInformation("🗺️ Регион {Region} относится к {District}", regionName, federalDistrict);

            // Парсим дела для региона
            var cases = await courtParser.ParseCasesAsync([regionName], 1);

            if (cases == null! || cases.Count == 0)
            {
                _logger.LogInformation("📭 Регион {Region}: дела не найдены", regionName);
                return;
            }

            // Отправляем в Kafka
            var messages = cases.Select(c => CreateCourtCaseMessage(c, federalDistrict)).ToList();
            await kafkaProducer.ProduceBatchAsync(_kafkaConfig.Topic, messages);

            _logger.LogInformation("✅ Регион {Region}: отправлено в Kafka {Count} дел", regionName, messages.Count);

            PrintRegionResult(messages, regionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка при обработке региона {Region}", regionName);
            throw; // Hangfire перезапустит задачу
        }
    }

    private CourtCaseMessage CreateCourtCaseMessage(CourtCase model, string federalDistrict)
    {
        return new CourtCaseMessage
        {
            Title = model.Title,
            Link = model.Link,
            CaseNumber = model.CaseNumber,
            CourtType = model.CourtType,
            Timestamp = DateTime.UtcNow,
            HasDecision = model.HasDecision,
            DecisionLink = model.DecisionLink,
            DecisionType = model.DecisionType,
            FederalDistrict = federalDistrict, 
            Region = model.Region,
            Plaintiff = model.Plaintiff,
            Defendant = model.Defendant,
            ThirdParties = model.ThirdParties,
            Representatives = model.Representatives,
            ReceivedDate = model.ReceivedDate,
            CaseCategory = model.CaseCategory,
            CaseSubcategory = model.CaseSubcategory,
            DecisionContent = model.DecisionContent,
            OriginalCaseLink = model.OriginalCaseLink,
            JudgeName = model.JudgeName,
            CaseResult = model.CaseResult,
            StartDate = model.StartDate,
            CaseMovements = model.CaseMovements
        };
    }

    private void PrintRegionResult(List<CourtCaseMessage> messages, string region)
    {
        if (messages.Count == 0)
        {
            Console.WriteLine($"❌ Регион {region}: дела не найдены");
            return;
        }

        Console.WriteLine($"\n🎯 РЕГИОН: {region.ToUpper()}");
        Console.WriteLine($"📊 Найдено дел: {messages.Count}\n");

        var casesWithDecisions = messages.Count(m => m.HasDecision);
        var embeddedDecisions = messages.Count(m => m.HasDecision && m.DecisionLink.Contains("#embedded_decision"));
        var externalDecisions = messages.Count(m => m.HasDecision && m.DecisionLink.Contains("#embedded_decision") == false);

        Console.WriteLine($"📈 СТАТИСТИКА:");
        Console.WriteLine($"   • Всего дел: {messages.Count}");
        Console.WriteLine($"   • С решениями: {casesWithDecisions}");
        Console.WriteLine($"   • Встроенных решений: {embeddedDecisions}");
        Console.WriteLine($"   • Внешних документов: {externalDecisions}");
        Console.WriteLine();

        // Выводим детальную информацию по каждому делу
        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            Console.WriteLine($"📋 Дело #{i + 1}");
            Console.WriteLine($"   🏛️  Суд: {message.CourtType}");
            Console.WriteLine($"   🔢 Номер: {message.CaseNumber}");

            if (!string.IsNullOrEmpty(message.CaseCategory))
            {
                Console.WriteLine($"   📂 Категория: {message.CaseCategory}");
            }

            if (message.ReceivedDate.HasValue)
            {
                Console.WriteLine($"   📅 Поступление: {message.ReceivedDate.Value:dd.MM.yyyy}");
            }

            if (!string.IsNullOrEmpty(message.Plaintiff))
            {
                Console.WriteLine($"   👤 Истец: {TruncateText(message.Plaintiff, 60)}");
            }

            if (!string.IsNullOrEmpty(message.Defendant))
            {
                Console.WriteLine($"   ⚖️  Ответчик: {TruncateText(message.Defendant, 60)}");
            }

            Console.WriteLine($"   ✅ Решение: {(message.HasDecision ? "ДА" : "НЕТ")}");

            if (message.HasDecision)
            {
                var decisionType = message.DecisionLink.Contains("#embedded_decision") 
                    ? "📄 Встроенное" 
                    : "📎 Отдельный документ";
                
                Console.WriteLine($"   💾 Тип: {decisionType}");
            }

            Console.WriteLine($"   🔗 Ссылка: {message.Link}");

            if (i < messages.Count - 1)
            {
                Console.WriteLine("   " + "".PadRight(60, '─'));
            }
        }

        // Итоговая статистика
        Console.WriteLine($"\n📈 ИТОГ по региону {region}:");
        Console.WriteLine($"   ✅ Дела с решениями: {casesWithDecisions}/{messages.Count}");

        if (casesWithDecisions > 0)
        {
            var successRate = (double)casesWithDecisions / messages.Count * 100;
            Console.WriteLine($"   📊 Эффективность: {successRate:F1}%");
        }

        Console.WriteLine("".PadRight(70, '═'));
    }

    
    /// <summary>
    /// Обрезает текст до указанной длины и добавляет многоточие
    /// </summary>
    private string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return text.Substring(0, maxLength - 3) + "...";
    }
}