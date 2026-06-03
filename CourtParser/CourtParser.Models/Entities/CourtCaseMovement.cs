namespace CourtParser.Models.Entities;

/// <summary>
/// Модель для представления события движения судебного дела
/// Содержит информацию о каждом этапе процесса рассмотрения дела в суде
/// </summary>
public class CourtCaseMovement
{
    /// <summary>
    /// Наименование процессуального события или действия
    /// </summary>
    public string? EventName { get; set; } = "";
    
    /// <summary>
    /// Результат или итог процессуального события
    /// Может быть пустым, если результат не указан
    /// </summary>
    public string? EventResult { get; set; } = "";
    
    /// <summary>
    /// Правовые основания или нормативные акты, на которых основано событие, в основном пустые
    /// </summary>
    public string? Basis { get; set; } = "";
    
    /// <summary>
    /// Дата совершения процессуального события
    /// Является ключевой информацией для построения хронологии дела
    /// </summary>
    public DateTime? EventDate { get; set; }
}