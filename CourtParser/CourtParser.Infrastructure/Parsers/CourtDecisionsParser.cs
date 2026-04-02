using CourtParser.Common.Interfaces;
using CourtParser.Infrastructure.Services;
using CourtParser.Models.Entities;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;

namespace CourtParser.Infrastructure.Parsers;

public class CourtDecisionsParser(
    ILogger<CourtDecisionsParser> logger,
    RegionSelectionService regionService,
    SearchResultsParserService searchParser,
    DecisionExtractionService decisionService) : IParser
{
    private const int MaxPages = 2;


    [Obsolete("Obsolete")]
    public async Task<List<CourtCase>> ParseCasesAsync(List<string> regions, int page)
    {
        return await FillFormAndSearchCasesAsync(regions, CancellationToken.None);
    }
    
    [Obsolete("Obsolete")]
    private async Task<List<CourtCase>> FillFormAndSearchCasesAsync(
        List<string>? regions = null, 
        CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync("https://www.xn--90afdbaav0bd1afy6eub5d.xn--p1ai/", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Сайт недоступен, пропускаем парсинг");
            return [];
        }
        
        // await new BrowserFetcher().DownloadAsync();

        var launchOptions = new LaunchOptions
        {
            Headless = true, // true для Docker
            Timeout = 120000,
            ExecutablePath = "/usr/bin/chromium",
            Args =
            [
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-dev-shm-usage",
                "--disable-accelerated-2d-canvas",
                "--disable-gpu",
                "--disable-gpu-sandbox",
                "--disable-software-rasterizer",
                "--disable-features=VizDisplayCompositor",
                "--disable-blink-features=AutomationControlled", 
                "--disable-web-security", 
                "--disable-features=IsolateOrigins,site-per-process",
                "--single-process" 
            ]
        };

        await using var browser = await Puppeteer.LaunchAsync(launchOptions);
        await using var page = await browser.NewPageAsync();
        await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        var allCases = new List<CourtCase>();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await page.GoToAsync("https://www.xn--90afdbaav0bd1afy6eub5d.xn--p1ai/extended-search", 
                WaitUntilNavigation.Networkidle2);

            await page.WaitForSelectorAsync("#extendedSearch_case_type", new WaitForSelectorOptions 
            { 
                Timeout = 60000 
            });

            logger.LogInformation("Форма загружена, начинаем заполнение...");

            // Заполнение формы
            await FillSearchForm(page, cancellationToken);
        
            await Task.Delay(2000, cancellationToken);

            // Определяем федеральный округ и регионы для контекста парсера
            var (federalDistrict, actualRegions) = ParseRegionsForContext(regions);
    
            // Устанавливаем контекст поиска для парсера
            searchParser.SetSearchContext(
                federalDistrict,
                string.Join(", ", actualRegions),
                "Имущественные споры", 
                "Иски о взыскании сумм по договору займа, кредитному договору"
            );

            logger.LogInformation("Контекст поиска: ФО={FederalDistrict}, Регионы={Regions}", 
                federalDistrict, string.Join(", ", actualRegions));

            // Выбор регионов
            if (regions != null && regions.Any())
            {
                logger.LogInformation("Начинаем выбор регионов: {Regions}", string.Join(", ", regions));
                await regionService.SelectRegionsAsync(page, regions);
            
                await Task.Delay(3000, cancellationToken);
            
                // Проверяем, что форма все еще доступна
                await page.WaitForSelectorAsync("#extendedSearch_search", new WaitForSelectorOptions 
                { 
                    Timeout = 10000 
                });
            }

            // Выполнение поиска
            await PerformSearch(page, cancellationToken);

            // Парсинг результатов
            var firstPageCases = await searchParser.ParseSearchResultsWithRetry(page);
            allCases.AddRange(firstPageCases);

            if (firstPageCases.Count > 0)
            {
                await ParseAdditionalPagesAsync(page, allCases, cancellationToken);
            }

            // проверка решений
            if (allCases.Count > 0)
            {
                await CheckDecisionsForCases(page, allCases, cancellationToken);
            }

            logger.LogInformation("Всего спарсено дел со всех страниц: {Count}", allCases.Count);
            return allCases;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Парсинг отменен");
            return allCases;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при заполнении формы и поиске");
            return allCases;
        }
    }
    
    /// <summary>
    /// Метод для парсинга регионов и определения контекста
    /// </summary>
    /// <param name="regions">Регионы</param>
    private (string federalDistrict, List<string> regions) ParseRegionsForContext(List<string>? regions)
    {
        if (regions == null || !regions.Any())
        {
            return ("Приволжский федеральный округ", new List<string> { "Республика Татарстан" });
        }

        // Список федеральных округов
        var federalDistricts = new List<string>
        {
            "Центральный федеральный округ",
            "Северо-Западный федеральный округ",
            "Южный федеральный округ",
            "Северо-Кавказский федеральный округ",
            "Приволжский федеральный округ", 
            "Уральский федеральный округ",
            "Сибирский федеральный округ",
            "Дальневостоский федеральный округ",
            "Крымский федеральный округ"
        };

        // Ищем федеральный округ в переданных регионах
        var federalDistrict = regions.FirstOrDefault(r => federalDistricts.Contains(r)) 
                              ?? "Приволжский федеральный округ";

        // Фильтруем только конкретные регионы (не федеральные округа)
        var actualRegions = regions.Where(r => !federalDistricts.Contains(r)).ToList();

        // Если передали только федеральный округ без конкретных регионов
        if (actualRegions.Count == 0 && federalDistricts.Contains(federalDistrict))
        {
            // Для случая когда выбрали весь федеральный округ
            actualRegions = [federalDistrict];
        }
        else if (actualRegions.Count == 0)
        {
            // Дефолтный случай
            actualRegions = ["Республика Татарстан"];
        }

        return (federalDistrict, actualRegions);
    }

    private async Task FillSearchForm(IPage page, CancellationToken cancellationToken)
    {
        var canSelectRegions = await CheckRegionSelectionAvailability(page);
        if (!canSelectRegions)
        {
            logger.LogWarning("Выбор регионов недоступен, продолжаем без фильтрации по регионам");
        }
    
        // Выбираем тип дела: Первая инстанция (гражданские и административные дела)
        await page.SelectAsync("#extendedSearch_case_type", "gr_first");
        logger.LogInformation("Выбран тип дела: Первая инстанция (гражданские и административные дела)");

        // Загрузки категорий
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#extendedSearch_sub_category_1') !== null",
            new WaitForFunctionOptions { Timeout = 15000 }
        );
        await Task.Delay(2000, cancellationToken);

        // Выбираем категорию: Имущественные споры
        await page.SelectAsync("#extendedSearch_sub_category_1", "46");
        logger.LogInformation("Выбрана категория: Имущественные споры");

        // Ждем загрузки подкатегорий
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#extendedSearch_sub_category_2') !== null",
            new WaitForFunctionOptions { Timeout = 15000 }
        );
        await Task.Delay(2000, cancellationToken);

        // Выбираем подкатегорию: Иски о взыскании сумм по договору займа, кредитному договору
        await page.SelectAsync("#extendedSearch_sub_category_2", "53");
        logger.LogInformation("Выбрана подкатегория: Иски о взыскании сумм по договору займа, кредитному договору");
    
        await Task.Delay(2000, cancellationToken);
    }

    private async Task PerformSearch(IPage page, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Нажимаем кнопку поиска...");
        
            // Загрузки кнопки
            await page.WaitForSelectorAsync("#extendedSearch_search", new WaitForSelectorOptions 
            { 
                Timeout = 10000,
                Visible = true 
            });
        
            await Task.Delay(2000, cancellationToken);
        
            // Используем простой клик без ожидания навигации
            await page.EvaluateExpressionAsync(@"
            document.querySelector('#extendedSearch_search').click();");
        
            logger.LogInformation("Кнопка поиска нажата, ждем результатов...");
        
            // Ждем загрузки результатов с увеличенным таймаутом
            try
            {
                await page.WaitForSelectorAsync("table.table-bordered", new WaitForSelectorOptions 
                { 
                    Timeout = 45000,
                    Visible = true 
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Не удалось найти таблицу результатов: {ex.Message}");
                // Пробуем альтернативный селектор
                await page.WaitForSelectorAsync("table", new WaitForSelectorOptions 
                { 
                    Timeout = 10000 
                });
            }
        
            await Task.Delay(3000, cancellationToken);
        
            logger.LogInformation("Результаты поиска загружены");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при выполнении поиска");
            throw;
        }
    }
    [Obsolete("Obsolete")]
    private async Task CheckDecisionsForCases(IPage page, List<CourtCase> cases, CancellationToken cancellationToken)
    {
        logger.LogInformation("Начинаем проверку решений для {Count} дел", cases.Count);
    
        var casesToCheck = cases.ToList();
    
        foreach (var courtCase in casesToCheck)
        {
            if (!string.IsNullOrEmpty(courtCase.Link))
            {
                // Убираем передачу DecisionDate
                await decisionService.CheckAndExtractDecisionAsync(page, courtCase, cancellationToken);
                await Task.Delay(3000, cancellationToken);
            }
        }
    
        logger.LogInformation("Проверка решений завершена. Найдено решений: {Count}", 
            casesToCheck.Count(c => c.HasDecision));
    }

    private async Task ParseAdditionalPagesAsync(IPage page, List<CourtCase> allCases, CancellationToken cancellationToken)
    {
        try
        {
            for (int currentPage = 2; currentPage <= MaxPages; currentPage++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                logger.LogInformation("Проверяем наличие страницы {Page}...", currentPage);

                // Ищем пагинацию
                var pagination = await page.QuerySelectorAsync(".pagination");
                if (pagination == null)
                {
                    logger.LogWarning("Пагинация не найдена на странице {Page}. Завершение.", currentPage);
                    break;
                }

                // Ищем ссылку на конкретную страницу
                var nextPageLink = await pagination.QuerySelectorAsync($"a[href*='page={currentPage}']");
                if (nextPageLink == null)
                {
                    logger.LogInformation("Ссылка на страницу {Page} не найдена. Завершение пагинации.", currentPage);
                    break;
                }

                logger.LogInformation("Переходим на страницу {Page}...", currentPage);

                // для перехода на следующую страницу
                var navigationTask = page.WaitForNavigationAsync(new NavigationOptions
                {
                    WaitUntil = [WaitUntilNavigation.Networkidle2],
                    Timeout = 30000
                });

                await nextPageLink.ClickAsync();
        
                // Ждем загрузки новой страницы
                await navigationTask;

                await Task.Delay(3000, cancellationToken);

                // Парсим текущую страницу
                var pageCases = await searchParser.ParseSearchResultsWithRetry(page);
        
                if (pageCases.Count == 0)
                {
                    logger.LogWarning("На странице {Page} не найдено дел. Завершение.", currentPage);
                    break;
                }

                // Просто добавляем все дела
                allCases.AddRange(pageCases);
                logger.LogInformation("Спарсено дел на странице {Page}: {Count}", currentPage, pageCases.Count);

                await Task.Delay(2000, cancellationToken);
            }

            logger.LogInformation("Пагинация завершена. Обработано страниц: {Pages}", MaxPages);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Пагинация прервана");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при парсинге дополнительных страниц");
        }
    }
    
    private async Task<bool> CheckRegionSelectionAvailability(IPage page)
    {
        try
        {
            // Проверяем различные элементы, которые могут указывать на доступность выбора регионов
            var indicators = await page.QuerySelectorAllAsync(
                "[class*='court'], [class*='region'], button, a, input[type='button']");
        
            return indicators.Length > 0;
        }
        catch
        {
            return false;
        }
    }
}