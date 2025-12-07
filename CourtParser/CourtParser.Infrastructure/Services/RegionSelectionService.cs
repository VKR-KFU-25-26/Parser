using Microsoft.Extensions.Logging;
using PuppeteerSharp;

namespace CourtParser.Infrastructure.Services;

/// <summary>
/// Выбор региона
/// </summary>
/// <param name="logger"></param>
public class RegionSelectionService(ILogger<RegionSelectionService> logger)
{
   [Obsolete("Obsolete")]
   public async Task SelectRegionsAsync(IPage page, List<string> regionsToSelect)
    {
        try
        {
            logger.LogInformation("Пытаемся выбрать регионы: {Regions}", string.Join(", ", regionsToSelect));

            // 1. Находим и открываем поле выбора судов/регионов
            var fieldFound = await FindAndOpenCourtField(page);
            if (!fieldFound)
            {
                logger.LogWarning("❌ Поле выбора судов не найдено");
                return;
            }

            // 2. Ждем загрузки модального окна с деревом
            var modalLoaded = await WaitForRegionModalAsync(page);
            if (!modalLoaded)
            {
                logger.LogWarning("❌ Модальное окно с деревом регионов не загрузилось");
                return;
            }

            // 3. Работаем с деревом регионов
            await ProcessRegionTree(page, regionsToSelect);

            logger.LogInformation("✅ Выбор регионов завершен");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Ошибка при выборе регионов");
        }
    }

    private async Task<bool> FindAndOpenCourtField(IPage page)
    {
        try
        {
            logger.LogInformation("Ищем поле выбора судов/регионов...");

            // Сначала пробуем найти дерево по ID
            var treeDiv = await page.QuerySelectorAsync("#tree");
            if (treeDiv != null)
            {
                logger.LogInformation("✅ Найдено дерево с ID 'tree'");
                await treeDiv.ClickAsync();
                await Task.Delay(3000);
                return true;
            }

            // Ищем по классам дерева
            var treeByClass = await page.QuerySelectorAsync(".aciTree");
            if (treeByClass != null)
            {
                logger.LogInformation("✅ Найдено дерево с классом 'aciTree'");
                await treeByClass.ClickAsync();
                await Task.Delay(3000);
                return true;
            }

            logger.LogWarning("❌ Поле выбора судов не найдено");
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при поиске поля выбора регионов");
            return false;
        }
    }

    private async Task<bool> WaitForRegionModalAsync(IPage page)
    {
        try
        {
            logger.LogInformation("Ждем загрузки модального окна с деревом регионов...");

            // Ждем появления дерева aciTree
            await page.WaitForSelectorAsync(".aciTree", new WaitForSelectorOptions 
            { 
                Timeout = 15000,
                Visible = true
            });

            logger.LogInformation("✅ Дерево регионов загружено");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning("❌ Дерево регионов не загрузилось: {Message}", ex.Message);
            return false;
        }
    }

    [Obsolete("Obsolete")]
    private async Task ProcessRegionTree(IPage page, List<string> regionsToSelect)
    {
        try
        {
            logger.LogInformation("Начинаем обработку дерева регионов...");

            // Группируем регионы по федеральным округам
            var regionsByDistrict = GroupRegionsByFederalDistrict(regionsToSelect);
            
            // Обрабатываем каждый федеральный округ
            foreach (var (districtName, regionsInDistrict) in regionsByDistrict)
            {
                if (regionsInDistrict.Any())
                {
                    await ProcessFederalDistrict(page, districtName, regionsInDistrict);
                }
            }

            // Подтверждаем выбор
            await ConfirmSelection(page);

            logger.LogInformation("✅ Обработка дерева регионов завершена");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при обработке дерева регионов");
        }
    }

    private Dictionary<string, List<string>> GroupRegionsByFederalDistrict(List<string> regions)
    {
        var result = new Dictionary<string, List<string>>();
        
        foreach (var region in regions)
        {
            var district = GetFederalDistrictForRegion(region);
            if (!result.ContainsKey(district))
            {
                result[district] = [];
            }
            result[district].Add(region);
        }
        
        logger.LogInformation("Регионы сгруппированы по округам: {Grouping}", 
            string.Join("; ", result.Select(x => $"{x.Key}: {x.Value.Count}")));
            
        return result;
    }

