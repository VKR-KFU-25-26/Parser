using CourtParser.Common.Interfaces;
using CourtParser.Common.Kafka.Abstraction;
using CourtParser.Common.Kafka.KafkaHelpers;
using CourtParser.Common.Options;
using CourtParser.Infrastructure.Parsers;
using CourtParser.Infrastructure.Producers;
using CourtParser.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CourtParser.Infrastructure;

/// <summary>
/// Класс для добавления сервисов
/// </summary>
public static class Entry
{
    public static void AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<IParser, SudactParser>(_ =>
        {
            // client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            // client.DefaultRequestHeaders.Add("Accept", "application/json, text/html, */*");
            // client.DefaultRequestHeaders.Add("Accept-Charset", "utf-8, windows-1251;q=0.7");
        });

        services.Configure<HttpClientOptions>(configuration.GetSection("HttpClientOptions"));
        
        services.AddSingleton(resolver => resolver.GetRequiredService<IOptions<HttpClientOptions>>().Value);
        
        services.AddScoped<RegionSelectionService>();
        services.AddScoped<SearchResultsParserService>();
        services.AddScoped<DecisionExtractionService>();
        
        services.AddScoped<IParser, SudactParser>();
        services.AddScoped<IParser, CourtDecisionsParser>();
        
        services.Configure<KafkaOptions>(configuration.GetSection("Kafka"));
        services.AddSingleton<IKafkaProducer, KafkaCourtMessageProducer>();
        services.AddSingleton<KafkaTopicHelpers>();
    }
}