using CourtParser.Models.Entities;

namespace CourtParser.Common.Interfaces;

/// <summary>
/// Интерфейс для парсера
/// </summary>
public interface IParser
{
    /// <summary>
    /// Метод для запуска задачи парсера
    /// </summary>
    /// <param name="regions">Регион, по которому будет запущена задача</param>
    /// <param name="page">Кол-во страниц с сайта</param>
    /// <param name="cancellationToken">Токен отмены</param>
    Task<List<CourtCase>> ParseCasesAsync(List<string> regions, int page, CancellationToken cancellationToken = default); 
}