using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using CourtParser.Common.Interfaces;
using CourtParser.Models.Entities;
using Microsoft.Extensions.Logging;

namespace CourtParser.Infrastructure.Parsers;

public class SudactParser : IParser
{
    private readonly HttpClient _httpClient;
    private readonly IHtmlParser _htmlParser;
    private readonly ILogger<SudactParser> _logger;

    public SudactParser(HttpClient httpClient, ILogger<SudactParser> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _htmlParser = new HtmlParser();
    }

   public async Task<List<CourtCase>> ParseCasesAsync(string keyword, int page = 0)
   {
        try
        {
            var url = $"https://sudact.ru/vsrf/doc_ajax/?vsrf-txt={Uri.EscapeDataString(keyword)}";
            
            _logger.LogInformation("Запрос к URL: {Url}", url);
            
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("HTTP ошибка: {StatusCode}", response.StatusCode);
                return new List<CourtCase>();
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            
            using var jsonDoc = JsonDocument.Parse(responseContent);
            var root = jsonDoc.RootElement;

            string htmlContent = null!;
            
            if (root.TryGetProperty("content", out var contentElement))
            {
                htmlContent = contentElement.GetString()!;
            }
            else
            {
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        htmlContent = property.Value.GetString()!;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(htmlContent))
            {
                _logger.LogWarning("HTML контент не найден в JSON ответе");
                return new List<CourtCase>();
            }

            var document = await _htmlParser.ParseDocumentAsync(htmlContent);
            var cases = new List<CourtCase>();
            var caseElements = document.QuerySelectorAll("ul.results li");

            _logger.LogInformation("Найдено элементов: {Count}", caseElements.Length);

            foreach (var li in caseElements)
            {
                try
                {
                    var a = li.QuerySelector("h4 a");
                    if (a == null) continue;

                    var title = CleanText(a.TextContent);
                    var href = a.GetAttribute("href");
                    var link = !string.IsNullOrEmpty(href) ? "https://sudact.ru" + href.Trim() : string.Empty;
                    
                    // Извлекаем номер дела из заголовка
                    var caseNumber = ExtractCaseNumber(title);
                    
                    // Извлекаем тип суда (только первую строку)
                    var justiceElement = li.QuerySelector(".b-justice");
                    string courtType = string.Empty;
                    
                    if (justiceElement != null)
                    {
                        // Клонируем элемент, чтобы не изменять оригинальный DOM
                        var clone = justiceElement.Clone() as AngleSharp.Html.Dom.IHtmlElement;
                        // Удаляем дочерний элемент с сутью спора
                        var additionChild = clone!.QuerySelector(".addution");
                        additionChild?.Remove();
                        
                        courtType = CleanText(clone.TextContent);
                    }
                    
                    // Извлекаем суть спора
                    var additionElement = li.QuerySelector(".addution");
                    string subject = string.Empty;

                    if (additionElement != null)
                    {
                        var additionText = CleanText(additionElement.TextContent);
                        
                        // Убираем префикс "Суть спора:" если есть
                        subject = additionText.StartsWith("Суть спора:") ? additionText.Replace("Суть спора:", "").Trim() : additionText;
                    }

                    var courtCase = new CourtCase
                    {
                        Title = title,
                        CaseNumber = caseNumber, // Новое поле
                        Link = link,
                        CourtType = courtType
                    };

                    cases.Add(courtCase);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при парсинге элемента дела");
                }
            }

            return cases;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при парсинге дел");
            return new List<CourtCase>();
        }
   }

   public Task<List<CourtCase>> ParseCasesAsync(List<string> regions, int page)
   {
       throw new NotImplementedException();
   }

   // Новый метод для извлечения номера дела из заголовка
    private string ExtractCaseNumber(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        // Ищем паттерн номера дела: "№ АXX-XXXXX/XXXX"
        var match = Regex.Match(title, @"№\s*([А-Я]\d+-\d+/\d+)");
        if (match.Success && match.Groups.Count > 1)
        {
            return match.Groups[1].Value;
        }

        // Альтернативный паттерн, если первый не сработал
        match = Regex.Match(title, @"дело\s*№?\s*([А-Я]\d+-\d+/\d+)");
        if (match.Success && match.Groups.Count > 1)
        {
            return match.Groups[1].Value;
        }

        return string.Empty;
    }

    private static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var cleaned = string.Join(" ", text.Split([' ', '\n', '\r', '\t'], 
            StringSplitOptions.RemoveEmptyEntries)).Trim();

        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        cleaned = Regex.Replace(cleaned, @"\s*-\s*", "-");
        
        return cleaned;
    }
    
}