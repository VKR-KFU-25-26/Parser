using CourtParser.Models.Entities;

namespace CourtParser.Common.Interfaces;

/// <summary>
/// Интерфейс для парсера
/// </summary>
public interface IParser
{
    Task<List<CourtCase>> ParseCasesAsync(List<string> regions, int page); 
}