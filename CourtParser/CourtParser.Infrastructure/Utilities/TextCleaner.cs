using System.Text.RegularExpressions;

namespace CourtParser.Infrastructure.Utilities;

/// <summary>
/// Класс для очистки текста
/// </summary>
public static class TextCleaner
{
    public static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Убираем все лишние пробелы, переносы, табы
        var cleaned = string.Join(" ", text.Split([' ', '\n', '\r', '\t'], 
            StringSplitOptions.RemoveEmptyEntries)).Trim();

        // Убираем лишние пробелы вокруг дефисов и других символов
        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        cleaned = Regex.Replace(cleaned, @"\s*-\s*", "-");
        
        return cleaned;
    }

    public static (string plaintiff, string defendant) ExtractParties(string partiesText)
    {
        if (string.IsNullOrWhiteSpace(partiesText))
            return ("Не указан", "Не указан");

        var cleaned = CleanText(partiesText);
        
        // Улучшенный парсинг участников процесса
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

        // Альтернативные разделители
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

        // Если нет явного разделителя, пытаемся определить по контексту
        return ExtractPartiesFromText(cleaned);
    }

    private static string ExtractPlaintiff(string text)
    {
        var cleaned = CleanText(text);
        
        // Убираем префиксы
        cleaned = cleaned.Replace("Истец:", "").Replace("Истец", "").Trim();
        cleaned = cleaned.Replace("Заявитель:", "").Replace("Заявитель", "").Trim();
        
        return CleanPartyName(cleaned);
    }

    private static string ExtractDefendant(string text)
    {
        var cleaned = CleanText(text);
        
        // Убираем префиксы
        cleaned = cleaned.Replace("Ответчик:", "").Replace("Ответчик", "").Trim();
        cleaned = cleaned.Replace("Ответчики:", "").Replace("Ответчики", "").Trim();
        
        return CleanPartyName(cleaned);
    }

    private static (string plaintiff, string defendant) ExtractPartiesFromText(string text)
    {
        var cleaned = CleanText(text);
        
        // Паттерны для определения истца и ответчика
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

        // Если не удалось разделить, возвращаем весь текст как истца
        return (CleanPartyName(cleaned), "Не указан");
    }

    private static string CleanPartyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Не указан";

        var cleaned = CleanText(name);
        
        // Убираем лишние слова
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