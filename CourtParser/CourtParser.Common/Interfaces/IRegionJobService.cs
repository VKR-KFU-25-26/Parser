namespace CourtParser.Common.Interfaces;

/// <summary>
/// Интерфейс пайплайна
/// </summary>
public interface IRegionJobService
{
    /// <summary>
    /// Основной пайплайн
    /// </summary>
    /// <param name="regionName"></param>
    Task ProcessRegionAsync(string regionName);
}