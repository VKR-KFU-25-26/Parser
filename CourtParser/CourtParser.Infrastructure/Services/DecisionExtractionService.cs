using CourtParser.Models.Entities;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using System.Text.RegularExpressions;

namespace CourtParser.Infrastructure.Services;

public class DecisionExtractionService(ILogger<DecisionExtractionService> logger)
{
    
    [Obsolete("Obsolete")]
    public async Task CheckAndExtractDecisionAsync(IPage page, CourtCase courtCase, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Проверяем наличие решения для дела: {CaseNumber}", courtCase.CaseNumber);
        
            // Сбрасываем флаг решения перед проверкой
            courtCase.HasDecision = false;
            courtCase.DecisionLink = string.Empty;
            courtCase.DecisionType = "Не найдено";
            courtCase.DecisionContent = string.Empty;
        
            // Ждем загрузки страницы
            await page.GoToAsync(courtCase.Link, WaitUntilNavigation.Networkidle2);
            await Task.Delay(2000, cancellationToken);

            // 1. Сначала извлекаем детальную информацию о деле
            await ExtractDetailedCaseInfo(page, courtCase);
        
            // 2. Извлекаем оригинальную ссылку на сайт суда
            await ExtractOriginalCaseLinkAsync(page, courtCase);
        
            // 3. ПРИОРИТЕТ: Проверяем наличие ссылок на файлы решений
            bool fileDecisionFound = await CheckFileDecisionLinks(page, courtCase);
            
            if (fileDecisionFound)
            {
                logger.LogInformation("✅ Найдено файловое решение для дела {CaseNumber}", courtCase.CaseNumber);
                return;
            }

            // 4. Если файлового решения нет, проверяем встроенное HTML-решение
            bool embeddedDecisionFound = await ExtractEmbeddedDecisionAsync(page, courtCase);
            
            if (embeddedDecisionFound)
            {
                logger.LogInformation("✅ Найдено встроенное решение для дела {CaseNumber}", courtCase.CaseNumber);
                return;
            }

            // 5. Ищем чистое решение
            bool cleanDecisionFound = await ExtractCleanDecisionAsync(page, courtCase);
            
            if (cleanDecisionFound)
            {
                logger.LogInformation("✅ Найдено чистое решение для дела {CaseNumber}", courtCase.CaseNumber);
                return;
            }

            // 6. Если ничего не найдено
            logger.LogInformation("❌ Для дела {CaseNumber} решение не найдено", courtCase.CaseNumber);
            courtCase.HasDecision = false;
            courtCase.DecisionType = "Не найдено";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при проверке решения для дела {CaseNumber}", courtCase.CaseNumber);
            courtCase.HasDecision = false;
            courtCase.DecisionLink = string.Empty;
            courtCase.DecisionType = "Ошибка при проверке";
        }
    }
    
    /// <summary>
    /// Извлекает решение и сохраняет HTML с минимальной очисткой
    /// </summary>
    private async Task<bool> ExtractDecisionWithHtml(IPage page, CourtCase courtCase)
{
    try
    {
        logger.LogInformation("Извлекаем решение с HTML для дела {CaseNumber}", courtCase.CaseNumber);

        // Получаем HTML страницы
        var pageContent = await page.GetContentAsync();
        
        // Находим начало решения
        int startIndex = FindDecisionStartInHtml(pageContent);
        if (startIndex == -1)
        {
            logger.LogInformation("Не найдено начало решения в HTML");
            return false;
        }

        logger.LogDebug("Начало решения найдено на позиции {StartIndex}", startIndex);
        
        // Вырезаем HTML от начала решения
        string htmlSolution = pageContent.Substring(startIndex);
        
        // Ищем конец решения
        int endIndex = FindDecisionEndInHtml(htmlSolution);
        if (endIndex == -1)
        {
            endIndex = Math.Min(50000, htmlSolution.Length);
            logger.LogDebug("Конец решения не найден, берем первые {EndIndex} символов", endIndex);
        }
        else
        {
            endIndex = Math.Min(endIndex, 50000);
            logger.LogDebug("Конец решения найден на позиции {EndIndex}", endIndex);
        }

        htmlSolution = htmlSolution.Substring(0, endIndex);
        logger.LogDebug("Извлечен HTML длиной {Length} символов", htmlSolution.Length);
        
        // Минимальная обработка для безопасности
        string processedHtml = CleanHtmlForStorage(htmlSolution);
        
        logger.LogDebug("После очистки HTML длиной {Length} символов", processedHtml.Length);
        
        if (string.IsNullOrWhiteSpace(processedHtml) || processedHtml.Length < 500)
        {
            logger.LogInformation("Извлеченный HTML слишком короткий: {Length}", processedHtml.Length);
            return false;
        }

        // Проверяем, что это действительно решение
        if (IsValidDecisionContent(processedHtml))
        {
            var documentType = DetermineDocumentTypeFromContent(processedHtml);
    
            courtCase.HasDecision = true;
            courtCase.DecisionLink = courtCase.Link + "#html_decision";
            courtCase.DecisionType = documentType;
            courtCase.DecisionContent = processedHtml; // Сохраняем HTML!

            logger.LogInformation("✅ Найдено HTML решение: {Type}, длина: {Length} символов", 
                documentType, processedHtml.Length);
            return true;
        }
        else
        {
            logger.LogInformation("HTML не прошел валидацию как решение");
            return false;
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Ошибка при извлечении HTML решения");
        return false;
    }
}
    /// <summary>
    /// Минимальная очистка HTML для безопасного хранения
    /// </summary>
    private string CleanHtmlForStorage(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        try
        {
            logger.LogDebug("Начинаем минимальную очистку HTML");
        
            // 1. Обрабатываем защищенные данные
            html = ProcessProtectedData(html);
        
            // 2. Убираем комментарии
            html = Regex.Replace(html, @"<!--.*?-->", "", RegexOptions.Singleline);
        
            // 3. Убираем только опасные теги (script, style, iframe)
            html = RemoveDangerousTags(html);
        
            // 4. Декодируем HTML сущности
            html = System.Net.WebUtility.HtmlDecode(html);
        
            // 5. Нормализуем пробелы
            html = Regex.Replace(html, @"\s+", " ");
        
            // 6. Убираем атрибуты событий (onclick, onload и т.д.)
            html = RemoveEventAttributes(html);
        
            logger.LogDebug("Минимальная очистка завершена");
            return html.Trim();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при минимальной очистке HTML");
            return html; // Возвращаем исходный HTML в случае ошибки
        }
    }

    /// <summary>
    /// Убирает потенциально опасные теги
    /// </summary>
    private string RemoveDangerousTags(string html)
    {
        var dangerousTags = new[] { "script", "style", "iframe", "object", "embed", "link", "meta" };
    
        foreach (var tag in dangerousTags)
        {
            // Удаляем парные теги
            html = Regex.Replace(html, 
                $@"<{tag}[^>]*>.*?</{tag}>", 
                "", 
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        
            // Удаляем самозакрывающиеся теги
            html = Regex.Replace(html, 
                $@"<{tag}[^>]*/>", 
                "", 
                RegexOptions.IgnoreCase);
        }
    
        return html;
    }

    /// <summary>
    /// Убирает атрибуты событий
    /// </summary>
    private string RemoveEventAttributes(string html)
    {
        var eventAttributes = new[] { "onclick", "onload", "onerror", "onmouseover", "onmouseout", 
            "onkeydown", "onkeyup", "onchange", "onsubmit" };
    
        foreach (var attr in eventAttributes)
        {
            html = Regex.Replace(html, $@"\s+{attr}\s*=\s*[""'][^""']*[""']", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, $@"\s+{attr}\s*=\s*[^>\s]*", "", RegexOptions.IgnoreCase);
        }
    
        return html;
    }

    /// <summary>
    /// Извлекает встроенное решение прямо из HTML страницы
    /// </summary>
    private async Task<bool> ExtractEmbeddedDecisionAsync(IPage page, CourtCase courtCase)
    {
        try
        {
            logger.LogInformation("Ищем встроенное решение в HTML для дела {CaseNumber}", courtCase.CaseNumber);

            // СПЕЦИАЛЬНАЯ ПРОВЕРКА: Ищем блоки MsoNormal с выравниванием по ширине
            bool hasMsoNormalStructure = await CheckMsoNormalStructure(page, courtCase);
            if (hasMsoNormalStructure)
            {
                logger.LogInformation("✅ Найдена структура MsoNormal для дела {CaseNumber}", courtCase.CaseNumber);
                return true;
            }

            // Стандартная проверка структуры решения
            bool hasStandardStructure = await CheckStandardDecisionStructure(page, courtCase);
            if (hasStandardStructure)
            {
                return true;
            }

            // Проверка по содержимому страницы
            bool hasDecisionContent = await CheckDecisionByContent(page, courtCase);
            if (hasDecisionContent)
            {
                return true;
            }

            // Пробуем новый метод с HTML
            bool hasHtmlDecision = await ExtractDecisionWithHtml(page, courtCase);
            if (hasHtmlDecision)
            {
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при поиске встроенного решения для дела {CaseNumber}", courtCase.CaseNumber);
            return false;
        }
    }

    private async Task<bool> ExtractCleanDecisionAsync(IPage page, CourtCase courtCase)
    {
        try
        {
            logger.LogInformation("Ищем чистое решение в HTML для дела {CaseNumber}", courtCase.CaseNumber);

            // 1. Сначала пробуем извлечь с HTML
            bool extracted = await ExtractDecisionWithHtml(page, courtCase);
            if (extracted) return true;
    
            // 2. Метод из HTML-структуры
            extracted = await ExtractFromHtmlStructure(page, courtCase);
            if (extracted) return true;
    
            // 3. Метод из стандартной структуры
            extracted = await ExtractFromStandardStructure(page, courtCase);
            if (extracted) return true;
    
            // 4. Запасные варианты (можно закомментировать если не нужны)
            // extracted = await ExtractBySimpleTextExtraction(page, courtCase);
            // if (extracted) return true;
    
            // extracted = await ExtractByDecisionKeywords(page, courtCase);
            // if (extracted) return true;
    
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при поиске чистого решения для дела {CaseNumber}", courtCase.CaseNumber);
            return false;
        }
    }
    /// <summary>
    /// СПЕЦИАЛЬНЫЙ МЕТОД: Извлекает решение из HTML с тегами форматирования
    /// </summary>
    /// <summary>
    /// Извлекает решение из HTML-структуры (сохраняем HTML)
    /// </summary>
    private async Task<bool> ExtractFromHtmlStructure(IPage page, CourtCase courtCase)
    {
        try
        {
            logger.LogInformation("Пытаемся извлечь решение из HTML-структуры...");

            // 1. Получаем весь HTML страницы
            var pageContent = await page.GetContentAsync();
    
            // 2. Ищем блок с решением
            int startIndex = FindDecisionStartInHtml(pageContent);
            if (startIndex == -1)
            {
                logger.LogInformation("Не найдено начало решения в HTML");
                return false;
            }

            // 3. Вырезаем HTML от начала решения
            string htmlSolution = pageContent.Substring(startIndex);
    
            // 4. Ищем конец решения
            int endIndex = FindDecisionEndInHtml(htmlSolution);
            if (endIndex == -1)
            {
                endIndex = Math.Min(50000, htmlSolution.Length);
            }
            else
            {
                endIndex = Math.Min(endIndex, 50000);
            }

            htmlSolution = htmlSolution.Substring(0, endIndex);
    
            // 5. Минимальная обработка HTML
            string cleanHtml = CleanHtmlForStorage(htmlSolution);
    
            if (string.IsNullOrWhiteSpace(cleanHtml) || cleanHtml.Length < 500)
            {
                logger.LogInformation("Извлеченный HTML слишком короткий: {Length}", cleanHtml.Length);
                return false;
            }

            // 6. Проверяем, что это действительно решение
            if (IsValidDecisionContent(cleanHtml))
            {
                var documentType = DetermineDocumentTypeFromContent(cleanHtml);
    
                courtCase.HasDecision = true;
                courtCase.DecisionLink = courtCase.Link + "#html_structure";
                courtCase.DecisionType = documentType;
                courtCase.DecisionContent = cleanHtml; // Сохраняем HTML!

                logger.LogInformation("✅ Найдено решение из HTML-структуры: {Type}, длина: {Length} символов", 
                    documentType, cleanHtml.Length);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении из HTML-структуры");
            return false;
        }
    }
    
    /// <summary>
    /// Извлекает текст из HTML с сохранением структуры (ГЛАВНЫЙ МЕТОД) - ИСПРАВЛЕННАЯ ВЕРСИЯ
    /// </summary>
    private string ExtractTextWithStructure(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        try
        {
            logger.LogDebug("Начинаем извлечение текста из HTML, длина: {Length} символов", html.Length);
        
            // ШАГ 1: Сначала обрабатываем защищенные данные - ДО ВСЕХ ОСТАЛЬНЫХ ПРЕОБРАЗОВАНИЙ
            html = ProcessProtectedData(html);
        
            // ШАГ 2: Убираем комментарии
            html = Regex.Replace(html, @"<!--.*?-->", "", RegexOptions.Singleline);
        
            // ШАГ 3: Заменяем теги на разметку с сохранением структуры (СОХРАНЯЕМ ЗАЩИЩЕННЫЕ МЕТКИ)
            html = ReplaceHtmlTagsWithStructure(html);
        
            // ШАГ 4: Очищаем от оставшихся HTML тегов (НО НЕ ТРОГАЕМ ЗАЩИЩЕННЫЕ МЕТКИ)
            html = CleanHtmlTagsPreservingMarkers(html);
        
            // ШАГ 5: Декодируем HTML-сущности
            html = System.Net.WebUtility.HtmlDecode(html);
        
            // ШАГ 6: Обрабатываем специальные символы
            html = ProcessSpecialCharacters(html);
        
            // ШАГ 7: Улучшаем форматирование текста
            html = ImproveTextFormatting(html);
        
            // ШАГ 8: Восстанавливаем защищенные метки после всех преобразований
            html = RestoreProtectedMarkers(html);
        
            // ШАГ 9: Убираем лишние пробелы и пустые строки
            html = CleanupWhitespace(html);
        
            logger.LogDebug("Извлечение текста завершено, результат: {Length} символов", html.Length);
            return html.Trim();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении текста с сохранением структуры");
            return string.Empty;
        }
    }

    /// <summary>
    /// Очищает HTML теги, но сохраняет защищенные метки
    /// </summary>
    private string CleanHtmlTagsPreservingMarkers(string html)
    {
        try
        {
            // Временные маркеры для защиты замен
            var protectedMarkers = new Dictionary<string, string>();
        
            // Заменяем защищенные данные на временные маркеры
            var protectedDataPattern = @"(<ФИО>\d*</ФИО>|<адрес>\d*</адрес>|<данные изъяты>\d*</данные изъяты>|<дата>\d*</дата>|<номер>\d*</номер>)";
            var matches = Regex.Matches(html, protectedDataPattern);
        
            for (int i = 0; i < matches.Count; i++)
            {
                var marker = $"###PROTECTED{i}###";
                protectedMarkers[marker] = matches[i].Value;
                html = html.Replace(matches[i].Value, marker);
            }
        
            // Теперь удаляем все HTML теги
            html = Regex.Replace(html, @"<[^>]*>", " ", RegexOptions.IgnoreCase);
        
            // Восстанавливаем защищенные данные
            foreach (var marker in protectedMarkers)
            {
                html = html.Replace(marker.Key, marker.Value);
            }
        
            return html;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при очистке HTML тегов с сохранением маркеров");
            return html;
        }
    }

    /// <summary>
    /// Восстанавливает защищенные метки после всех преобразований
    /// </summary>
    private string RestoreProtectedMarkers(string text)
    {
        try
        {
            // Обеспечиваем корректное форматирование защищенных меток
            text = text.Replace(" <ФИО>", " <ФИО>");
            text = text.Replace("</ФИО> ", "</ФИО> ");
            text = text.Replace(" <адрес>", " <адрес>");
            text = text.Replace("</адрес> ", "</адрес> ");
            text = text.Replace(" <дата>", " <дата>");
            text = text.Replace("</дата> ", "</дата> ");
            text = text.Replace(" <номер>", " <номер>");
            text = text.Replace("</номер> ", "</номер> ");
            text = text.Replace(" <данные изъяты>", " <данные изъяты>");
            text = text.Replace("</данные изъяты> ", "</данные изъяты> ");
        
            // Убираем лишние пробелы внутри меток
            text = Regex.Replace(text, @"<(\s*ФИО\s*)>", "<ФИО>");
            text = Regex.Replace(text, @"</\s*ФИО\s*>", "</ФИО>");
            text = Regex.Replace(text, @"<(\s*адрес\s*)>", "<адрес>");
            text = Regex.Replace(text, @"</\s*адрес\s*>", "</адрес>");
            text = Regex.Replace(text, @"<(\s*дата\s*)>", "<дата>");
            text = Regex.Replace(text, @"</\s*дата\s*>", "</дата>");
            text = Regex.Replace(text, @"<(\s*номер\s*)>", "<номер>");
            text = Regex.Replace(text, @"</\s*номер\s*>", "</номер>");
            text = Regex.Replace(text, @"<(\s*данные изъяты\s*)>", "<данные изъяты>");
            text = Regex.Replace(text, @"</\s*данные изъяты\s*>", "</данные изъяты>");
        
            return text;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при восстановлении защищенных маркеров");
            return text;
        }
    }

    /// <summary>
    /// Обрабатывает защищенные данные (ФИО, адреса и т.д.) - УЛУЧШЕННАЯ ВЕРСИЯ
    /// </summary>
    private string ProcessProtectedData(string html)
    {
        try
        {
            logger.LogDebug("Начинаем обработку защищенных данных");
        
            // ОБРАБОТКА ЗАЩИЩЕННЫХ СПАНОВ с порядковыми номерами
            var protectedPatterns = new Dictionary<string, string>
            {
                { @"<span[^>]*class\s*=\s*[""']?(FIO\d+)[""']?[^>]*>(.*?)</span>", "<ФИО>$2</ФИО>" },
                { @"<span[^>]*class\s*=\s*[""']?(Address\d+)[""']?[^>]*>(.*?)</span>", "<адрес>$2</адрес>" },
                { @"<span[^>]*class\s*=\s*[""']?(others\d+)[""']?[^>]*>(.*?)</span>", "<данные изъяты>$2</данные изъяты>" },
                { @"<span[^>]*class\s*=\s*[""']?(Data\d+)[""']?[^>]*>(.*?)</span>", "<дата>$2</дата>" },
                { @"<span[^>]*class\s*=\s*[""']?(Nomer\d+)[""']?[^>]*>(.*?)</span>", "<номер>$2</номер>" }
            };

            foreach (var pattern in protectedPatterns)
            {
                var matches = Regex.Matches(html, pattern.Key, 
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
            
                if (matches.Count > 0)
                {
                    logger.LogDebug("Найдено защищенных элементов типа {Type}: {Count}", 
                        pattern.Value, matches.Count);
                }
            
                foreach (Match match in matches)
                {
                    var innerText = match.Groups[2].Value.Trim();
                
                    // Создаем правильную замену
                    string replacement;
                    if (pattern.Value.Contains("ФИО"))
                    {
                        replacement = "<ФИО>" + CleanProtectedInnerText(innerText) + "</ФИО>";
                    }
                    else if (pattern.Value.Contains("адрес"))
                    {
                        replacement = "<адрес>" + CleanProtectedInnerText(innerText) + "</адрес>";
                    }
                    else if (pattern.Value.Contains("данные изъяты"))
                    {
                        replacement = "<данные изъяты>" + CleanProtectedInnerText(innerText) + "</данные изъяты>";
                    }
                    else if (pattern.Value.Contains("дата"))
                    {
                        // Для дат пытаемся распарсить
                        if (DateTime.TryParse(innerText, out var date))
                            replacement = "<дата>" + date.ToString("dd.MM.yyyy") + "</дата>";
                        else
                            replacement = "<дата>" + CleanProtectedInnerText(innerText) + "</дата>";
                    }
                    else if (pattern.Value.Contains("номер"))
                    {
                        replacement = "<номер>" + CleanProtectedInnerText(innerText) + "</номер>";
                    }
                    else
                    {
                        replacement = pattern.Value.Replace("$2", CleanProtectedInnerText(innerText));
                    }
                
                    html = html.Replace(match.Value, replacement);
                }
            }
        
            logger.LogDebug("Защищенные данные обработаны");
            return html;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при обработке защищенных данных");
            return html;
        }
    }

    /// <summary>
    /// Очищает внутренний текст защищенных элементов
    /// </summary>
    private string CleanProtectedInnerText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
    
        // Убираем HTML теги, но оставляем текст
        text = Regex.Replace(text, @"<[^>]*>", "", RegexOptions.IgnoreCase);
        // Декодируем HTML сущности
        text = System.Net.WebUtility.HtmlDecode(text);
        // Убираем лишние пробелы
        return text.Trim();
    }

    /// <summary>
    /// Заменяет HTML теги на текстовую разметку с сохранением структуры (УЛУЧШЕННАЯ ВЕРСИЯ)
    /// </summary>
    private string ReplaceHtmlTagsWithStructure(string html)
    {
        try
        {
            // СОХРАНЯЕМ ЗАЩИЩЕННЫЕ МЕТКИ
            var protectedMarkers = new Dictionary<string, string>();
            var protectedDataPattern = @"(<ФИО>.*?</ФИО>|<адрес>.*?</адрес>|<данные изъяты>.*?</данные изъяты>|<дата>.*?</дата>|<номер>.*?</номер>)";
            var matches = Regex.Matches(html, protectedDataPattern, RegexOptions.Singleline);
        
            for (int i = 0; i < matches.Count; i++)
            {
                var marker = $"###PROTECTED{i}###";
                protectedMarkers[marker] = matches[i].Value;
                html = html.Replace(matches[i].Value, marker);
            }
        
            // Сохраняем заголовки и важные элементы
            html = Regex.Replace(html, @"<(h[1-6])[^>]*>(.*?)</\1>", "\n\n### $2 ###\n\n", 
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        
            // Абзацы - двойной перенос
            html = Regex.Replace(html, @"<p[^>]*>", "\n\n", RegexOptions.IgnoreCase);
        
            // Разрывы строк - одиночный перенос
            html = Regex.Replace(html, @"<br[^>]*>", "\n", RegexOptions.IgnoreCase);
        
            // Div'ы - тоже могут создавать абзацы
            html = Regex.Replace(html, @"<div[^>]*>", "\n", RegexOptions.IgnoreCase);
        
            // Списки
            html = Regex.Replace(html, @"<li[^>]*>", "\n• ", RegexOptions.IgnoreCase);
        
            // Таблицы - упрощаем
            html = Regex.Replace(html, @"<tr[^>]*>", "\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<td[^>]*>", " | ", RegexOptions.IgnoreCase);
        
            // Форматирование текста
            html = Regex.Replace(html, @"<(b|strong)[^>]*>(.*?)</\1>", "**$2**", 
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<(i|em)[^>]*>(.*?)</\1>", "_$2_", 
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        
            // Убираем закрывающие теги
            html = Regex.Replace(html, @"</(p|div|span|li|ul|ol|table|tr|td)[^>]*>", "", 
                RegexOptions.IgnoreCase);
        
            // ВОССТАНАВЛИВАЕМ ЗАЩИЩЕННЫЕ МЕТКИ
            foreach (var marker in protectedMarkers)
            {
                html = html.Replace(marker.Key, marker.Value);
            }
        
            return html;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при замене HTML тегов");
            return html;
        }
    }
    
    /// <summary>
    /// Обрабатывает специальные символы HTML
    /// </summary>
    private string ProcessSpecialCharacters(string html)
    {
        html = html.Replace("&nbsp;", " ");
        html = html.Replace("&amp;", "&");
        html = html.Replace("&lt;", "<");
        html = html.Replace("&gt;", ">");
        html = html.Replace("&quot;", "\"");
        html = html.Replace("&laquo;", "«");
        html = html.Replace("&raquo;", "»");
        html = html.Replace("&ndash;", "–");
        html = html.Replace("&mdash;", "—");
        
        return html;
    }

    /// <summary>
    /// Улучшает форматирование текста
    /// </summary>
    private string ImproveTextFormatting(string html)
    {
        // Восстанавливаем абзацы после знаков препинания
        html = Regex.Replace(html, @"([.!?])\s+([А-ЯA-Z])", "$1\n\n$2");
        html = Regex.Replace(html, @"([.!?])\s+(\*\*[А-ЯA-Z])", "$1\n\n$2");
        
        // Убираем лишние пробелы вокруг знаков препинания
        html = Regex.Replace(html, @"\s+([.,;:!?])", "$1");
        html = Regex.Replace(html, @"([.,;:!?])\s+", "$1 ");
        
        // Восстанавливаем правильные переносы для дефисов
        html = Regex.Replace(html, @"\s+-\s+", " - ");
        html = Regex.Replace(html, @"\s*,\s*", ", ");
        
        return html;
    }

    /// <summary>
    /// Очищает от лишних пробелов и пустых строк
    /// </summary>
    private string CleanupWhitespace(string text)
    {
        // Убираем множественные пробелы внутри строк
        text = Regex.Replace(text, @"[ \t]+", " ");
        
        // Разделяем на строки
        var lines = text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        var resultLines = new List<string>();
        
        bool previousLineWasEmpty = false;
        
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                // Добавляем только одну пустую строку между абзацами
                if (!previousLineWasEmpty && resultLines.Count > 0)
                {
                    resultLines.Add("");
                    previousLineWasEmpty = true;
                }
            }
            else
            {
                resultLines.Add(trimmedLine);
                previousLineWasEmpty = false;
            }
        }
        
        // Убираем пустые строки в начале и конце
        while (resultLines.Count > 0 && string.IsNullOrWhiteSpace(resultLines[0]))
            resultLines.RemoveAt(0);
        
        while (resultLines.Count > 0 && string.IsNullOrWhiteSpace(resultLines[^1]))
            resultLines.RemoveAt(resultLines.Count - 1);
        
        return string.Join("\n", resultLines);
    }

    /// <summary>
    /// Метод для извлечения форматированного текста решения
    /// </summary>
    private async Task<bool> ExtractFormattedDecision(IPage page, CourtCase courtCase)
    {
        try
        {
            logger.LogInformation("Пытаемся извлечь форматированное решение...");

            // Получаем HTML всей страницы
            var pageContent = await page.GetContentAsync();
        
            // Ищем начало решения
            int startIndex = FindDecisionStartInHtml(pageContent);
            if (startIndex == -1)
            {
                return false;
            }
        
            // Вырезаем HTML от начала решения
            string htmlSolution = pageContent.Substring(startIndex);
        
            // Находим конец решения
            int endIndex = FindDecisionEndInHtml(htmlSolution);
            if (endIndex == -1)
            {
                endIndex = Math.Min(50000, htmlSolution.Length);
            }
        
            htmlSolution = htmlSolution.Substring(0, endIndex);
        
            // Извлекаем текст с форматированием
            string cleanText = ExtractTextWithStructure(htmlSolution);
        
            if (string.IsNullOrWhiteSpace(cleanText) || cleanText.Length < 500)
            {
                return false;
            }

            // Проверяем, что это действительно решение
            if (IsValidDecisionContent(cleanText))
            {
                var documentType = DetermineDocumentTypeFromContent(cleanText);

                courtCase.HasDecision = true;
                courtCase.DecisionLink = courtCase.Link + "#formatted_decision";
                courtCase.DecisionType = documentType;
                courtCase.DecisionContent = cleanText;

                logger.LogInformation("✅ Найдено форматированное решение: {Type}, длина: {Length}", 
                    documentType, cleanText.Length);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении форматированного решения");
            return false;
        }
    }

    private int FindDecisionStartInHtml(string html)
    {
        var startMarkers = new[]
        {
            "<p style=\"TEXT-ALIGN: center",
            "<p style=\"TEXT-ALIGN: center; TEXT-INDENT: 0.5in\">РЕШЕНИЕ</p>",
            "<p style=\"TEXT-ALIGN: center; TEXT-INDENT: 0.5in\">ИМЕНЕМ РОССИЙСКОЙ ФЕДЕРАЦИИ</p>",
            "Р Е Ш Е Н И Е",
            "РЕШЕНИЕ",
            "Именем Российской Федерации"
        };
    
        foreach (var marker in startMarkers)
        {
            int index = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                return Math.Max(0, index - 200);
            }
        }
    
        return -1;
    }

    private int FindDecisionEndInHtml(string html)
    {
        var endMarkers = new[]
        {
            "<table class=\"law-case-table\">",
            "</table>",
            "Председательствующий:",
            "Резолютивная часть решения оглашена",
            "Мотивированное решение составлено",
            "________________",
            "\nСудья "
        };
    
        foreach (var marker in endMarkers)
        {
            int index = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && index > 1000)
            {
                return index + marker.Length;
            }
        }
    
        return -1;
    }

    /// <summary>
    /// ПРОСТОЙ МЕТОД: Получает весь текст страницы и вырезает решение
    /// </summary>
    private async Task<bool> ExtractBySimpleTextExtraction(IPage page, CourtCase courtCase)
    {
        try
        {
            logger.LogInformation("Используем простой метод извлечения текста...");
        
            // Получаем весь текст страницы
            var fullText = await page.EvaluateFunctionAsync<string>(@"
            () => {
                const body = document.body;
                if (!body) return '';
                
                const clone = body.cloneNode(true);
                const scripts = clone.querySelectorAll('script, style, noscript, iframe');
                scripts.forEach(el => el.remove());
                
                return clone.innerText || clone.textContent || '';
            }
        ");
        
            if (string.IsNullOrWhiteSpace(fullText))
                return false;
        
            // Ищем начало решения
            var startMarkers = new[]
            {
                "Р Е Ш Е Н И Е",
                "РЕШЕНИЕ",
                "Именем Российской Федерации",
                "ИМЕНЕМ РОССИЙСКОЙ ФЕДЕРАЦИИ"
            };
        
            int startIndex = -1;
            foreach (var marker in startMarkers)
            {
                startIndex = fullText.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (startIndex >= 0) break;
            }
        
            if (startIndex < 0)
            {
                logger.LogInformation("Не найдены маркеры начала решения");
                return false;
            }
        
            // Ищем конец решения
            int endIndex = Math.Min(startIndex + 15000, fullText.Length);
        
            var endMarkers = new[]
            {
                "\nСудья ",
                "\nМировой судья ",
                "Председательствующий ",
                "________________",
                "Резолютивная часть"
            };
        
            foreach (var marker in endMarkers)
            {
                int markerIndex = fullText.IndexOf(marker, startIndex + 1000, StringComparison.OrdinalIgnoreCase);
                if (markerIndex > startIndex && markerIndex < endIndex)
                {
                    endIndex = markerIndex + 100;
                }
            }
        
            string decisionText = fullText.Substring(startIndex, endIndex - startIndex);
        
            // Очищаем текст (но сохраняем структуру)
            decisionText = CleanDecisionText(decisionText);
        
            if (string.IsNullOrWhiteSpace(decisionText) || decisionText.Length < 300)
            {
                logger.LogInformation("Текст решения слишком короткий: {Length}", decisionText.Length);
                return false;
            }
        
            if (IsValidDecisionContent(decisionText))
            {
                var documentType = DetermineDocumentTypeFromContent(decisionText);
        
                courtCase.HasDecision = true;
                courtCase.DecisionLink = courtCase.Link + "#simple_extraction";
                courtCase.DecisionType = documentType;
                courtCase.DecisionContent = decisionText;

                logger.LogInformation("✅ Найдено решение простым методом: {Type}, длина: {Length}", 
                    documentType, decisionText.Length);
                return true;
            }
        
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка в простом методе извлечения");
            return false;
        }
    }
    
    /// <summary>
    /// Извлекает решение по ключевым словам
    /// </summary>
    private async Task<bool> ExtractByDecisionKeywords(IPage page, CourtCase courtCase)
    {
        try
        {
            // Получаем весь текст страницы
            var body = await page.QuerySelectorAsync("body");
            if (body == null) return false;

            var fullText = await body.EvaluateFunctionAsync<string>("el => el.innerText");
            
            if (string.IsNullOrWhiteSpace(fullText))
                return false;

            // Ищем начало решения
            var startKeywords = new[]
            {
                "Именем Российской Федерации",
                "Р Е Ш Е Н И Е",
                "О П Р Е Д Е Л Е Н И Е",
                "П О С Т А Н О В Л Е Н И Е",
                "ИМЕНЕМ РОССИЙСКОЙ ФЕДЕРАЦИИ",
                "РЕШЕНИЕ",
                "ОПРЕДЕЛЕНИЕ",
                "ПОСТАНОВЛЕНИЕ"
            };

            int startIndex = -1;

            foreach (var keyword in startKeywords)
            {
                startIndex = fullText.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
                if (startIndex >= 0)
                {
                    logger.LogInformation("Найдено ключевое слово: {Keyword} на позиции {Position}", keyword, startIndex);
                    break;
                }
            }

            if (startIndex < 0)
            {
                logger.LogInformation("Не найдено ключевых слов для начала решения");
                return false;
            }

            // Вырезаем решение
            int endIndex = FindEndOfDecision(fullText, startIndex);
            
            string decisionText = endIndex > startIndex 
                ? fullText.Substring(startIndex, endIndex - startIndex)
                : fullText.Substring(startIndex);

            // Очищаем и проверяем
            decisionText = CleanDecisionText(decisionText);
            
            if (string.IsNullOrWhiteSpace(decisionText) || decisionText.Length < 200)
            {
                logger.LogInformation("Текст решения слишком короткий: {Length} символов", decisionText.Length);
                return false;
            }

            if (IsValidDecisionContent(decisionText))
            {
                var documentType = DetermineDocumentTypeFromContent(decisionText);
            
                courtCase.HasDecision = true;
                courtCase.DecisionLink = courtCase.Link + "#text_decision";
                courtCase.DecisionType = documentType;
                courtCase.DecisionContent = decisionText;

                logger.LogInformation("✅ Найдено решение по ключевым словам: {Type}, длина: {Length}", 
                    documentType, courtCase.DecisionContent.Length);
                return true;
            }

            logger.LogInformation("Текст не прошел валидацию как решение");
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении по ключевым словам");
            return false;
        }
    }
    
    /// <summary>
    /// Находит конец решения
    /// </summary>
    private int FindEndOfDecision(string text, int startIndex)
    {
        var endMarkers = new[]
        {
            "\nСудья\n",
            "\nСудья:\n",
            "\nПредседательствующий\n",
            "\nМировой судья\n",
            "\nСуд рассмотрел в открытом судебном заседании\n",
            "________________",
            "\nРезолютивная часть",
            "\nМотивированное решение",
            "\n\n\n"
        };

        int endIndex = text.Length;
        
        foreach (var marker in endMarkers)
        {
            int markerIndex = text.IndexOf(marker, startIndex + 100, StringComparison.OrdinalIgnoreCase);
            if (markerIndex > startIndex && markerIndex < endIndex)
            {
                endIndex = markerIndex;
                logger.LogInformation("Найден маркер конца: {Marker} на позиции {Position}", marker, markerIndex);
            }
        }

        // Если не нашли маркеров, берем следующие 5000 символов
        if (endIndex == text.Length)
        {
            endIndex = Math.Min(startIndex + 5000, text.Length);
        }

        return endIndex;
    }

    /// <summary>
    /// Очищает текст решения от лишнего форматирования (для простых методов)
    /// </summary>
    private string CleanDecisionText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // 1. Убираем HTML-теги
        text = Regex.Replace(text, @"<[^>]*>", " ");
        
        // 2. Декодируем HTML-сущности
        text = System.Net.WebUtility.HtmlDecode(text);
        
        // 3. Заменяем множественные пробелы и переносы
        text = Regex.Replace(text, @"[\r\n\t]+", "\n");
        text = Regex.Replace(text, @"\s+", " ");
        
        // 4. Убираем лишние пробелы вокруг знаков препинания
        text = Regex.Replace(text, @"\s+([.,;:!?])", "$1");
        text = Regex.Replace(text, @"([.,;:!?])\s+", "$1 ");
        
        // 5. Восстанавливаем переносы для абзацев
        text = Regex.Replace(text, @"\.\s+([А-ЯA-Z])", ".\n$1");
        text = Regex.Replace(text, @"!\s+([А-ЯA-Z])", "!\n$1");
        text = Regex.Replace(text, @"\?\s+([А-ЯA-Z])", "?\n$1");
        
        // 6. Убираем номерные и буллиты в начале строк
        text = Regex.Replace(text, @"^\s*[\d•\-*]\s*", "", RegexOptions.Multiline);
        
        // 7. Разделяем на строки и убираем пустые
        var lines = text.Split('\n');
        var resultLines = new List<string>();
        
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (!string.IsNullOrEmpty(trimmedLine) && trimmedLine.Length > 2)
            {
                resultLines.Add(trimmedLine);
            }
        }
        
        // 8. Объединяем с пустыми строками между абзацами
        text = string.Join("\n\n", resultLines);
        
        return text.Trim();
    }
    
    /// <summary>
    /// Извлекает решение из стандартной структуры
    /// </summary>
    private async Task<bool> ExtractFromStandardStructure(IPage page, CourtCase courtCase)
    {
        try
        {
            // Ищем заголовок "Р Е Ш Е Н И Е" или "О П Р Е Д Е Л Е Н И Е"
            var possibleHeaders = await page.QuerySelectorAllAsync(
                "p[style*='TEXT-ALIGN: center'], " +
                "p[style*='text-align: center'], " +
                "p.MsoNormal[style*='center'], " +
                "h3, h4, p"
            );

            int decisionStartIndex = -1;
            string decisionText = "";

            for (int i = 0; i < possibleHeaders.Length; i++)
            {
                var header = possibleHeaders[i];
                var headerText = await header.EvaluateFunctionAsync<string>("el => el.textContent?.trim()");
                
                if (!string.IsNullOrEmpty(headerText) && 
                    (headerText.ToUpper().Contains("Р Е Ш Е Н И Е") || 
                     headerText.ToUpper().Contains("О П Р Е Д Е Л Е Н И Е") ||
                     headerText.ToUpper().Contains("П О С Т А Н О В Л Е Н И Е") ||
                     headerText.ToUpper().Contains("РЕШЕНИЕ") ||
                     headerText.ToUpper().Contains("ОПРЕДЕЛЕНИЕ") ||
                     headerText.ToUpper().Contains("ПОСТАНОВЛЕНИЕ")))
                {
                    decisionStartIndex = i;
                    logger.LogInformation("Найден заголовок решения: {Header}", headerText);
                    break;
                }
            }

            if (decisionStartIndex >= 0)
            {
                // Собираем текст решения
                for (int i = decisionStartIndex; i < Math.Min(decisionStartIndex + 150, possibleHeaders.Length); i++)
                {
                    var element = possibleHeaders[i];
                    var text = await element.EvaluateFunctionAsync<string>("el => el.textContent?.trim()");
                    
                    if (!string.IsNullOrEmpty(text))
                    {
                        // Очищаем текст от лишних пробелов
                        text = CleanDecisionText(text);
                        decisionText += text + "\n\n";
                        
                        // Проверяем, не дошли ли мы до конца решения
                        if (text.Contains("Судья") || text.Contains("Председательствующий") || 
                            text.Contains("Мировой судья") || text.Length < 10)
                        {
                            break;
                        }
                    }
                }

                if (decisionText.Length < 500)
                {
                    return false;
                }

                if (IsValidDecisionContent(decisionText))
                {
                    var documentType = DetermineDocumentTypeFromContent(decisionText);
            
                    courtCase.HasDecision = true;
                    courtCase.DecisionLink = courtCase.Link + "#clean_decision";
                    courtCase.DecisionType = documentType;
                    courtCase.DecisionContent = decisionText;

                    logger.LogInformation("✅ Найдено чистое решение из структуры: {Type}, длина: {Length} символов", 
                        documentType, courtCase.DecisionContent.Length);
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении из стандартной структуры");
            return false;
        }
    }
    
    /// <summary>
    /// СПЕЦИАЛЬНАЯ ПРОВЕРКА: Ищем блоки MsoNormal с выравниванием по ширине
    /// </summary>
    /// <summary>
/// СПЕЦИАЛЬНАЯ ПРОВЕРКА: Ищем блоки MsoNormal с выравниванием по ширине
/// </summary>
private async Task<bool> CheckMsoNormalStructure(IPage page, CourtCase courtCase)
{
    try
    {
        // Ищем все параграфы с классом MsoNormal и выравниванием по ширине
        var msoNormalElements = await page.QuerySelectorAllAsync(
            "p.MsoNormal[style*='TEXT-ALIGN: justify'], " +
            "p.MsoNormal[style*='text-align: justify'], " +
            "p[class*='MsoNormal'][style*='justify']"
        );

        logger.LogInformation("Найдено элементов MsoNormal с выравниванием: {Count}", msoNormalElements.Length);

        if (msoNormalElements.Length < 5)
        {
            logger.LogInformation("Недостаточно элементов MsoNormal для признания решения: {Count}", msoNormalElements.Length);
            return false;
        }

        // Извлекаем HTML всех элементов
        var decisionHtmlParts = new List<string>();
        foreach (var element in msoNormalElements.Take(20))
        {
            var outerHtml = await element.EvaluateFunctionAsync<string>("el => el.outerHTML");
            if (!string.IsNullOrEmpty(outerHtml))
            {
                decisionHtmlParts.Add(outerHtml);
            }
        }

        if (decisionHtmlParts.Count < 3)
        {
            return false;
        }

        var fullHtml = string.Join("\n", decisionHtmlParts);
        var cleanHtml = CleanHtmlForStorage(fullHtml);
        
        if (!IsValidDecisionContent(cleanHtml))
        {
            logger.LogInformation("HTML из MsoNormal не прошел валидацию как судебное решение");
            return false;
        }

        var documentType = DetermineDocumentTypeFromContent(cleanHtml);
        if (string.IsNullOrEmpty(documentType))
        {
            return false;
        }

        courtCase.HasDecision = true;
        courtCase.DecisionLink = courtCase.Link + "#mso_decision";
        courtCase.DecisionType = documentType;
        courtCase.DecisionContent = cleanHtml; // Сохраняем HTML!

        logger.LogInformation("✅ Найдено валидное решение в MsoNormal структуре: {Type}", documentType);
        return true;
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Ошибка при проверке MsoNormal структуры");
        return false;
    }
}
    
    /// <summary>
    /// Проверяет стандартную структуру решения (h3 + blockquote)
    /// </summary>
    /// <summary>
/// Проверяет стандартную структуру решения (h3 + blockquote)
/// </summary>
private async Task<bool> CheckStandardDecisionStructure(IPage page, CourtCase courtCase)
{
    try
    {
        var pageContent = await page.GetContentAsync();
        
        bool hasSolutionStructure = 
            pageContent.Contains("<h3 class=\"text-center\">") && 
            pageContent.Contains("<blockquote itemprop=\"text\">");

        if (!hasSolutionStructure)
        {
            return false;
        }

        // Извлекаем заголовок
        var headerMatch = Regex.Match(
            pageContent, 
            @"<h3 class=""text-center"">([^<]*)</h3>"
        );
        
        if (!headerMatch.Success)
        {
            return false;
        }

        // Извлекаем текст решения из blockquote
        var blockquoteMatch = Regex.Match(
            pageContent,
            @"<blockquote itemprop=""text"">(.*?)</blockquote>",
            RegexOptions.Singleline
        );
        
        if (!blockquoteMatch.Success)
        {
            return false;
        }

        var decisionHtml = blockquoteMatch.Groups[1].Value;
        var cleanHtml = CleanHtmlForStorage(decisionHtml);
        
        if (!IsValidDecisionContent(cleanHtml))
        {
            return false;
        }

        var documentType = DetermineDocumentTypeFromContent(cleanHtml);
        if (string.IsNullOrEmpty(documentType))
        {
            return false;
        }

        courtCase.HasDecision = true;
        courtCase.DecisionLink = courtCase.Link + "#standard_structure";
        courtCase.DecisionType = documentType;
        courtCase.DecisionContent = cleanHtml; // Сохраняем HTML!
        
        logger.LogInformation("✅ Найдено решение в стандартной структуре: {Type}, длина: {Length}", 
            documentType, cleanHtml.Length);
        return true;
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Ошибка при проверке стандартной структуры");
        return false;
    }
}
     
    /// <summary>
    /// ВАЛИДАЦИЯ содержимого решения - УЛУЧШЕННАЯ ВЕРСИЯ
    /// </summary>
    private bool IsValidDecisionContent(string content)
{
    if (string.IsNullOrWhiteSpace(content))
        return false;

    var cleanContent = content.ToLower();

    // Проверяем, есть ли HTML теги (но не считаем опасные)
    bool hasHtmlTags = content.Contains("<p") || 
                       content.Contains("<div") || 
                       content.Contains("<br") ||
                       content.Contains("<span");

    // Если это HTML, проверяем только основные признаки
    if (hasHtmlTags)
    {
        var htmlIndicators = new[]
        {
            "именем российской федерации",
            "решил:",
            "решила:",
            "определил:",
            "определила:",
            "постановил:",
            "постановила:",
            "суд",
            "судья",
            "дело №"
        };
        
        int indicatorsCount = htmlIndicators.Count(indicator => cleanContent.Contains(indicator));
        bool isVal = indicatorsCount >= 3;
        
        logger.LogDebug("Валидация HTML контента: Индикаторов={IndicatorsCount}, Valid={IsValid}", 
            indicatorsCount, isVal);
        
        return isVal;
    }
    
    // Для чистого текста используем старую логику
    var requiredElements = new[]
    {
        "именем российской федерации",
        "решил:",
        "решила:",
        "определил:",
        "определила:",
        "постановил:",
        "постановила:",
        "установил:",
        "установила:"
    };

    var additionalElements = new[]
    {
        "суд",
        "судья",
        "рассмотрев",
        "заявление",
        "иск",
        "дело №",
        "председательствующий",
        "решение",
        "определение",
        "постановление",
        "удовлетворить",
        "отказать",
        "истец",
        "ответчик"
    };

    bool hasRequired = requiredElements.Any(element => cleanContent.Contains(element));
    int additionalCount = additionalElements.Count(element => cleanContent.Contains(element));

    bool isValid = hasRequired && additionalCount >= 3;

    logger.LogDebug("Валидация текстового контента: Required={HasRequired}, Additional={AdditionalCount}, Valid={IsValid}", 
        hasRequired, additionalCount, isValid);

    return isValid;
}

    /// <summary>
    /// Проверяет решение по содержимому всей страницы
    /// </summary>
    /// <summary>
/// Проверяет решение по содержимому всей страницы
/// </summary>
private async Task<bool> CheckDecisionByContent(IPage page, CourtCase courtCase)
{
    try
    {
        var pageContent = await page.GetContentAsync();
        
        // Ищем явные признаки решения в тексте
        var cleanContent = pageContent.ToLower();
        
        bool hasStrongIndicators = 
            cleanContent.Contains("р е ш е н и е") ||
            cleanContent.Contains("о п р е д е л е н и е") ||
            cleanContent.Contains("именем российской федерации");

        if (!hasStrongIndicators)
        {
            return false;
        }

        // Находим начало решения в HTML
        int startIndex = FindDecisionStartInHtml(pageContent);
        if (startIndex == -1)
        {
            return false;
        }

        // Вырезаем HTML решение
        string htmlSolution = pageContent.Substring(startIndex);
        int endIndex = FindDecisionEndInHtml(htmlSolution);
        
        if (endIndex == -1)
        {
            endIndex = Math.Min(50000, htmlSolution.Length);
        }

        htmlSolution = htmlSolution.Substring(0, endIndex);
        var cleanHtml = CleanHtmlForStorage(htmlSolution);
        
        if (!IsValidDecisionContent(cleanHtml))
        {
            return false;
        }

        var documentType = DetermineDocumentTypeFromContent(cleanHtml);
        if (string.IsNullOrEmpty(documentType))
        {
            return false;
        }

        courtCase.HasDecision = true;
        courtCase.DecisionLink = courtCase.Link + "#content_decision";
        courtCase.DecisionType = documentType;
        courtCase.DecisionContent = cleanHtml;
        
        logger.LogInformation("✅ Найдено решение по содержимому страницы: {Type}, длина: {Length}", 
            documentType, cleanHtml.Length);
        return true;
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Ошибка при проверке по содержимому страницы");
        return false;
    }
}
    /// <summary>
    /// Проверяет ссылки на файлы решений
    /// </summary>
    private async Task<bool> CheckFileDecisionLinks(IPage page, CourtCase courtCase)
    {
        try
        {
            logger.LogInformation("Ищем ссылки на файлы решений для дела {CaseNumber}", courtCase.CaseNumber);

            // Ищем блок с кнопками для скачивания
            var btnGroup = await page.QuerySelectorAsync(".btn-group1");
            if (btnGroup == null)
            {
                logger.LogInformation("Блок .btn-group1 не найден для дела {CaseNumber}", courtCase.CaseNumber);
                return false;
            }

            var decisionLinks = await btnGroup.QuerySelectorAllAsync("a");
            logger.LogInformation("Найдено ссылок в блоке решений: {Count}", decisionLinks.Length);
        
            foreach (var link in decisionLinks)
            {
                var href = await link.EvaluateFunctionAsync<string>("el => el.getAttribute('href')");
                var text = await link.EvaluateFunctionAsync<string>("el => el.textContent");
            
                if (!string.IsNullOrEmpty(href) && IsValidDecisionFileLink(href, text))
                {
                    var fullLink = href.StartsWith("/") 
                        ? "https://www.xn--90afdbaav0bd1afy6eub5d.xn--p1ai" + href 
                        : href;
                
                    courtCase.HasDecision = true;
                    courtCase.DecisionLink = fullLink;
                    courtCase.DecisionType = DetermineDocumentTypeFromLink(text);
                
                    logger.LogInformation("✅ Найдена ссылка на файл решения для дела {CaseNumber}: {Type}", 
                        courtCase.CaseNumber, courtCase.DecisionType);
                
                    return true;
                }
            }
        
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при проверке ссылок на файлы решений для дела {CaseNumber}", courtCase.CaseNumber);
            return false;
        }
    }
  
    /// <summary>
    /// Строгая проверка валидности ссылки на файл решения
    /// </summary>
    private bool IsValidDecisionFileLink(string href, string linkText)
    {
        if (string.IsNullOrEmpty(href)) 
            return false;
    
        var hasValidExtension = href.EndsWith(".doc") || 
                                href.EndsWith(".docx") || 
                                href.EndsWith(".pdf") ||
                                href.EndsWith(".rtf");

        var hasValidPath = href.Contains("/decisions/");

        var cleanText = linkText.ToLower();
        var hasValidText = cleanText.Contains("решение") ||
                           cleanText.Contains("определение") ||
                           cleanText.Contains("постановление") ||
                           cleanText.Contains("приказ") ||
                           cleanText.Contains("мотивированное");

        return hasValidExtension && hasValidPath && hasValidText;
    }
    
    /// <summary>
    /// Определяет тип документа из текста ссылки
    /// </summary>
    private string DetermineDocumentTypeFromLink(string linkText)
    {
        if (string.IsNullOrEmpty(linkText)) 
            return "Документ";
    
        var text = linkText.ToLower();
    
        if (text.Contains("мотивированное решение")) return "Мотивированное решение";
        if (text.Contains("решение")) return "Решение";
        if (text.Contains("определение")) return "Определение";
        if (text.Contains("постановление")) return "Постановление";
        if (text.Contains("приказ")) return "Судебный приказ";
        return "Документ";
    }
    
    /// <summary>
    /// Определяет тип документа из содержимого - УЛУЧШЕННАЯ ВЕРСИЯ
    /// </summary>
    private string DetermineDocumentTypeFromContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null!;

        var cleanContent = content.ToLower();

        if (cleanContent.Contains("р е ш е н и е") || 
            (cleanContent.Contains("решение") && cleanContent.Contains("именем российской федерации")))
            return "Решение";
        if (cleanContent.Contains("о п р е д е л е н и е") || 
            (cleanContent.Contains("определение") && cleanContent.Contains("именем российской федерации")))
            return "Определение";
        if (cleanContent.Contains("п о с т а н о в л е н и е") || 
            (cleanContent.Contains("постановление") && cleanContent.Contains("именем российской федерации")))
            return "Постановление";
        if (cleanContent.Contains("приказ") && cleanContent.Contains("именем российской федерации"))
            return "Судебный приказ";
        if (cleanContent.Contains("мотивированное решение"))
            return "Мотивированное решение";
        
        if (cleanContent.Contains("решил:") || cleanContent.Contains("решила:"))
            return "Решение"; 
        if (cleanContent.Contains("определил:") || cleanContent.Contains("определила:"))
            return "Определение";
        if (cleanContent.Contains("постановил:") || cleanContent.Contains("постановила:"))
            return "Постановление";
        if (cleanContent.Contains("именем российской федерации"))
            return "Судебный акт";
        return null!;
    }
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    [Obsolete("Obsolete")]
    private async Task ExtractDetailedCaseInfo(IPage page, CourtCase courtCase)
    {
        try
        {
            logger.LogInformation("Извлекаем детальную информацию для дела: {CaseNumber}", courtCase.CaseNumber);

            // 1. Извлекаем информацию из заголовка (номер дела, дата начала, суд)
            await ExtractHeaderInfo(page, courtCase);

            // 2. Извлекаем информацию о сторонах (истец, ответчик, третьи лица, представители)
            await ExtractPartiesInfo(page, courtCase);

            // 3. Извлекаем информацию о движении дела
            await ExtractCaseMovementInfo(page, courtCase);

            // 4. Извлекаем результат дела
            await ExtractCaseResultInfo(page, courtCase);

            logger.LogInformation("✅ Детальная информация извлечена для дела {CaseNumber}", courtCase.CaseNumber);
            logger.LogInformation("📋 Итог по сторонам: Истцы={Plaintiffs}, Ответчики={Defendants}, Третьи лица={ThirdParties}, Представители={Representatives}", 
                courtCase.Plaintiff, courtCase.Defendant, courtCase.ThirdParties, courtCase.Representatives);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении детальной информации для дела {CaseNumber}", courtCase.CaseNumber);
        }
    }
    private async Task ExtractHeaderInfo(IPage page, CourtCase courtCase)
    {
        try
        {
            logger.LogInformation("Извлекаем информацию из заголовка дела...");

            // Ищем блок с основной информацией
            var headerBlock = await page.QuerySelectorAsync(".col-md-8.text-right");
            if (headerBlock == null)
            {
                logger.LogWarning("Блок заголовка не найден для дела {CaseNumber}", courtCase.CaseNumber);
                return;
            }

            // Получаем весь текст блока
            var headerText = await headerBlock.EvaluateFunctionAsync<string>("el => el.textContent");
            logger.LogInformation("Текст заголовка: {HeaderText}", headerText);

            // Используем регулярные выражения для извлечения данных
            await ExtractDataWithRegex(headerText, courtCase);
        
            // Альтернативный метод: парсим каждый параграф
            await ExtractDataFromParagraphs(headerBlock, courtCase);

            logger.LogInformation("✅ Информация заголовка извлечена: Суд={Court}, Судья={Judge}, Начало={StartDate}", 
                courtCase.CourtType, courtCase.JudgeName, courtCase.StartDate.ToString("dd.MM.yyyy"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении информации заголовка для дела {CaseNumber}", courtCase.CaseNumber);
        }
    }

    /// <summary>
    /// Извлекает данные с помощью регулярных выражений
    /// </summary>
    private async Task ExtractDataWithRegex(string headerText, CourtCase courtCase)
    {
        try
        {
            // 1. Номер дела
            var caseNumberMatch = Regex.Match(headerText, @"Номер дела:\s*([^\s]+)", RegexOptions.IgnoreCase);
            if (caseNumberMatch.Success)
            {
                var caseNumber = caseNumberMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(caseNumber))
                {
                    courtCase.CaseNumber = caseNumber;
                    logger.LogInformation("Найден номер дела: {CaseNumber}", caseNumber);
                }
            }

            // 2. Дата начала дела (StartDate) - сохраняем как было
            var startDateMatch = Regex.Match(headerText, @"Дата начала:\s*(\d{1,2}\.\d{1,2}\.\d{4})", RegexOptions.IgnoreCase);
            if (startDateMatch.Success)
            {
                var startDateStr = startDateMatch.Groups[1].Value.Trim();
                if (DateTime.TryParse(startDateStr, out var startDate))
                {
                    courtCase.StartDate = startDate;
                    logger.LogInformation("✅ Найдена дата начала дела: {StartDate}", startDate.ToString("dd.MM.yyyy"));
                }
                else
                {
                    logger.LogWarning("Не удалось распарсить дату начала: {StartDateStr}", startDateStr);
                }
            }

            // 3. ВАЖНО: Дата рассмотрения (DecisionDate) - это поле ReceivedDate!
            var decisionDateMatch = Regex.Match(headerText, @"Дата рассмотрения:\s*(\d{1,2}\.\d{1,2}\.\d{4})", RegexOptions.IgnoreCase);
            if (decisionDateMatch.Success)
            {
                var decisionDateStr = decisionDateMatch.Groups[1].Value.Trim();
                if (DateTime.TryParse(decisionDateStr, out var decisionDate))
                {
                    // ВАЖНО: Сохраняем в ReceivedDate, а не в DecisionDate!
                    courtCase.ReceivedDate = decisionDate;
                    logger.LogInformation("✅ Найдена дата рассмотрения (ReceivedDate): {DecisionDate}", decisionDate.ToString("dd.MM.yyyy"));
                }
                else
                {
                    logger.LogWarning("Не удалось распарсить дату рассмотрения: {DecisionDateStr}", decisionDateStr);
                }
            }
            else
            {
                logger.LogInformation("Дата рассмотрения не найдена в заголовке");
                courtCase.ReceivedDate = null;
            }

            // 4. Суд
            var courtMatch = Regex.Match(headerText, @"Суд:\s*([^\n]+)", RegexOptions.IgnoreCase);
            if (courtMatch.Success)
            {
                var courtName = courtMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(courtName))
                {
                    courtCase.CourtType = courtName;
                    logger.LogInformation("Найден суд: {CourtName}", courtName);
                }
            }

            // 5. Судья
            var judgeMatch = Regex.Match(headerText, @"Судья:\s*([^\n]+)", RegexOptions.IgnoreCase);
            if (judgeMatch.Success)
            {
                var judgeName = judgeMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(judgeName))
                {
                    courtCase.JudgeName = judgeName;
                    logger.LogInformation("✅ Найден судья: {JudgeName}", judgeName);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении данных через регулярные выражения");
        }
    }

    /// <summary>
    /// Альтернативный метод: извлекает данные из отдельных параграфов
    /// </summary>
    private async Task ExtractDataFromParagraphs(IElementHandle headerBlock, CourtCase courtCase)
    {
        try
        {
            // Получаем все параграфы внутри блока
            var paragraphs = await headerBlock.QuerySelectorAllAsync("p");
            logger.LogInformation("Найдено параграфов в заголовке: {Count}", paragraphs.Length);

            foreach (var paragraph in paragraphs)
            {
                var text = await paragraph.EvaluateFunctionAsync<string>("el => el.textContent?.trim()");
                if (string.IsNullOrEmpty(text))
                    continue;

                logger.LogDebug("Обрабатываем параграф: {Text}", text);

                // Проверяем каждый параграф на наличие нужной информации
                if (text.StartsWith("Номер дела:", StringComparison.OrdinalIgnoreCase))
                {
                    await ExtractCaseNumberFromParagraph(paragraph, courtCase);
                }
                else if (text.StartsWith("Дата начала:", StringComparison.OrdinalIgnoreCase))
                {
                    await ExtractStartDateFromParagraph(paragraph, courtCase);
                }
                
                else if (text.StartsWith("Суд:", StringComparison.OrdinalIgnoreCase))
                {
                    await ExtractCourtFromParagraph(paragraph, courtCase);
                }
                else if (text.StartsWith("Судья:", StringComparison.OrdinalIgnoreCase))
                {
                    await ExtractJudgeFromParagraph(paragraph, courtCase);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении данных из параграфов");
        }
    }

    /// <summary>
    /// Извлекает номер дела из параграфа
    /// </summary>
    private async Task ExtractCaseNumberFromParagraph(IElementHandle paragraph, CourtCase courtCase)
    {
        try
        {
            // Ищем тег <b> внутри параграфа
            var boldElement = await paragraph.QuerySelectorAsync("b");
            if (boldElement != null)
            {
                var caseNumber = await boldElement.EvaluateFunctionAsync<string>("el => el.textContent?.trim()");
                if (!string.IsNullOrEmpty(caseNumber))
                {
                    courtCase.CaseNumber = caseNumber;
                    logger.LogInformation("Извлечен номер дела из тега <b>: {CaseNumber}", caseNumber);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Ошибка при извлечении номера дела из параграфа");
        }
    }

    /// <summary>
    /// Извлекает дату начала из параграфа
    /// </summary>
    private async Task ExtractStartDateFromParagraph(IElementHandle paragraph, CourtCase courtCase)
    {
        try
        {
            var boldElement = await paragraph.QuerySelectorAsync("b");
            if (boldElement != null)
            {
                var dateStr = await boldElement.EvaluateFunctionAsync<string>("el => el.textContent?.trim()");
                if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var date))
                {
                    courtCase.StartDate = date;
                    logger.LogInformation("✅ Извлечена дата начала из тега <b>: {Date}", date.ToString("dd.MM.yyyy"));
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Ошибка при извлечении даты начала из параграфа");
        }
    }
    

    /// <summary>
    /// Извлекает суд из параграфа
    /// </summary>
    private async Task ExtractCourtFromParagraph(IElementHandle paragraph, CourtCase courtCase)
    {
        try
        {
            var boldElement = await paragraph.QuerySelectorAsync("b");
            if (boldElement != null)
            {
                var courtName = await boldElement.EvaluateFunctionAsync<string>("el => el.textContent?.trim()");
                if (!string.IsNullOrEmpty(courtName))
                {
                    courtCase.CourtType = courtName;
                    logger.LogInformation("Извлечен суд из тега <b>: {CourtName}", courtName);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Ошибка при извлечении суда из параграфа");
        }
    }

    /// <summary>
    /// Извлекает судью из параграфа
    /// </summary>
    private async Task ExtractJudgeFromParagraph(IElementHandle paragraph, CourtCase courtCase)
    {
        try
        {
            var boldElement = await paragraph.QuerySelectorAsync("b");
            if (boldElement != null)
            {
                var judgeName = await boldElement.EvaluateFunctionAsync<string>("el => el.textContent?.trim()");
                if (!string.IsNullOrEmpty(judgeName))
                {
                    courtCase.JudgeName = judgeName;
                    logger.LogInformation("✅ Извлечен судья из тега <b>: {JudgeName}", judgeName);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Ошибка при извлечении судьи из параграфа");
        }
    }
    
    /// <summary>
    /// Извлекает результат дела из блока dl-horizontal
    /// </summary>
    private async Task ExtractCaseResultInfo(IPage page, CourtCase courtCase)
    {
        try
        {
            logger.LogInformation("Ищем результат дела для дела {CaseNumber}", courtCase.CaseNumber);

            // Ищем блок с классом dl-horizontal
            var dlHorizontal = await page.QuerySelectorAsync("dl.dl-horizontal");
            if (dlHorizontal == null)
            {
                logger.LogInformation("Блок dl-horizontal не найден для дела {CaseNumber}", courtCase.CaseNumber);
                // Оставляем поле пустым, а не "Не указан"
                courtCase.CaseResult = string.Empty;
                return;
            }

            // Ищем все элементы dt и dd внутри блока
            var dtElements = await dlHorizontal.QuerySelectorAllAsync("dt");
            var ddElements = await dlHorizontal.QuerySelectorAllAsync("dd");

            // Создаем словарь для пар ключ-значение
            var caseInfo = new Dictionary<string, string>();

            for (int i = 0; i < Math.Min(dtElements.Length, ddElements.Length); i++)
            {
                try
                {
                    var key = await dtElements[i].EvaluateFunctionAsync<string>("el => el.textContent?.trim()");
                    var value = await ddElements[i].EvaluateFunctionAsync<string>("el => el.textContent?.trim()");

                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                    {
                        caseInfo[key] = value;
                        logger.LogDebug("Найдена информация о деле: {Key} = {Value}", key, value);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Ошибка при извлечении пары dt-dd");
                }
            }

            // Извлекаем результат дела
            if (caseInfo.ContainsKey("Результат"))
            {
                courtCase.CaseResult = caseInfo["Результат"];
                logger.LogInformation("✅ Найден результат дела: {Result}", courtCase.CaseResult);
            }
            else if (caseInfo.ContainsKey("Итог"))
            {
                courtCase.CaseResult = caseInfo["Итог"];
                logger.LogInformation("✅ Найден итог дела: {Result}", courtCase.CaseResult);
            }
            else
            {
                // Оставляем поле пустым, если нет результата
                courtCase.CaseResult = string.Empty;
                logger.LogInformation("❌ Результат дела не найден в блоке dl-horizontal");
            }

            // Дополнительно: обновляем категорию и подкатегорию если они есть
            if (caseInfo.ContainsKey("Категория"))
            {
                await UpdateCategoryFromDlHorizontal(caseInfo["Категория"], courtCase);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении результата дела {CaseNumber}", courtCase.CaseNumber);
            courtCase.CaseResult = string.Empty;
        }
    }

    /// <summary>
    /// Извлекает ссылку на оригинальный сайт суда
    /// </summary>
    private async Task ExtractOriginalCaseLinkAsync(IPage page, CourtCase courtCase)
    {
        try
        {
            logger.LogInformation("Ищем ссылку на оригинальный сайт суда для дела {CaseNumber}", courtCase.CaseNumber);

            // 1. Ищем кнопку для показа ссылки
            var showLinkButton = await page.QuerySelectorAsync("#show-original-link");
            if (showLinkButton == null)
            {
                logger.LogInformation("Кнопка показа оригинальной ссылки не найдена для дела {CaseNumber}", courtCase.CaseNumber);
                return;
            }

            // 2. Кликаем на кнопку чтобы показать ссылку
            await showLinkButton.ClickAsync();
            await Task.Delay(2000); // Ждем появления ссылки

            // 3. Ищем появившуюся ссылку
            var originalLinkContainer = await page.QuerySelectorAsync("#original-link");
            if (originalLinkContainer != null)
            {
                var linkElement = await originalLinkContainer.QuerySelectorAsync("a[target='_blank']");
                if (linkElement != null)
                {
                    var originalLink = await linkElement.EvaluateFunctionAsync<string>("el => el.getAttribute('href')");
                    if (!string.IsNullOrEmpty(originalLink))
                    {
                        courtCase.OriginalCaseLink = originalLink;
                        logger.LogInformation("✅ Найдена оригинальная ссылка для дела {CaseNumber}: {Link}", 
                            courtCase.CaseNumber, originalLink);
                        return;
                    }
                }
            }

            logger.LogInformation("Оригинальная ссылка не найдена после нажатия кнопки для дела {CaseNumber}", courtCase.CaseNumber);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении оригинальной ссылки для дела {CaseNumber}", courtCase.CaseNumber);
        }
    }

    [Obsolete("Obsolete")]
    private async Task ExtractPartiesInfo(IPage page, CourtCase courtCase)
    {
        try
        {
            logger.LogInformation("Начинаем извлечение информации о сторонах...");

            // Стратегия 1: Обычный поиск по таблицам
            var tables = await page.QuerySelectorAllAsync("table.table-condensed");
            bool foundWithFirstMethod = false;

            foreach (var table in tables)
            {
                var tableText = await table.EvaluateFunctionAsync<string>("el => el.textContent");
            
                if (tableText.Contains("Стороны по делу") || 
                    tableText.Contains("ИСТЕЦ") || 
                    tableText.Contains("ОТВЕТЧИК"))
                {
                    logger.LogInformation("Найдена таблица сторон, используем первый метод");
                    await ExtractPartiesFromTable(table, courtCase);
                    foundWithFirstMethod = true;
                    break;
                }
            }

            // Стратегия 2: Если первый метод не нашел представителей, используем XPath
            if (foundWithFirstMethod && string.IsNullOrEmpty(courtCase.Representatives))
            {
                logger.LogInformation("Первый метод не нашел представителей, пробуем XPath...");
                await ExtractPartiesWithXPath(page, courtCase);
            }
            else if (!foundWithFirstMethod)
            {
                // Если вообще не нашли таблицу, пробуем XPath
                logger.LogInformation("Таблица сторон не найдена, используем XPath...");
                await ExtractPartiesWithXPath(page, courtCase);
            }

            // Финальная проверка
            if (string.IsNullOrEmpty(courtCase.Plaintiff) && 
                string.IsNullOrEmpty(courtCase.Defendant) &&
                string.IsNullOrEmpty(courtCase.Representatives))
            {
                logger.LogWarning("❌ Не удалось извлечь ни одной стороны дела");
            }
            else
            {
                logger.LogInformation("✅ Извлечение сторон завершено успешно");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при поиске таблицы сторон");
        }
    }
    
    /// <summary>
    /// Альтернативный метод извлечения сторон с использованием XPath (более надежный)
    /// </summary>
    [Obsolete("Obsolete")]
    private async Task ExtractPartiesWithXPath(IPage page, CourtCase courtCase)
    {
        try
        {
            logger.LogInformation("Используем XPath для извлечения сторон...");

            // Ищем все строки с данными сторон (игнорируем заголовки)
            var partyRows = await page.XPathAsync("//table[contains(@class, 'table-condensed')]//tr[td[2][@itemprop='contributor']]");
    
            logger.LogInformation("Найдено строк с участниками: {Count}", partyRows.Length);

            var plaintiffs = new List<string>();
            var defendants = new List<string>();
            var thirdParties = new List<string>();
            var representatives = new List<string>();

            foreach (var row in partyRows)
            {
                // Получаем тип участника из первой ячейки
                var typeCell = await row.QuerySelectorAsync("td:nth-child(1)");
                var nameCell = await row.QuerySelectorAsync("td:nth-child(2)");

                if (typeCell != null && nameCell != null)
                {
                    var partyType = await typeCell.EvaluateFunctionAsync<string>("el => el.textContent?.trim()");
                    var partyName = await nameCell.EvaluateFunctionAsync<string>("el => el.textContent?.trim()");

                    if (!string.IsNullOrEmpty(partyType) && !string.IsNullOrEmpty(partyName))
                    {
                        var cleanType = partyType.ToUpper();
                        var cleanName = partyName;

                        logger.LogDebug("XPath: Тип='{Type}', Имя='{Name}'", cleanType, cleanName);

                        switch (cleanType)
                        {
                            case "ИСТЕЦ":
                                plaintiffs.Add(cleanName);
                                break;
                            case "ОТВЕТЧИК":
                                defendants.Add(cleanName);
                                break;
                            case "ТРЕТЬЕ ЛИЦО":
                                thirdParties.Add(cleanName);
                                break;
                            case "ПРЕДСТАВИТЕЛЬ":
                                representatives.Add(cleanName);
                                break;
                            default:
                                // Дополнительная проверка для частичных совпадений
                                if (cleanType.Contains("ПРЕДСТАВИТЕЛЬ"))
                                {
                                    representatives.Add(cleanName);
                                    logger.LogDebug("✅ Найден представитель (частичное совпадение): {Name}", cleanName);
                                }
                                else
                                {
                                    logger.LogWarning("XPath: Неизвестный тип '{Type}' для '{Name}'", cleanType, cleanName);
                                }
                                break;
                        }
                    }
                }
            }

            // Записываем результаты
            courtCase.Plaintiff = string.Join("; ", plaintiffs);
            courtCase.Defendant = string.Join("; ", defendants);
            courtCase.ThirdParties = string.Join("; ", thirdParties);
            courtCase.Representatives = string.Join("; ", representatives);

            logger.LogInformation("✅ XPath извлечение завершено");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при XPath извлечении сторон");
        }
    }
    private async Task ExtractPartiesFromTable(IElementHandle table, CourtCase courtCase)
    {
        try
        {
            var rows = await table.QuerySelectorAllAsync("tr");
            var plaintiffs = new List<string>();
            var defendants = new List<string>();
            var thirdParties = new List<string>();
            var representatives = new List<string>();

            logger.LogInformation("Обрабатываем таблицу сторон: найдено {RowCount} строк", rows.Length);

            foreach (var row in rows)
            {
                var cells = await row.QuerySelectorAllAsync("td");
                if (cells.Length >= 2)
                {
                    var partyType = await cells[0].EvaluateFunctionAsync<string>("el => el.textContent");
                    var partyName = await cells[1].EvaluateFunctionAsync<string>("el => el.textContent");

                    if (!string.IsNullOrEmpty(partyType) && !string.IsNullOrEmpty(partyName))
                    {
                        var cleanType = partyType.Trim().ToUpper();
                        var cleanName = partyName.Trim();

                        logger.LogDebug("Обрабатываем строку: Тип='{Type}', Имя='{Name}'", cleanType, cleanName);

                        // Улучшенная проверка по тексту - точное соответствие
                        if (cleanType == "ИСТЕЦ" || cleanType.StartsWith("ИСТЕЦ"))
                        {
                            plaintiffs.Add(cleanName);
                            logger.LogDebug("✅ Найден истец: {Name}", cleanName);
                        }
                        else if (cleanType == "ОТВЕТЧИК" || cleanType.StartsWith("ОТВЕТЧИК"))
                        {
                            defendants.Add(cleanName);
                            logger.LogDebug("✅ Найден ответчик: {Name}", cleanName);
                        }
                        else if (cleanType.Contains("ТРЕТЬЕ ЛИЦО") || cleanType == "ТРЕТЬЕ ЛИЦО" || cleanType.StartsWith("ТРЕТЬЕ"))
                        {
                            thirdParties.Add(cleanName);
                            logger.LogDebug("✅ Найдено третье лицо: {Name}", cleanName);
                        }
                        else if (cleanType == "ПРЕДСТАВИТЕЛЬ" || cleanType.StartsWith("ПРЕДСТАВИТЕЛЬ"))
                        {
                            representatives.Add(cleanName);
                            logger.LogDebug("✅ Найден представитель: {Name}", cleanName);
                        }
                        else
                        {
                            logger.LogWarning("❌ Неизвестный тип стороны: '{Type}' - '{Name}'", cleanType, cleanName);
                        }
                    }
                    else
                    {
                        logger.LogDebug("Пустые данные в строке: Type='{Type}', Name='{Name}'", 
                            partyType, partyName);
                    }
                }
                else
                {
                    // Это может быть заголовочная строка
                    var rowText = await row.EvaluateFunctionAsync<string>("el => el.textContent");
                    if (!string.IsNullOrEmpty(rowText) && rowText.Contains("Стороны по делу"))
                    {
                        logger.LogDebug("Пропускаем заголовочную строку: {Text}", rowText.Trim());
                    }
                }
            }

            // Собираем всех через точку с запятой
            courtCase.Plaintiff = plaintiffs.Any() ? string.Join("; ", plaintiffs) : "";
            courtCase.Defendant = defendants.Any() ? string.Join("; ", defendants) : "";
            courtCase.ThirdParties = thirdParties.Any() ? string.Join("; ", thirdParties) : "";
            courtCase.Representatives = representatives.Any() ? string.Join("; ", representatives) : "";

            logger.LogInformation("✅ Стороны извлечены: Истцов={PlaintiffsCount}, Ответчиков={DefendantsCount}, Третьих лиц={ThirdPartiesCount}, Представителей={RepresentativesCount}", 
                plaintiffs.Count, defendants.Count, thirdParties.Count, representatives.Count);
    
            // Детальный лог результатов
            if (plaintiffs.Any()) logger.LogInformation("📋 Истцы: {Plaintiffs}", string.Join("; ", plaintiffs));
            if (defendants.Any()) logger.LogInformation("📋 Ответчики: {Defendants}", string.Join("; ", defendants));
            if (thirdParties.Any()) logger.LogInformation("📋 Третьи лица: {ThirdParties}", string.Join("; ", thirdParties));
            if (representatives.Any()) logger.LogInformation("📋 Представители: {Representatives}", string.Join("; ", representatives));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении сторон из таблицы");
        }
    }
    /// <summary>
    /// Извлекает информацию о движении дела из таблицы
    /// </summary>
    private async Task ExtractCaseMovementInfo(IPage page, CourtCase courtCase)
    {
        try
        {
            logger.LogInformation("Ищем таблицу движения дела для дела {CaseNumber}", courtCase.CaseNumber);

            // Ищем таблицу с движением дела
            var movementTables = await page.QuerySelectorAllAsync("table.table-condensed");
        
            foreach (var table in movementTables)
            {
                var tableText = await table.EvaluateFunctionAsync<string>("el => el.textContent");
                if (tableText.Contains("Движение дела") || tableText.Contains("Наименование события"))
                {
                    logger.LogInformation("✅ Найдена таблица движения дела");
                    await ExtractMovementDetailsFromTable(table, courtCase);
                    return;
                }
            }

            logger.LogInformation("❌ Таблица движения дела не найдена");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении информации о движении дела {CaseNumber}", courtCase.CaseNumber);
        }
    }

    /// <summary>
    /// Извлекает детальную информацию о движении дела из таблицы
    /// </summary>
    private async Task ExtractMovementDetailsFromTable(IElementHandle table, CourtCase courtCase)
    {
        try
        {
            var rows = await table.QuerySelectorAllAsync("tr");
            var movements = new List<CourtCaseMovement>();

            logger.LogInformation("Обрабатываем таблицу движения дела: найдено {RowCount} строк", rows.Length);

            foreach (var row in rows)
            {
                try
                {
                    // Пропускаем заголовочные строки
                    var isHeader = await row.EvaluateFunctionAsync<bool>("el => el.classList.contains('active')");
                    if (isHeader) continue;

                    var cells = await row.QuerySelectorAllAsync("td");
                
                    // Должно быть 4 ячейки: Наименование, Результат, Основания, Дата
                    if (cells.Length >= 4)
                    {
                        var eventName = await cells[0].EvaluateFunctionAsync<string>("el => el.textContent?.trim()");
                        var eventResult = await cells[1].EvaluateFunctionAsync<string>("el => el.textContent?.trim()");
                        var basis = await cells[2].EvaluateFunctionAsync<string>("el => el.textContent?.trim()");
                        var eventDateStr = await cells[3].EvaluateFunctionAsync<string>("el => el.textContent?.trim()");

                        // Парсим дату
                        DateTime? eventDate = null;
                        if (!string.IsNullOrEmpty(eventDateStr) && DateTime.TryParse(eventDateStr, out var parsedDate))
                        {
                            eventDate = parsedDate;
                        }

                        // Создаем объект движения дела
                        var movement = new CourtCaseMovement
                        {
                            EventName = eventName ?? "",
                            EventResult = eventResult ?? "",
                            Basis = basis ?? "",
                            EventDate = eventDate
                        };

                        // Добавляем только если есть хотя бы название события
                        if (!string.IsNullOrEmpty(eventName))
                        {
                            movements.Add(movement);
                        
                            logger.LogDebug("Добавлено событие: {EventName} - {EventDate}", 
                                eventName, eventDate?.ToString("dd.MM.yyyy") ?? "нет даты");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Ошибка при обработке строки движения дела");
                }
            }

            // Сохраняем движения дела
            courtCase.CaseMovements = movements;
        
            logger.LogInformation("✅ Извлечено событий движения дела: {Count}", movements.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении деталей движения дела {CaseNumber}", courtCase.CaseNumber);
        }
    }
    
    
    /// <summary>
    /// Обновляет категорию и подкатегорию из блока Категория
    /// </summary>
    private Task UpdateCategoryFromDlHorizontal(string categoryText, CourtCase courtCase)
    {
        try
        {
            if (string.IsNullOrEmpty(categoryText))
                return Task.CompletedTask;

            // Разделяем категорию и подкатегорию по символу "/"
            var parts = categoryText.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .ToArray();

            if (parts.Length >= 1)
            {
                courtCase.CaseCategory = parts[0];
                logger.LogInformation("Обновлена категория дела: {Category}", parts[0]);
            }

            if (parts.Length >= 2)
            {
                courtCase.CaseSubcategory = parts[1];
                logger.LogInformation("Обновлена подкатегория дела: {Subcategory}", parts[1]);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при обновлении категории из dl-horizontal");
        }

        return Task.CompletedTask;
    }
}