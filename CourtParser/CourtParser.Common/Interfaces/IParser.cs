using CourtParser.Models.Entities;

namespace CourtParser.Common.Interfaces;

/// <summary>
/// Интерфейс для парсера
/// </summary>
public interface IParser
{
    /// <summary>
    /// Выполняет поиск дел по ключевому слову.
    /// </summary>
    Task<List<CourtCase>> ParseCasesAsync(string keyword, int page = 1);
    
    Task<List<CourtCase>> ParseCasesAsync(List<string> regions, int page); 
}