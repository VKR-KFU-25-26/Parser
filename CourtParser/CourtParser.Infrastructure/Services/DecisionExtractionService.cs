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
        
            // Сбрасываем флаг решения
            courtCase.HasDecision = false;
            courtCase.DecisionLink = string.Empty;
            courtCase.DecisionType = "Не найдено";
            courtCase.DecisionContent = string.Empty;
        
            // Ждем загрузки страницы
            await page.GoToAsync(courtCase.Link, WaitUntilNavigation.Networkidle2);
            await Task.Delay(3000, cancellationToken);

            // 1. Извлекаем детальную информацию
            await ExtractDetailedCaseInfo(page, courtCase);
        
            // 2. Извлекаем оригинальную ссылку
            await ExtractOriginalCaseLinkAsync(page, courtCase);
        
            // 3. Проверяем файловые решения
            bool fileDecisionFound = await CheckFileDecisionLinks(page, courtCase);
            if (fileDecisionFound) return;

            // 4. Проверяем HTML решения в порядке приоритета
            var extractionMethods = new List<Func<IPage, CourtCase, Task<bool>>>
            {
                ExtractFromBlockquoteStructure,
                ExtractFromXPath,
                ExtractFromStandardHtmlStructure,
                ExtractSimpleDecisionFromPage
            };
        
            foreach (var method in extractionMethods)
            {
                bool found = await method(page, courtCase);
                if (found)
                {
                    logger.LogInformation("Решение найдено методом: {MethodName}", 
                        method.Method.Name);
                    return;
                }
            }

            // 5. Если ничего не найдено
            logger.LogInformation("Для дела {CaseNumber} решение не найдено", courtCase.CaseNumber);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при проверке решения для дела {CaseNumber}", courtCase.CaseNumber);
            courtCase.HasDecision = false;
            courtCase.DecisionType = "Ошибка при проверке";
        }
    }
   
    /// <summary>
    /// ПРОСТОЙ МЕТОД: Извлекает решение через CSS селекторы
    /// </summary>
    private async Task<bool> ExtractFromBlockquoteStructure(IPage page, CourtCase courtCase)
    {
        try
        {
            logger.LogInformation("Ищем решение в структуре blockquote...");
        
            // СПОСОБ 1: Простой поиск blockquote
            var blockquote = await page.QuerySelectorAsync("blockquote");
        
            if (blockquote != null)
            {
                var text = await blockquote.EvaluateFunctionAsync<string>("el => el.textContent || el.innerText || ''");
            
                if (text.Contains("РЕШЕНИЕ") || text.Contains("ОПРЕДЕЛЕНИЕ") || text.Contains("ПОСТАНОВЛЕНИЕ") ||
                    text.Contains("Именем Российской Федерации"))
                {
                    var html = await blockquote.EvaluateFunctionAsync<string>("el => el.innerHTML");
                    return await ProcessDecisionHtml(html, courtCase, "simple_blockquote");
                }
            }
        
            // СПОСОБ 2: Ищем конкретную структуру
            var rows = await page.QuerySelectorAllAsync("div.row");
        
            foreach (var row in rows)
            {
                // Проверяем, есть ли в этом row заголовок решения
                var h3 = await row.QuerySelectorAsync("h3");
                if (h3 == null) continue;
            
                var h3Text = await h3.EvaluateFunctionAsync<string>("el => el.textContent || ''");
                if (!h3Text.Contains("Решение") && !h3Text.Contains("Определение")) continue;
            
                // Ищем blockquote в этом row
                var bq = await row.QuerySelectorAsync("blockquote");
                if (bq != null)
                {
                    var html = await bq.EvaluateFunctionAsync<string>("el => el.innerHTML");
                    return await ProcessDecisionHtml(html, courtCase, "row_blockquote");
                }
            
                // Если нет blockquote, берем весь row
                var rowHtml = await row.EvaluateFunctionAsync<string>("el => el.innerHTML");
                return await ProcessDecisionHtml(rowHtml, courtCase, "row_content");
            }
        
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении из blockquote");
            return false;
        }
    }

    /// <summary>
    /// Метод через XPath
    /// </summary>
    [Obsolete("Obsolete")]
    private async Task<bool> ExtractFromXPath(IPage page, CourtCase courtCase)
    {
        try
        {
            logger.LogInformation("Используем XPath для поиска решения...");
            
            var xpaths = new[]
            {
                "//blockquote[@itemprop='text']",
                "//div[contains(@class, 'row')]//blockquote",
                "//blockquote"
            };
            
            foreach (var xpath in xpaths)
            {
                try
                {
                    var elements = await page.XPathAsync(xpath);
                    if (elements != null && elements.Length > 0)
                    {
                        foreach (var element in elements)
                        {
                            var text = await element.EvaluateFunctionAsync<string>("el => el.textContent || ''");
                            if (text.Contains("РЕШЕНИЕ") || text.Contains("Именем Российской Федерации"))
                            {
                                var decisionHtml = await element.EvaluateFunctionAsync<string>("el => el.innerHTML");
                                if (!string.IsNullOrWhiteSpace(decisionHtml) && decisionHtml.Length > 500)
                                {
                                    return await ProcessDecisionHtml(decisionHtml, courtCase, "xpath_method");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "XPath {Xpath} не сработал", xpath);
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при XPath извлечении");
            return false;
        }
    }

    /// <summary>
    /// Извлекает решение из стандартной HTML структуры
    /// </summary>
    private async Task<bool> ExtractFromStandardHtmlStructure(IPage page, CourtCase courtCase)
    {
        try
        {
            logger.LogInformation("Ищем решение в стандартной HTML структуре...");
            
            var pageContent = await page.GetContentAsync();
            
            var startMarkers = new[]
            {
                "<blockquote itemprop=\"text\">",
                "<p style=\"TEXT-ALIGN: center\">РЕШЕНИЕ</p>",
                "<p style=\"TEXT-ALIGN: center\">ОПРЕДЕЛЕНИЕ</p>",
                "<p style=\"TEXT-ALIGN: center\">ИМЕНЕМ РОССИЙСКОЙ ФЕДЕРАЦИИ</p>"
            };
            
            foreach (var marker in startMarkers)
            {
                int startIndex = pageContent.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (startIndex >= 0)
                {
                    logger.LogInformation("Найден маркер начала: {Marker}", marker);
                    
                    string htmlSolution = pageContent.Substring(startIndex);
                    
                    // Ищем конец решения
                    int endIndex = FindDecisionEndInHtml(htmlSolution);
                    if (endIndex == -1)
                    {
                        endIndex = Math.Min(60000, htmlSolution.Length);
                    }
                    
                    htmlSolution = htmlSolution.Substring(0, endIndex);
                    
                    return await ProcessDecisionHtml(htmlSolution, courtCase, "standard_structure");
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
    /// Простой метод извлечения решения
    /// </summary>
    private async Task<bool> ExtractSimpleDecisionFromPage(IPage page, CourtCase courtCase)
    {
        try
        {
            logger.LogInformation("Используем простой метод извлечения...");
            
            var fullText = await page.EvaluateFunctionAsync<string>(@"
                () => {
                    const body = document.body;
                    if (!body) return '';
                    
                    const clone = body.cloneNode(true);
                    const scripts = clone.querySelectorAll('script, style, noscript, iframe, nav, footer, header');
                    scripts.forEach(el => el.remove());
                    
                    return clone.innerText || clone.textContent || '';
                }
            ");
            
            if (string.IsNullOrWhiteSpace(fullText))
                return false;
            
            var startKeywords = new[]
            {
                "РЕШЕНИЕ",
                "ОПРЕДЕЛЕНИЕ",
                "ПОСТАНОВЛЕНИЕ",
                "Именем Российской Федерации"
            };
            
            int startIndex = -1;
            foreach (var keyword in startKeywords)
            {
                startIndex = fullText.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
                if (startIndex >= 0) break;
            }
            
            if (startIndex < 0) return false;
            
            int endIndex = Math.Min(startIndex + 15000, fullText.Length);
            
            var endMarkers = new[]
            {
                "\nСудья",
                "\nПредседательствующий",
                "\nМировой судья",
                "Решение может быть обжаловано"
            };
            
            foreach (var marker in endMarkers)
            {
                int markerIndex = fullText.IndexOf(marker, startIndex, StringComparison.OrdinalIgnoreCase);
                if (markerIndex > startIndex && markerIndex < endIndex)
                {
                    endIndex = markerIndex + 100;
                }
            }
            
            string decisionText = fullText.Substring(startIndex, endIndex - startIndex);
            decisionText = CleanDecisionText(decisionText);
            
            if (string.IsNullOrWhiteSpace(decisionText) || decisionText.Length < 300)
                return false;
            
            if (IsValidDecisionContent(decisionText))
            {
                var documentType = DetermineDocumentTypeFromContent(decisionText);
                
                courtCase.HasDecision = true;
                courtCase.DecisionLink = courtCase.Link + "#simple_text";
                courtCase.DecisionType = documentType;
                courtCase.DecisionContent = decisionText;
                
                logger.LogInformation("Найдено решение простым методом: {Type}, длина: {Length}", 
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
    /// Обрабатывает извлеченный HTML решения
    /// </summary>
    private async Task<bool> ProcessDecisionHtml(string html, CourtCase courtCase, string methodName)
    {
        try
        {
            // Убираем таблицу law-case-table если есть
            html = RemoveLawCaseTable(html);
            
            // Обрабатываем защищенные данные
            html = ProcessProtectedData(html);
            
            // Очищаем HTML
            string cleanHtml = CleanHtmlForStorage(html);
            
            if (string.IsNullOrWhiteSpace(cleanHtml) || cleanHtml.Length < 500)
            {
                logger.LogInformation("HTML слишком короткий после обработки: {Length}", cleanHtml.Length);
                return false;
            }
            
            // Проверяем валидность
            if (!IsValidDecisionContent(cleanHtml))
            {
                logger.LogInformation("HTML не прошел валидацию как решение");
                return false;
            }
            
            var documentType = DetermineDocumentTypeFromContent(cleanHtml);
            
            courtCase.HasDecision = true;
            courtCase.DecisionLink = courtCase.Link + "#" + methodName;
            courtCase.DecisionType = documentType;
            courtCase.DecisionContent = cleanHtml;
            
            logger.LogInformation("Найдено решение методом {MethodName}: {Type}, длина: {Length}", 
                methodName, documentType, cleanHtml.Length);
            
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при обработке HTML решения");
            return false;
        }
    }

    /// <summary>
    /// Убирает таблицу law-case-table
    /// </summary>
    private string RemoveLawCaseTable(string html)
    {
        try
        {
            var tablePattern = @"<table class=""law-case-table"">.*?</table>";
            return Regex.Replace(html, tablePattern, "", 
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }
        catch
        {
            return html;
        }
    }

    /// <summary>
    /// Находит конец решения в HTML
    /// </summary>
    private int FindDecisionEndInHtml(string html)
    {
        var endMarkers = new[]
        {
            "<table class=\"law-case-table\">",
            "<div class=\"row\" style=\"margin-top:100px\">",
            "<div class=\"col-md-12\" style=\"margin-bottom:40px\">",
            "Судья</p>",
            "Председательствующий</p>",
            "</blockquote>"
        };
        
        foreach (var marker in endMarkers)
        {
            int index = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 1000)
            {
                return index;
            }
        }
        
        return -1;
    }

    /// <summary>
    /// Очищает текст решения
    /// </summary>
    private string CleanDecisionText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        
        text = Regex.Replace(text, @"<[^>]*>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");
        text = Regex.Replace(text, @"([.!?])\s+([А-ЯA-Z])", "$1\n$2");
        
        return text.Trim();
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
            // 1. Обрабатываем защищенные данные
            html = ProcessProtectedData(html);
        
            // 2. Убираем комментарии
            html = Regex.Replace(html, @"<!--.*?-->", "", RegexOptions.Singleline);
        
            // 3. Убираем опасные теги
            var dangerousTags = new[] { "script", "style", "iframe", "object", "embed", "link", "meta" };
            foreach (var tag in dangerousTags)
            {
                html = Regex.Replace(html, $@"<{tag}[^>]*>.*?</{tag}>", "", 
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, $@"<{tag}[^>]*/>", "", RegexOptions.IgnoreCase);
            }
        
            // 4. Декодируем HTML сущности
            html = System.Net.WebUtility.HtmlDecode(html);
        
            // 5. Нормализуем пробелы
            html = Regex.Replace(html, @"\s+", " ");
        
            return html.Trim();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при очистке HTML");
            return html;
        }
    }

    /// <summary>
    /// Обрабатывает защищенные данные (ФИО, адреса и т.д.)
    /// </summary>
    private string ProcessProtectedData(string html)
    {
        try
        {
            var patterns = new Dictionary<string, string>
            {
                { @"<span[^>]*class\s*=\s*[""']?(FIO\d*)[""']?[^>]*>(.*?)</span>", "<ФИО>$2</ФИО>" },
                { @"<span[^>]*class\s*=\s*[""']?(Address\d*)[""']?[^>]*>(.*?)</span>", "<адрес>$2</адрес>" },
                { @"<span[^>]*class\s*=\s*[""']?(others\d*)[""']?[^>]*>(.*?)</span>", "<данные изъяты>$2</данные изъяты>" },
                { @"<span[^>]*class\s*=\s*[""']?(Data\d*)[""']?[^>]*>(.*?)</span>", "<дата>$2</дата>" },
                { @"<span[^>]*class\s*=\s*[""']?(Nomer\d*)[""']?[^>]*>(.*?)</span>", "<номер>$2</номер>" }
            };
            
            foreach (var pattern in patterns)
            {
                html = Regex.Replace(html, pattern.Key, pattern.Value, 
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
            }
            
            return html;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при обработке защищенных данных");
            return html;
        }
    }

    /// <summary>
    /// ВАЛИДАЦИЯ содержимого решения
    /// </summary>
    private bool IsValidDecisionContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var cleanContent = content.ToLower();

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
            "председательствующий"
        };

        bool hasRequired = requiredElements.Any(element => cleanContent.Contains(element));
        int additionalCount = additionalElements.Count(element => cleanContent.Contains(element));

        return hasRequired && additionalCount >= 2;
    }

    /// <summary>
    /// Определяет тип документа из содержимого
    /// </summary>
    private string DetermineDocumentTypeFromContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "Неизвестно";

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
        if (cleanContent.Contains("мотивированное решение"))
            return "Мотивированное решение";
        
        if (cleanContent.Contains("решил:") || cleanContent.Contains("решила:"))
            return "Решение"; 
        if (cleanContent.Contains("определил:") || cleanContent.Contains("определила:"))
            return "Определение";
        if (cleanContent.Contains("постановил:") || cleanContent.Contains("постановила:"))
            return "Постановление";
        
        return "Судебный акт";
    }

    /// <summary>
    /// Проверяет ссылки на файлы решений
    /// </summary>
    private async Task<bool> CheckFileDecisionLinks(IPage page, CourtCase courtCase)
    {
        try
        {
            logger.LogInformation("Ищем ссылки на файлы решений для дела {CaseNumber}", courtCase.CaseNumber);

            var downloadBlocks = await page.QuerySelectorAllAsync(".btn-group1, .download-block, div[style*='margin-bottom:40px'], div[style*='margin-top:10px']");
        
            foreach (var block in downloadBlocks)
            {
                var decisionLinks = await block.QuerySelectorAllAsync("a");
                
                foreach (var link in decisionLinks)
                {
                    var href = await link.EvaluateFunctionAsync<string>("el => el.getAttribute('href')");
                    var text = await link.EvaluateFunctionAsync<string>("el => el.textContent?.trim()");
                
                    if (!string.IsNullOrEmpty(href) && IsValidDecisionFileLink(href, text))
                    {
                        var fullLink = href.StartsWith("/") 
                            ? "https://www.xn--90afdbaav0bd1afy6eub5d.xn--p1ai" + href 
                            : href;
                    
                        var documentType = DetermineDocumentTypeFromLink(text);
                    
                        courtCase.HasDecision = true;
                        courtCase.DecisionLink = fullLink;
                        courtCase.DecisionType = documentType;
                        courtCase.DecisionContent = string.Empty;
                    
                        logger.LogInformation("Найдена ссылка на файл решения: {Type} - {Link}", 
                            documentType, fullLink);
                    
                        return true;
                    }
                }
            }
        
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при проверке ссылок на файлы решений");
            return false;
        }
    }
  
    /// <summary>
    /// Проверяет валидность ссылки на файл решения
    /// </summary>
    private bool IsValidDecisionFileLink(string href, string linkText)
    {
        if (string.IsNullOrEmpty(href)) 
            return false;
    
        var validExtensions = new[] { ".doc", ".docx", ".pdf", ".rtf", ".txt", ".odt" };
        bool hasValidExtension = validExtensions.Any(ext => href.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    
        var validPaths = new[] { "/decisions/", "/solutions/", "/docs/", "/files/" };
        bool hasValidPath = validPaths.Any(path => href.Contains(path, StringComparison.OrdinalIgnoreCase));
    
        if (string.IsNullOrEmpty(linkText)) 
            return hasValidExtension && hasValidPath;
    
        var cleanText = linkText.ToLower();
    
        var validTextIndicators = new[]
        {
            "решение", "определение", "постановление", "приказ", 
            "мотивированное", "документ", "скачать"
        };
    
        bool hasValidText = validTextIndicators.Any(indicator => cleanText.Contains(indicator));
    
        return (hasValidExtension && hasValidPath) || (hasValidText && hasValidPath);
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
        if (text.Contains("судебный приказ")) return "Судебный приказ";
        if (text.Contains("приказ")) return "Приказ";
    
        return "Документ";
    }

    [Obsolete("Obsolete")]
    private async Task ExtractDetailedCaseInfo(IPage page, CourtCase courtCase)
    {
        try
        {
            logger.LogInformation("Извлекаем детальную информацию для дела: {CaseNumber}", courtCase.CaseNumber);
            await ExtractHeaderInfo(page, courtCase);
            await ExtractPartiesInfo(page, courtCase);
            await ExtractCaseMovementInfo(page, courtCase);
            await ExtractCaseResultInfo(page, courtCase);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении детальной информации");
        }
    }

    private async Task ExtractHeaderInfo(IPage page, CourtCase courtCase)
    {
        try
        {
            var headerBlock = await page.QuerySelectorAsync(".col-md-8.text-right");
            if (headerBlock == null) return;

            var headerText = await headerBlock.EvaluateFunctionAsync<string>("el => el.textContent");
            
            // Номер дела
            var caseNumberMatch = Regex.Match(headerText, @"Номер дела:\s*([^\s]+)", RegexOptions.IgnoreCase);
            if (caseNumberMatch.Success)
            {
                courtCase.CaseNumber = caseNumberMatch.Groups[1].Value.Trim();
            }

            // Дата начала
            var startDateMatch = Regex.Match(headerText, @"Дата начала:\s*(\d{1,2}\.\d{1,2}\.\d{4})", RegexOptions.IgnoreCase);
            if (startDateMatch.Success)
            {
                if (DateTime.TryParse(startDateMatch.Groups[1].Value.Trim(), out var startDate))
                {
                    courtCase.StartDate = startDate;
                }
            }

            // Дата рассмотрения (ReceivedDate)
            var decisionDateMatch = Regex.Match(headerText, @"Дата рассмотрения:\s*(\d{1,2}\.\d{1,2}\.\d{4})", RegexOptions.IgnoreCase);
            if (decisionDateMatch.Success)
            {
                if (DateTime.TryParse(decisionDateMatch.Groups[1].Value.Trim(), out var decisionDate))
                {
                    courtCase.ReceivedDate = decisionDate;
                }
            }

            // Суд
            var courtMatch = Regex.Match(headerText, @"Суд:\s*([^\n]+)", RegexOptions.IgnoreCase);
            if (courtMatch.Success)
            {
                courtCase.CourtType = courtMatch.Groups[1].Value.Trim();
            }

            // Судья
            var judgeMatch = Regex.Match(headerText, @"Судья:\s*([^\n]+)", RegexOptions.IgnoreCase);
            if (judgeMatch.Success)
            {
                courtCase.JudgeName = judgeMatch.Groups[1].Value.Trim();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении информации заголовка");
        }
    }
    
    /// <summary>
    /// Извлекает результат дела
    /// </summary>
    private async Task ExtractCaseResultInfo(IPage page, CourtCase courtCase)
    {
        try
        {
            var dlHorizontal = await page.QuerySelectorAsync("dl.dl-horizontal");
            if (dlHorizontal == null)
            {
                courtCase.CaseResult = string.Empty;
                return;
            }

            var dtElements = await dlHorizontal.QuerySelectorAllAsync("dt");
            var ddElements = await dlHorizontal.QuerySelectorAllAsync("dd");

            for (int i = 0; i < Math.Min(dtElements.Length, ddElements.Length); i++)
            {
                var key = await dtElements[i].EvaluateFunctionAsync<string>("el => el.textContent?.trim()");
                var value = await ddElements[i].EvaluateFunctionAsync<string>("el => el.textContent?.trim()");

                if (key == "Результат" || key == "Итог")
                {
                    courtCase.CaseResult = value ?? string.Empty;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении результата дела");
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
            var showLinkButton = await page.QuerySelectorAsync("#show-original-link");
            if (showLinkButton == null) return;

            await showLinkButton.ClickAsync();
            await Task.Delay(2000);

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
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении оригинальной ссылки");
        }
    }

    [Obsolete("Obsolete")]
    private async Task ExtractPartiesInfo(IPage page, CourtCase courtCase)
    {
        try
        {
            var tables = await page.QuerySelectorAllAsync("table.table-condensed");

            foreach (var table in tables)
            {
                var tableText = await table.EvaluateFunctionAsync<string>("el => el.textContent");
            
                if (tableText.Contains("Стороны по делу") || 
                    tableText.Contains("ИСТЕЦ") || 
                    tableText.Contains("ОТВЕТЧИК"))
                {
                    await ExtractPartiesFromTable(table, courtCase);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при поиске таблицы сторон");
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

                        if (cleanType == "ИСТЕЦ" || cleanType.StartsWith("ИСТЕЦ"))
                        {
                            plaintiffs.Add(cleanName);
                        }
                        else if (cleanType == "ОТВЕТЧИК" || cleanType.StartsWith("ОТВЕТЧИК"))
                        {
                            defendants.Add(cleanName);
                        }
                        else if (cleanType.Contains("ТРЕТЬЕ ЛИЦО") || cleanType == "ТРЕТЬЕ ЛИЦО" || cleanType.StartsWith("ТРЕТЬЕ"))
                        {
                            thirdParties.Add(cleanName);
                        }
                        else if (cleanType == "ПРЕДСТАВИТЕЛЬ" || cleanType.StartsWith("ПРЕДСТАВИТЕЛЬ"))
                        {
                            representatives.Add(cleanName);
                        }
                    }
                }
            }

            courtCase.Plaintiff = plaintiffs.Any() ? string.Join("; ", plaintiffs) : "";
            courtCase.Defendant = defendants.Any() ? string.Join("; ", defendants) : "";
            courtCase.ThirdParties = thirdParties.Any() ? string.Join("; ", thirdParties) : "";
            courtCase.Representatives = representatives.Any() ? string.Join("; ", representatives) : "";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении сторон из таблицы");
        }
    }

    /// <summary>
    /// Извлекает информацию о движении дела
    /// </summary>
    private async Task ExtractCaseMovementInfo(IPage page, CourtCase courtCase)
    {
        try
        {
            var movementTables = await page.QuerySelectorAllAsync("table.table-condensed");
        
            foreach (var table in movementTables)
            {
                var tableText = await table.EvaluateFunctionAsync<string>("el => el.textContent");
                if (tableText.Contains("Движение дела") || tableText.Contains("Наименование события"))
                {
                    await ExtractMovementDetailsFromTable(table, courtCase);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении информации о движении дела");
        }
    }

    private async Task ExtractMovementDetailsFromTable(IElementHandle table, CourtCase courtCase)
    {
        try
        {
            var rows = await table.QuerySelectorAllAsync("tr");
            var movements = new List<CourtCaseMovement>();

            foreach (var row in rows)
            {
                try
                {
                    var isHeader = await row.EvaluateFunctionAsync<bool>("el => el.classList.contains('active')");
                    if (isHeader) continue;

                    var cells = await row.QuerySelectorAllAsync("td");
                
                    if (cells.Length >= 4)
                    {
                        var eventName = await cells[0].EvaluateFunctionAsync<string>("el => el.textContent?.trim()");
                        var eventResult = await cells[1].EvaluateFunctionAsync<string>("el => el.textContent?.trim()");
                        var basis = await cells[2].EvaluateFunctionAsync<string>("el => el.textContent?.trim()");
                        var eventDateStr = await cells[3].EvaluateFunctionAsync<string>("el => el.textContent?.trim()");

                        DateTime? eventDate = null;
                        if (!string.IsNullOrEmpty(eventDateStr) && DateTime.TryParse(eventDateStr, out var parsedDate))
                        {
                            eventDate = parsedDate;
                        }

                        var movement = new CourtCaseMovement
                        {
                            EventName = eventName ?? "",
                            EventResult = eventResult ?? "",
                            Basis = basis ?? "",
                            EventDate = eventDate
                        };

                        if (!string.IsNullOrEmpty(eventName))
                        {
                            movements.Add(movement);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Ошибка при обработке строки движения дела");
                }
            }

            courtCase.CaseMovements = movements;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при извлечении деталей движения дела");
        }
    }
}