    private string GetFederalDistrictForRegion(string regionName)
    {
        // Определяем федеральный округ для региона
        var districtMap = new Dictionary<string, string>
        {
            // Центральный ФО
            ["Москва"] = "Центральный федеральный округ",
            ["Московская область"] = "Центральный федеральный округ",
            ["Белгородская область"] = "Центральный федеральный округ",
            ["Брянская область"] = "Центральный федеральный округ",
            ["Владимирская область"] = "Центральный федеральный округ",
            ["Воронежская область"] = "Центральный федеральный округ",
            ["Ивановская область"] = "Центральный федеральный округ",
            ["Калужская область"] = "Центральный федеральный округ",
            ["Костромская область"] = "Центральный федеральный округ",
            ["Курская область"] = "Центральный федеральный округ",
            ["Липецкая область"] = "Центральный федеральный округ",
            ["Орловская область"] = "Центральный федеральный округ",
            ["Рязанская область"] = "Центральный федеральный округ",
            ["Смоленская область"] = "Центральный федеральный округ",
            ["Тамбовская область"] = "Центральный федеральный округ",
            ["Тверская область"] = "Центральный федеральный округ",
            ["Тульская область"] = "Центральный федеральный округ",
            ["Ярославская область"] = "Центральный федеральный округ",

            // Северо-Западный ФО
            ["Санкт-Петербург"] = "Северо-Западный федеральный округ",
            ["Ленинградская область"] = "Северо-Западный федеральный округ",
            ["Архангельская область"] = "Северо-Западный федеральный округ",
            ["Вологодская область"] = "Северо-Западный федеральный округ",
            ["Калининградская область"] = "Северо-Западный федеральный округ",
            ["Республика Карелия"] = "Северо-Западный федеральный округ",
            ["Республика Коми"] = "Северо-Западный федеральный округ",
            ["Мурманская область"] = "Северо-Западный федеральный округ",
            ["Ненецкий автономный округ"] = "Северо-Западный федеральный округ",
            ["Новгородская область"] = "Северо-Западный федеральный округ",
            ["Псковская область"] = "Северо-Западный федеральный округ",

            // Южный ФО
            ["Республика Адыгея"] = "Южный федеральный округ",
            ["Астраханская область"] = "Южный федеральный округ",
            ["Волгоградская область"] = "Южный федеральный округ",
            ["Республика Калмыкия"] = "Южный федеральный округ",
            ["Краснодарский край"] = "Южный федеральный округ",
            ["Ростовская область"] = "Южный федеральный округ",

            // Северо-Кавказский ФО
            ["Республика Дагестан"] = "Северо-Кавказский федеральный округ",
            ["Республика Ингушетия"] = "Северо-Кавказский федеральный округ",
            ["Кабардино-Балкарская Республика"] = "Северо-Кавказский федеральный округ",
            ["Карачаево-Черкесская Республика"] = "Северо-Кавказский федеральный округ",
            ["Республика Северная Осетия-Алания"] = "Северо-Кавказский федеральный округ",
            ["Чеченская Республика"] = "Северо-Кавказский федеральный округ",
            ["Ставропольский край"] = "Северо-Кавказский федеральный округ",

            // Приволжский ФО
            ["Республика Башкортостан"] = "Приволжский федеральный округ",
            ["Кировская область"] = "Приволжский федеральный округ",
            ["Республика Марий Эл"] = "Приволжский федеральный округ",
            ["Республика Мордовия"] = "Приволжский федеральный округ",
            ["Нижегородская область"] = "Приволжский федеральный округ",
            ["Оренбургская область"] = "Приволжский федеральный округ",
            ["Пензенская область"] = "Приволжский федеральный округ",
            ["Пермский край"] = "Приволжский федеральный округ",
            ["Самарская область"] = "Приволжский федеральный округ",
            ["Саратовская область"] = "Приволжский федеральный округ",
            ["Республика Татарстан"] = "Приволжский федеральный округ",
            ["Удмуртская Республика"] = "Приволжский федеральный округ",
            ["Ульяновская область"] = "Приволжский федеральный округ",
            ["Чувашская Республика"] = "Приволжский федеральный округ",

            // Уральский ФО
            ["Курганская область"] = "Уральский федеральный округ",
            ["Свердловская область"] = "Уральский федеральный округ",
            ["Тюменская область"] = "Уральский федеральный округ",
            ["Челябинская область"] = "Уральский федеральный округ",
            ["Ханты-Мансийский автономный округ - Югра"] = "Уральский федеральный округ",
            ["Ямало-Ненецкий автономный округ"] = "Уральский федеральный округ",

            // Сибирский ФО
            ["Республика Алтай"] = "Сибирский федеральный округ",
            ["Алтайский край"] = "Сибирский федеральный округ",
            ["Иркутская область"] = "Сибирский федеральный округ",
            ["Кемеровская область"] = "Сибирский федеральный округ",
            ["Красноярский край"] = "Сибирский федеральный округ",
            ["Новосибирская область"] = "Сибирский федеральный округ",
            ["Омская область"] = "Сибирский федеральный округ",
            ["Томская область"] = "Сибирский федеральный округ",
            ["Республика Тыва"] = "Сибирский федеральный округ",
            ["Республика Хакасия"] = "Сибирский федеральный округ",
            ["Забайкальский край"] = "Сибирский федеральный округ",

            // Дальневосточный ФО
            ["Амурская область"] = "Дальневосточный федеральный округ",
            ["Еврейская автономная область"] = "Дальневосточный федеральный округ",
            ["Камчатский край"] = "Дальневосточный федеральный округ",
            ["Магаданская область"] = "Дальневосточный федеральный округ",
            ["Приморский край"] = "Дальневосточный федеральный округ",
            ["Республика Саха (Якутия)"] = "Дальневосточный федеральный округ",
            ["Сахалинская область"] = "Дальневосточный федеральный округ",
            ["Хабаровский край"] = "Дальневосточный федеральный округ",
            ["Чукотский автономный округ"] = "Дальневосточный федеральный округ",

            // Крымский ФО
            ["Республика Крым"] = "Крымский федеральный округ",
            ["Севастополь"] = "Крымский федеральный округ"
        };

        // Если регион сам является федеральным округом
        var federalDistricts = new[]
        {
            "Центральный федеральный округ",
            "Северо-Западный федеральный округ", 
            "Южный федеральный округ",
            "Северо-Кавказский федеральный округ",
            "Приволжский федеральный округ",
            "Уральский федеральный округ", 
            "Сибирский федеральный округ",
            "Дальневосточный федеральный округ",
            "Крымский федеральный округ"
        };

        if (federalDistricts.Contains(regionName))
        {
            return regionName;
        }

        return districtMap.ContainsKey(regionName) ? districtMap[regionName] : "Приволжский федеральный округ";
    }

