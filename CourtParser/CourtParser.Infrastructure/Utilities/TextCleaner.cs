using System.Text.RegularExpressions;

namespace CourtParser.Infrastructure.Utilities;

/// <summary>
/// Класс для очистки текста и извлечения данных о сторонах судебного процесса
/// </summary>
public static class TextCleaner
{
    /// <summary>
    /// Убирает лишние пробелы, табы
    /// </summary>
    public static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var cleaned = string.Join(" ", text.Split([' ', '\n', '\r', '\t'], 
            StringSplitOptions.RemoveEmptyEntries)).Trim();

        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        cleaned = Regex.Replace(cleaned, @"\s*-\s*", "-");
        
        return cleaned;
    }

    /// <summary>
    /// Извлекает данные об истце и ответчике из текстового представления сторон процесса
    /// </summary>
    public static (string plaintiff, string defendant) ExtractParties(string partiesText)
    {
        if (string.IsNullOrWhiteSpace(partiesText))
            return ("Не указан", "Не указан");

        var cleaned = CleanText(partiesText);
        
        if (cleaned.Contains("|"))
        {
            var parts = cleaned.Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var plaintiff = ExtractPlaintiff(parts[0]);
                var defendant = ExtractDefendant(parts[1]);
                return (plaintiff, defendant);
            }
        }

        if (cleaned.Contains(";"))
        {
            var parts = cleaned.Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var plaintiff = ExtractPlaintiff(parts[0]);
                var defendant = ExtractDefendant(parts[1]);
                return (plaintiff, defendant);
            }
        }

        return ExtractPartiesFromText(cleaned);
    }

    /// <summary>
    /// Извлекает и очищает имя истца из текстового фрагмента
    /// </summary>
    private static string ExtractPlaintiff(string text)
    {
        var cleaned = CleanText(text);
        
        // Убираем префиксы
        cleaned = cleaned.Replace("Истец:", "").Replace("Истец", "").Trim();
        cleaned = cleaned.Replace("Заявитель:", "").Replace("Заявитель", "").Trim();
        
        return CleanPartyName(cleaned);
    }

    /// <summary>
    /// Извлекает и очищает имя ответчика из текстового фрагмента
    /// </summary>
    private static string ExtractDefendant(string text)
    {
        var cleaned = CleanText(text);
        
        // Убираем префиксы
        cleaned = cleaned.Replace("Ответчик:", "").Replace("Ответчик", "").Trim();
        cleaned = cleaned.Replace("Ответчики:", "").Replace("Ответчики", "").Trim();
        
        return CleanPartyName(cleaned);
    }

    /// <summary>
    /// Извлечь данные об истце и ответчике из текста без явных разделителей
    /// </summary>
    private static (string plaintiff, string defendant) ExtractPartiesFromText(string text)
    {
        var cleaned = CleanText(text);
        
        var patterns = new[]
        {
            @"(.+)\s+против\s+(.+)",
            @"(.+)\s+к\s+(.+)",
            @"(.+)\s+о\s+(.+)",
            @"Истец:\s*(.+)\s*Ответчик:\s*(.+)"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(cleaned, pattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count >= 3)
            {
                var plaintiff = CleanPartyName(match.Groups[1].Value);
                var defendant = CleanPartyName(match.Groups[2].Value);
                return (plaintiff, defendant);
            }
        }

        return (CleanPartyName(cleaned), "Не указан");
    }

    /// <summary>
    /// Выполняет финальную очистку имени стороны процесса
    /// </summary>
    private static string CleanPartyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Не указан";

        var cleaned = CleanText(name);
        
        var wordsToRemove = new[]
        {
            "истец", "ответчик", "заявитель", "истцы", "ответчики", "заявители",
            ":", "-", "–", "—"
        };

        foreach (var word in wordsToRemove)
        {
            cleaned = cleaned.Replace(word, "").Trim();
        }

        return CleanText(cleaned);
    }
}