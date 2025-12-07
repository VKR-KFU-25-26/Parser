namespace CourtParser.Common.Options;

/// <summary>
/// Настройки http клиента
/// </summary>
public class HttpClientOptions
{
    /// <summary>
    /// Сайт суда
    /// </summary>
    public string Url { get; set; } = "";
    
    /// <summary>
    /// Параметры запроса
    /// </summary>
    public string Query { get; set; } = "";
    //public string Token { get; set; }
}