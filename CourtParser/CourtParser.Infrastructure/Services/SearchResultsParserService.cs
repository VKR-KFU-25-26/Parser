using CourtParser.Infrastructure.Utilities;
using CourtParser.Models.Entities;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;

namespace CourtParser.Infrastructure.Services;

/// <summary>
/// Сервис для заполнения формы и поиска результата
/// </summary>
public class SearchResultsParserService(ILogger<SearchResultsParserService> logger)
{
    private string _currentFederalDistrict = string.Empty;
    private string _currentRegion = string.Empty;
    private string _caseCategory = "Имущественные споры";
    private string _caseSubcategory = "Иски о взыскании сумм по договору займа, кредитному договору";

    public void SetSearchContext(string federalDistrict, string region, string category, string subcategory)
    {
        _currentFederalDistrict = federalDistrict;
        _currentRegion = region;
        _caseCategory = category;
        _caseSubcategory = subcategory;
    }
    
  public async Task<List<CourtCase>> ParseSearchResultsWithRetry(IPage page, int maxRetries = 3)
    {
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                logger.LogInformation("Попытка парсинга результатов #{Attempt}", attempt);
                
                try
                {
                    await page.WaitForSelectorAsync("table.table-bordered", new WaitForSelectorOptions 
                    { 
                        Timeout = 10000 
                    });
                }
                catch
                {
                    await page.WaitForSelectorAsync("table", new WaitForSelectorOptions 
                    { 
                        Timeout = 5000 
                    });
                }

                var cases = await ParseSearchResultsAsync(page);
                
                if (cases.Count > 0)
                {
                    logger.LogInformation("Успешно спарсено {Count} дел с попытки #{Attempt}", cases.Count, attempt);
                    return cases;
                }

                logger.LogWarning("На попытке #{Attempt} не найдено дел, ждем и пробуем снова", attempt);
                await Task.Delay(2000);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Ошибка при парсинге на попытке #{Attempt}", attempt);
                if (attempt < maxRetries) await Task.Delay(2000);
            }
        }

        logger.LogError("Не удалось спарсить результаты после {MaxRetries} попыток", maxRetries);
        return [];
    }

    private async Task<List<CourtCase>> ParseSearchResultsAsync(IPage page)
    {
        var cases = new List<CourtCase>();

        try
        {
            var countElement = await page.QuerySelectorAsync(".count");
            if (countElement != null)
            {
                var countText = await countElement.EvaluateFunctionAsync<string>("el => el.textContent");
                logger.LogInformation("Информация о результатах: {CountText}", countText);
            }

            var caseTables = await page.QuerySelectorAllAsync("table.table-bordered");
            logger.LogInformation("Найдено таблиц с делами: {Count}", caseTables.Length);

            if (caseTables.Length == 0)
            {
                logger.LogInformation("Пытаемся найти дела альтернативным способом...");
                caseTables = await page.QuerySelectorAllAsync("table");
                logger.LogInformation("Альтернативный поиск нашел таблиц: {Count}", caseTables.Length);
            }

            foreach (var table in caseTables)
            {
                try
                {
                    var caseData = await ParseCaseTableAsync(table);
                    if (caseData != null) cases.Add(caseData);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Ошибка при парсинге таблицы дела");
                }
            }

            if (cases.Count == 0)
            {
                cases = await ParseAlternativeResultsAsync(page);
            }

            logger.LogInformation("Успешно спарсено дел: {Count}", cases.Count);
            return cases;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при парсинге результатов поиска");
            
            try
            {
                var alternativeCases = await ParseAlternativeResultsAsync(page);
                logger.LogInformation("Альтернативный парсинг нашел дел: {Count}", alternativeCases.Count);
                return alternativeCases;
            }
            catch (Exception altEx)
            {
                logger.LogError(altEx, "Ошибка при альтернативном парсинге");
                return cases;
            }
        }
    }

    private async Task<CourtCase?> ParseCaseTableAsync(IElementHandle table)
    {
        try
        {
            var headerRow = await table.QuerySelectorAsync("tr.active");
            if (headerRow == null) return null;

            var headerCells = await headerRow.QuerySelectorAllAsync("td");
            if (headerCells.Length < 2) return null;

            var courtElement = headerCells[0];
            var courtName = await courtElement.EvaluateFunctionAsync<string>("el => el.textContent");
        
            var caseElement = headerCells[1];
            var linkElement = await caseElement.QuerySelectorAsync("a");
            if (linkElement == null) return null;

            var caseNumber = await linkElement.EvaluateFunctionAsync<string>("el => el.textContent");
            var href = await linkElement.EvaluateFunctionAsync<string>("el => el.getAttribute('href')");
            var link = !string.IsNullOrEmpty(href) ? 
                "https://www.xn--90afdbaav0bd1afy6eub5d.xn--p1ai" + href : 
                string.Empty;

            var detailsRow = await table.QuerySelectorAsync("tr:not(.active)");
            if (detailsRow == null) return null;

            var detailCells = await detailsRow.QuerySelectorAllAsync("td");
            if (detailCells.Length < 2) return null;

            var datesElement = detailCells[0];
            var datesText = await datesElement.EvaluateFunctionAsync<string>("el => el.textContent");
        
            var partiesElement = detailCells[1];
            var partiesText = await partiesElement.EvaluateFunctionAsync<string>("el => el.textContent");

            var dates = DateExtractor.ExtractDates(datesText);
            var (plaintiff, defendant) = TextCleaner.ExtractParties(partiesText);

            return new CourtCase
            {
                Title = $"{TextCleaner.CleanText(courtName)} - {TextCleaner.CleanText(caseNumber)}",
                CaseNumber = TextCleaner.CleanText(caseNumber),
                Link = link,
                CourtType = TextCleaner.CleanText(courtName),
                Description = $"Поступило: {dates.receivedDate}, Решение: {dates.decisionDate}",
                Subject = $"Истец: {plaintiff} | Ответчик: {defendant}",
                HasDecision = false,
                DecisionLink = string.Empty,
                
                // НОВЫЕ ПОЛЯ:
                FederalDistrict = _currentFederalDistrict,
                Region = _currentRegion,
                Plaintiff = plaintiff,
                Defendant = defendant,
                ReceivedDate = dates.receivedDateObj,
                CaseCategory = _caseCategory,
                CaseSubcategory = _caseSubcategory
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при парсинге таблицы дела");
            return null;
        }
    }
    
    private async Task<List<CourtCase>> ParseAlternativeResultsAsync(IPage page)
    {
        var cases = new List<CourtCase>();
    
        try
        {
            logger.LogInformation("Запуск альтернативного парсинга...");

            // Способ 1: Ищем все строки таблиц
            var allRows = await page.QuerySelectorAllAsync("tr");
            logger.LogInformation("Найдено строк в таблицах: {Count}", allRows.Length);

            foreach (var row in allRows)
            {
                try
                {
                    // Пропускаем заголовки
                    var isActive = await row.EvaluateFunctionAsync<bool>("el => el.classList.contains('active')");
                    if (isActive) continue;

                    var cells = await row.QuerySelectorAllAsync("td");
                    if (cells.Length >= 2)
                    {
                        var caseData = await ParseTableRowAsync(cells);
                        if (caseData != null)
                        {
                            cases.Add(caseData);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Ошибка при парсинге строки таблицы");
                }
            }

            // Способ 2: Ищем ссылки на дела
            if (cases.Count == 0)
            {
                var caseLinks = await page.QuerySelectorAllAsync("a[href*='/extended']");
                logger.LogInformation("Найдено ссылок на дела: {Count}", caseLinks.Length);

                foreach (var link in caseLinks)
                {
                    try
                    {
                        var caseData = await ParseCaseLinkAsync(link);
                        if (caseData != null)
                        {
                            cases.Add(caseData);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Ошибка при парсинге ссылки на дело");
                    }
                }
            }

            // Убираем дубликаты по номеру дела
            cases = cases
                .Where(c => !string.IsNullOrEmpty(c.CaseNumber))
                .GroupBy(c => c.CaseNumber)
                .Select(g => g.First())
                .ToList();

            logger.LogInformation("Альтернативный парсинг завершен. Найдено уникальных дел: {Count}", cases.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при альтернативном парсинге");
        }
    
        return cases;
    }

    private async Task<CourtCase?> ParseTableRowAsync(IElementHandle[] cells)
    {
        try
        {
            // Первая ячейка - даты
            var datesElement = cells[0];
            var datesText = await datesElement.EvaluateFunctionAsync<string>("el => el.textContent");
        
            // Вторая ячейка - участники и возможно ссылка
            var partiesElement = cells[1];
            var partiesText = await partiesElement.EvaluateFunctionAsync<string>("el => el.textContent");
        
            // Ищем ссылку в любой из ячеек
            IElementHandle? linkElement = null;
            foreach (var cell in cells)
            {
                linkElement = await cell.QuerySelectorAsync("a");
                if (linkElement != null) break;
            }

            if (linkElement == null) return null;

            var caseNumber = await linkElement.EvaluateFunctionAsync<string>("el => el.textContent");
            var href = await linkElement.EvaluateFunctionAsync<string>("el => el.getAttribute('href')");
            var link = !string.IsNullOrEmpty(href) ? 
                "https://www.xn--90afdbaav0bd1afy6eub5d.xn--p1ai" + href : 
                string.Empty;

            // Пытаемся найти информацию о суде (может быть в предыдущей строке)
            var courtName = "Не указан";
            var parentRow = await linkElement.EvaluateFunctionAsync<IElementHandle>("el => el.closest('tr').previousElementSibling");
            if (parentRow != null)
            {
                var courtCell = await parentRow.QuerySelectorAsync("td");
                if (courtCell != null)
                {
                    courtName = await courtCell.EvaluateFunctionAsync<string>("el => el.textContent");
                }
            }

            var dates = DateExtractor.ExtractDates(datesText);
            var (plaintiff, defendant) = TextCleaner.ExtractParties(partiesText);

            return new CourtCase
            {
                Title = $"{TextCleaner.CleanText(courtName)} - {TextCleaner.CleanText(caseNumber)}",
                CaseNumber = TextCleaner.CleanText(caseNumber),
                Link = link,
                CourtType = TextCleaner.CleanText(courtName),
                Description = $"Поступило: {dates.receivedDate}, Решение: {dates.decisionDate}",
                Subject = $"Истец: {plaintiff} | Ответчик: {defendant}",
                HasDecision = false,
                DecisionLink = string.Empty,
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Ошибка при парсинге строки таблицы");
            return null;
        }
    }

    private async Task<CourtCase?> ParseCaseLinkAsync(IElementHandle link)
    {
        try
        {
            var caseNumber = await link.EvaluateFunctionAsync<string>("el => el.textContent");
            var href = await link.EvaluateFunctionAsync<string>("el => el.getAttribute('href')");
            var linkUrl = !string.IsNullOrEmpty(href) ? 
                "https://www.xn--90afdbaav0bd1afy6eub5d.xn--p1ai" + href : 
                string.Empty;

            // Пытаемся найти дополнительную информацию вокруг ссылки
            var parentRow = await link.EvaluateFunctionAsync<IElementHandle>("el => el.closest('tr')");
            string courtName = "Не указан";
            string datesText = "";
            string partiesText = "";

            if (parentRow != null)
            {
                var cells = await parentRow.QuerySelectorAllAsync("td");
                if (cells.Length >= 2)
                {
                    // Первая ячейка - даты
                    datesText = await cells[0].EvaluateFunctionAsync<string>("el => el.textContent");
                    // Вторая ячейка - участники
                    partiesText = await cells[1].EvaluateFunctionAsync<string>("el => el.textContent");
                }

                // Ищем суд в предыдущей строке
                var prevRow = await parentRow.EvaluateFunctionAsync<IElementHandle>("el => el.previousElementSibling");
                if (prevRow != null)
                {
                    var courtCell = await prevRow.QuerySelectorAsync("td");
                    if (courtCell != null)
                    {
                        courtName = await courtCell.EvaluateFunctionAsync<string>("el => el.textContent");
                    }
                }
            }

            var dates = DateExtractor.ExtractDates(datesText);
            var (plaintiff, defendant) = TextCleaner.ExtractParties(partiesText);

            return new CourtCase
            {
                Title = $"{TextCleaner.CleanText(courtName)} - {TextCleaner.CleanText(caseNumber)}",
                CaseNumber = TextCleaner.CleanText(caseNumber),
                Link = linkUrl,
                CourtType = TextCleaner.CleanText(courtName),
                Description = $"Поступило: {dates.receivedDate}, Решение: {dates.decisionDate}",
                Subject = $"Истец: {plaintiff} | Ответчик: {defendant}",
                HasDecision = false,
                DecisionLink = string.Empty,
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Ошибка при парсинге ссылки на дело");
            return null;
        }
    }
}