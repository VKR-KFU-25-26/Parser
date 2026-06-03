using System.Globalization;
using System.Text.RegularExpressions;

namespace CourtParser.Infrastructure.Utilities;

/// <summary>
/// Класс для извлечения дат
/// </summary>
public static class DateExtractor
{
    public static (string receivedDate, string decisionDate, DateTime? receivedDateObj, DateTime? decisionDateObj) ExtractDates(string datesText)
    {
        if (string.IsNullOrWhiteSpace(datesText))
            return ("не указана", "не указана", null, null);

        var cleaned = TextCleaner.CleanText(datesText);
        
        DateTime? receivedDateObj = null;
        DateTime? decisionDateObj = null;
        string receivedDate = "не указана";
        string decisionDate = "не указана";

        // Паттерны для дат
        var patterns = new[]
        {
            @"Поступило:\s*(\d{1,2}\s+\w+\s+\d{4}).*?Решение:\s*(\d{1,2}\s+\w+\s+\d{4})",
            @"(\d{1,2}\s+\w+\s+\d{4}).*?(\d{1,2}\s+\w+\s+\d{4})",
            @"Поступило:\s*([^,]+).*?Решение:\s*([^,]+)"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(cleaned, pattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count >= 3)
            {
                receivedDate = match.Groups[1].Value.Trim();
                decisionDate = match.Groups[2].Value.Trim();
                
                receivedDateObj = ParseRussianDate(receivedDate);
                decisionDateObj = ParseRussianDate(decisionDate);
                break;
            }
        }

        return (receivedDate, decisionDate, receivedDateObj, decisionDateObj);
    }

    private static DateTime? ParseRussianDate(string dateText)
    {
        if (string.IsNullOrWhiteSpace(dateText))
            return null;

        try
        {
            // Заменяем русские названия месяцев на английские
            var months = new Dictionary<string, string>
            {
                ["января"] = "January", ["февраля"] = "February", ["марта"] = "March",
                ["апреля"] = "April", ["мая"] = "May", ["июня"] = "June",
                ["июля"] = "July", ["августа"] = "August", ["сентября"] = "September",
                ["октября"] = "October", ["ноября"] = "November", ["декабря"] = "December"
            };

            var cleanedDate = dateText;
            foreach (var month in months)
            {
                cleanedDate = cleanedDate.Replace(month.Key, month.Value);
            }

            if (DateTime.TryParse(cleanedDate, new CultureInfo("ru-RU"), DateTimeStyles.None, out var result))
            {
                return result;
            }
        }
        catch
        {
            // Игнорируем ошибки парсинга дат
        }

        return null;
    }
}