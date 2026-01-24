namespace CourtParser.Models.Entities;

/// <summary>
/// Сырое дело, получаемое напрямую с сайта суда
/// </summary>
public class CourtCase
{
    /// <summary>
    /// Первичная информация
    /// </summary>
    public string Title { get; set; } = null!;
    
    /// <summary>
    /// Ссылка из источника
    /// </summary>
    public string Link { get; set; } = null!;

    /// <summary>
    /// Дата начало 
    /// </summary>
    public DateTime StartDate { get; set; }
    
    /// <summary>
    /// Движение дела (список событий)
    /// </summary>
    public List<CourtCaseMovement> CaseMovements { get; set; } = [];
    
    /// <summary>
    /// Номер дела
    /// </summary>
    public string CaseNumber { get; set; } = null!;
    
    /// <summary>
    /// Тип дела
    /// </summary>
    public string CourtType { get; set; } = null!;
    
    /// <summary>
    /// Ссылка на оригинальный сайт суда
    /// </summary>
    public string OriginalCaseLink { get; set; } = string.Empty;
    
    /// <summary>
    /// Есть ли решение
    /// </summary>
    public bool HasDecision { get; set; }
    
    /// <summary>
    /// Ссылка на решение
    /// </summary>
    public string DecisionLink { get; set; } = string.Empty;
    
    /// <summary>
    /// Тип решения (мотивированное, определение и т.д.)
    /// </summary>
    public string DecisionType { get; set; } = string.Empty;
    
    /// <summary>
    /// Федеральный округ
    /// </summary>
    public string FederalDistrict { get; set; } = string.Empty;
    
    /// <summary>
    /// Регион (субъект РФ)
    /// </summary>
    public string Region { get; set; } = string.Empty;
    
    /// <summary>
    /// Истец (отдельное поле)
    /// </summary>
    public string Plaintiff { get; set; } = string.Empty;
    
    /// <summary>
    /// Ответчик (отдельное поле)
    /// </summary>
    public string Defendant { get; set; } = string.Empty;
    
    /// <summary>
    /// Третьи лица (если есть)
    /// </summary>
    public string ThirdParties { get; set; } = string.Empty;
    
    /// <summary>
    /// Представители (если есть)
    /// </summary>
    public string? Representatives { get; set; } = string.Empty;
    
    /// <summary>
    /// Дата рассмотрения (Дата поступления дела)
    /// </summary>
    public DateTime? ReceivedDate { get; set; }
    
    /// <summary>
    /// Категория дела
    /// </summary>
    public string CaseCategory { get; set; } = string.Empty;
    
    /// <summary>
    /// Результат дела (например: "Заявление ВОЗВРАЩЕНО заявителю")
    /// </summary>
    public string CaseResult { get; set; } = string.Empty;
    
    /// <summary>
    /// Подкатегория дела
    /// </summary>
    public string CaseSubcategory { get; set; } = string.Empty;
    
    /// <summary>
    /// Решение контент
    /// </summary>
    public string? DecisionContent { get; set; }
    
    /// <summary>
    /// Судья
    /// </summary>
    public string? JudgeName { get; set; } = string.Empty;  
}