    [Obsolete("Obsolete")]
    private async Task ProcessFederalDistrict(IPage page, string districtName, List<string> regions)
    {
        try
        {
            logger.LogInformation("Обрабатываем федеральный округ: {District} с регионами: {Regions}", 
                districtName, string.Join(", ", regions));

            // Ищем федеральный округ
            var districtFound = await FindAndExpandFederalDistrict(page, districtName);
            if (!districtFound)
            {
                logger.LogWarning("❌ Федеральный округ {District} не найден", districtName);
                return;
            }

            // Выбираем регионы внутри округа
            foreach (var regionName in regions)
            {
                await FindAndSelectRegionInDistrict(page, regionName);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при обработке федерального округа {District}", districtName);
        }
    }

    [Obsolete("Obsolete")]
    private async Task<bool> FindAndExpandFederalDistrict(IPage page, string districtName)
    {
        try
        {
            logger.LogInformation("Ищем федеральный округ: {District}", districtName);

            // Пробуем разные варианты названия
            var districtVariants = GetDistrictNameVariants(districtName);
            
            foreach (var variant in districtVariants)
            {
                var districtXpath = $"//span[@class='aciTreeText' and contains(text(), '{variant}')]";
                var districtElements = await page.XPathAsync(districtXpath);

                if (districtElements.Any())
                {
                    var districtElement = districtElements.First();
                    logger.LogInformation("✅ Найден федеральный округ: {Variant}", variant);

                    // Раскрываем округ если нужно
                    await ExpandDistrict(page, districtElement, variant);
                    return true;
                }
            }

            logger.LogWarning("❌ Федеральный округ {District} не найден", districtName);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при поиске федерального округа {District}", districtName);
            return false;
        }
    }

    private string[] GetDistrictNameVariants(string districtName)
    {
        // Возможные варианты названий для каждого округа
        var variantsMap = new Dictionary<string, string[]>
        {
            ["Центральный федеральный округ"] = ["Центральный федеральный округ", "Центральный ФО", "ЦФО"],
            ["Северо-Западный федеральный округ"] = ["Северо-Западный федеральный округ", "Северо-Западный ФО", "СЗФО"],
            ["Южный федеральный округ"] = ["Южный федеральный округ", "Южный ФО", "ЮФО"],
            ["Северо-Кавказский федеральный округ"] = ["Северо-Кавказский федеральный округ", "Северо-Кавказский ФО", "СКФО"],
            ["Приволжский федеральный округ"] = ["Приволжский федеральный округ", "Приволжский ФО", "ПФО"],
            ["Уральский федеральный округ"] = ["Уральский федеральный округ", "Уральский ФО", "УФО"],
            ["Сибирский федеральный округ"] = ["Сибирский федеральный округ", "Сибирский ФО", "СФО"],
            ["Дальневосточный федеральный округ"] = ["Дальневосточный федеральный округ", "Дальневосточный ФО", "ДФО"],
            ["Крымский федеральный округ"] = ["Крымский федеральный округ", "Крымский ФО", "КФО"]
        };

        return variantsMap.TryGetValue(districtName, out var value) ? value : [districtName];
    }

    [Obsolete("Obsolete")]
    private async Task ExpandDistrict(IPage page, IElementHandle districtElement, string districtName)
    {
        try
        {
            // Находим кнопку раскрытия
            var expandButtonXpath = "./ancestor::div[@role='treeitem']//span[@class='aciTreeButton']";
            var expandButtons = await districtElement.XPathAsync(expandButtonXpath);

            if (expandButtons.Any())
            {
                var expandButton = expandButtons.First();
                
                await WaitForElementVisibility(expandButton);
                
                // Проверяем состояние через JavaScript
                var isExpanded = await page.EvaluateFunctionAsync<string>(@"
                    (button) => {
                        const lineElement = button.closest('.aciTreeLine');
                        return lineElement ? lineElement.getAttribute('aria-expanded') : 'false';
                    }", expandButton);

                logger.LogInformation("Состояние раскрытия округа {District}: {IsExpanded}", districtName, isExpanded);

                if (isExpanded != "true")
                {
                    await expandButton.EvaluateFunctionAsync("el => el.click()");
                    logger.LogInformation("✅ Раскрыли федеральный округ: {District}", districtName);
                    await Task.Delay(2000); // Ждем загрузки регионов
                }
                else
                {
                    logger.LogInformation("✅ Федеральный округ {District} уже раскрыт", districtName);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при раскрытии округа {District}", districtName);
        }
    }

    [Obsolete("Obsolete")]
    private async Task FindAndSelectRegionInDistrict(IPage page, string regionName)
    {
        try
        {
            logger.LogInformation("Ищем регион: {RegionName}", regionName);

            // Ищем регион по точному тексту
            var xpath = $"//span[@class='aciTreeText' and normalize-space()='{regionName}']";
            var regionElements = await page.XPathAsync(xpath);

            if (!regionElements.Any())
            {
                logger.LogWarning("Регион {RegionName} не найден по точному совпадению", regionName);
                
                // Пробуем частичное совпадение
                xpath = $"//span[@class='aciTreeText' and contains(text(), '{regionName}')]";
                regionElements = await page.XPathAsync(xpath);
            }

            if (regionElements.Any())
            {
                var regionElement = regionElements.First();
                
                // Используем безопасный способ получения текста
                var actualText = await regionElement.EvaluateFunctionAsync<string>("el => el.textContent?.trim()");
                logger.LogInformation("Найден регион: '{ActualText}'", actualText);
                
                // Проверяем состояние через JavaScript
                var isChecked = await page.EvaluateFunctionAsync<string>(@"
                    (element) => {
                        const lineElement = element.closest('.aciTreeLine');
                        return lineElement ? lineElement.getAttribute('aria-checked') : 'false';
                    }", regionElement);

                logger.LogInformation("Состояние чекбокса: {IsChecked}", isChecked);

                if (isChecked != "true")
                {
                    // Выбираем регион
                    await SelectRegionSafe(page, regionElement, regionName);
                }
                else
                {
                    logger.LogInformation("✅ Регион {RegionName} уже выбран", regionName);
                }
            }
            else
            {
                logger.LogWarning("❌ Регион {RegionName} не найден", regionName);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при выборе региона {RegionName}", regionName);
        }
    }

    [Obsolete("Obsolete")]
    private async Task SelectRegionSafe(IPage page, IElementHandle regionElement, string regionName)
    {
        try
        {
            // Пробуем разные стратегии выбора
            
            // 1. Сначала пробуем найти и кликнуть на чекбокс через XPath
            var checkboxXpath = "./ancestor::div[@role='treeitem']//span[@class='aciTreeCheck']";
            var checkboxes = await regionElement.XPathAsync(checkboxXpath);
            
            if (checkboxes.Any())
            {
                var checkbox = checkboxes.First();
                await WaitForElementVisibility(checkbox);
                await checkbox.EvaluateFunctionAsync("el => el.click()");
                logger.LogInformation("✅ Выбран регион {RegionName} через чекбокс", regionName);
                await Task.Delay(500);
                return;
            }

            // 2. Если чекбокса нет, кликаем на текст региона через JavaScript
            await WaitForElementVisibility(regionElement);
            await regionElement.EvaluateFunctionAsync("el => el.click()");
            logger.LogInformation("✅ Выбран регион {RegionName} через текст", regionName);
            
            // Даем время на применение выбора
            await Task.Delay(500);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при выборе региона {RegionName}", regionName);
            
            // Последняя попытка - клик через координаты
            try
            {
                await ClickViaCoordinates(page, regionElement);
                logger.LogInformation("✅ Выбран регион {RegionName} через координаты", regionName);
            }
            catch (Exception coordEx)
            {
                logger.LogError(coordEx, "Не удалось выбрать регион {RegionName} даже через координаты", regionName);
            }
        }
    }

    private async Task WaitForElementVisibility(IElementHandle element)
    {
        try
        {
            // Упрощенная проверка видимости через JavaScript без WaitForFunction
            var isVisible = await element.EvaluateFunctionAsync<bool>(@"
                (element) => {
                    if (!element) return false;
                    const rect = element.getBoundingClientRect();
                    return element.offsetParent !== null && 
                           rect.width > 0 && 
                           rect.height > 0 &&
                           element.disabled !== true;
                }");

            if (!isVisible)
            {
                // Если элемент не видим, ждем немного и проверяем снова
                await Task.Delay(500);
                
                isVisible = await element.EvaluateFunctionAsync<bool>(@"
                    (element) => {
                        if (!element) return false;
                        const rect = element.getBoundingClientRect();
                        return element.offsetParent !== null && 
                               rect.width > 0 && 
                               rect.height > 0 &&
                               element.disabled !== true;
                    }");
            }

            if (!isVisible)
            {
                logger.LogWarning("Элемент не видим или недоступен для клика");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при проверке видимости элемента");
            // Продолжаем выполнение, так как это не критическая ошибка
        }
    }

    private async Task ClickViaCoordinates(IPage page, IElementHandle element)
    {
        try
        {
            // Получаем координаты элемента
            var boundingBox = await element.BoundingBoxAsync();
            if (boundingBox != null)
            {
                // Кликаем в центр элемента
                await page.Mouse.ClickAsync(
                    boundingBox.X + boundingBox.Width / 2,
                    boundingBox.Y + boundingBox.Height / 2
                );
                await Task.Delay(200);
            }
            else
            {
                throw new Exception("Не удалось получить координаты элемента");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при клике через координаты");
            throw;
        }
    }

    [Obsolete("Obsolete")]
    private async Task ConfirmSelection(IPage page)
    {
        try
        {
            logger.LogInformation("Подтверждаем выбор регионов...");

            // Ищем кнопки подтверждения
            var confirmSelectors = new[]
            {
                "button.btn-primary",
                "button.btn-success",
                "input[type='submit']",
                "button[type='submit']"
            };

            foreach (var selector in confirmSelectors)
            {
                var button = await page.QuerySelectorAsync(selector);
                if (button != null)
                {
                    // Используем безопасную проверку видимости
                    var isVisible = await button.EvaluateFunctionAsync<bool>(
                        "el => el.offsetParent !== null && " +
                        "el.getBoundingClientRect().width > 0 && " +
                        "el.getBoundingClientRect().height > 0");

                    if (isVisible)
                    {
                        await button.EvaluateFunctionAsync("el => el.click()");
                        logger.LogInformation("✅ Подтвердили выбор: {Selector}", selector);
                        await Task.Delay(2000);
                        return;
                    }
                }
            }

            // Ищем по тексту
            var textButtons = new[] { "Применить", "Выбрать", "OK", "Сохранить", "Готово" };
            foreach (var text in textButtons)
            {
                var xpath = $"//button[contains(text(), '{text}')]";
                var buttons = await page.XPathAsync(xpath);
                if (buttons.Any())
                {
                    var button = buttons.First();
                    await button.EvaluateFunctionAsync("el => el.click()");
                    logger.LogInformation("✅ Подтвердили выбор: кнопка '{Text}'", text);
                    await Task.Delay(2000);
                    return;
                }
            }

            // Если кнопку не нашли, пробуем кликнуть вне модального окна
            await page.ClickAsync("body");
            logger.LogInformation("✅ Закрыли модальное окно кликом вне его");
            await Task.Delay(1000);

            // Альтернатива - нажатие клавиши Escape
            await page.Keyboard.PressAsync("Escape");
            logger.LogInformation("✅ Закрыли модальное окно клавишей Escape");
            await Task.Delay(1000);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при подтверждении выбора");
        }
    }
}