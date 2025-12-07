using CourtParser.Common.Interfaces;
using CourtParser.Models.Regions;
using Hangfire;

namespace CourtParser.Infrastructure.Hangfire.Initializer;

public static class HangfireRegionInitializer
{
    [Obsolete("Obsolete")]
    public static void ScheduleRegionJobs()
    {
        var allRegions = RussianRegions.GetAllRegions();
        
        foreach (var region in allRegions)
        {
            var jobId = $"region_job_{region.Replace(" ", "_")}";
            
            BackgroundJob.Enqueue<IRegionJobService>(
                x => x.ProcessRegionAsync(region)
            );
            
        }
    }
    
    // [Obsolete("Obsolete")]
    // public static void ScheduleRegionJobs()
    // {
    //     Console.WriteLine("🚀 Инициализация циклического парсинга регионов");
    //     
    //     var allRegions = RussianRegions.GetAllRegions();
    //     
    //     // Удаляем старые задачи, если есть
    //     CleanupOldCycleJobs();
    //     
    //     // Запускаем первый цикл
    //     StartInfiniteCycle(allRegions);
    // }
    //
    // [Obsolete("Obsolete")]
    // private static void CleanupOldCycleJobs()
    // {
    //     using var connection = JobStorage.Current.GetConnection();
    //     var recurringJobs = connection.GetRecurringJobs();
    //     
    //     // Удаляем все задачи, связанные с циклами регионов
    //     foreach (var job in recurringJobs)
    //     {
    //         if (job.Id.Contains("region_cycle_") || job.Id.Contains("region_next_cycle"))
    //         {
    //             RecurringJob.RemoveIfExists(job.Id);
    //             Console.WriteLine($"🗑️ Удалена старая задача: {job.Id}");
    //         }
    //     }
    // }
    //
    // [Obsolete("Obsolete")]
    // private static void StartInfiniteCycle(List<string> regions)
    // {
    //     Console.WriteLine($"📋 Всего регионов: {regions.Count}");
    //     
    //     // Запускаем первый регион сразу
    //     BackgroundJob.Enqueue<IRegionJobService>(
    //         x => x.ProcessRegionAsync(regions[0])
    //     );
    //     
    //     Console.WriteLine($"✅ Первый регион '{regions[0]}' запланирован на выполнение");
    //     
    //     // Создаем цепочку для первого цикла
    //     CreateRegionChain(regions, 0, null);
    // }
    //
    // [Obsolete("Obsolete")]
    // private static void CreateRegionChain(List<string> regions, int cycleNumber, string previousJobId = null)
    // {
    //     string currentJobId = null;
    //     
    //     for (int i = 0; i < regions.Count; i++)
    //     {
    //         var region = regions[i];
    //         
    //         if (i == 0 && previousJobId == null)
    //         {
    //             // Первый регион в первом цикле уже запущен
    //             currentJobId = null;
    //             continue;
    //         }
    //         else if (i == 0 && previousJobId != null)
    //         {
    //             // Первый регион в следующем цикле - через сутки после последнего
    //             currentJobId = BackgroundJob.Schedule<IRegionJobService>(
    //                 x => x.ProcessRegionAsync(region),
    //                 TimeSpan.FromHours(24)
    //             );
    //         }
    //         else if (previousJobId != null)
    //         {
    //             // Регионы после первого - с задержкой от предыдущего
    //             currentJobId = BackgroundJob.ContinueJobWith<IRegionJobService>(
    //                 previousJobId,
    //                 x => x.ProcessRegionAsync(region),
    //                 JobContinuationOptions.OnlyOnSucceededState
    //             );
    //         }
    //         else
    //         {
    //             // Для первого цикла используем Schedule с задержкой
    //             var delayMinutes = i * 30;
    //             currentJobId = BackgroundJob.Schedule<IRegionJobService>(
    //                 x => x.ProcessRegionAsync(region),
    //                 TimeSpan.FromMinutes(delayMinutes)
    //             );
    //         }
    //         
    //         Console.WriteLine($"📅 Запланирован регион '{region}' (Цикл {cycleNumber + 1}, позиция {i + 1})");
    //         
    //         previousJobId = currentJobId;
    //     }
    //     
    //     // После планирования всех регионов в цикле, планируем следующий цикл
    //     if (!string.IsNullOrEmpty(currentJobId))
    //     {
    //         ScheduleNextCycle(regions, cycleNumber + 1, currentJobId);
    //     }
    // }
    //
    // [Obsolete("Obsolete")]
    // private static void ScheduleNextCycle(List<string> regions, int nextCycleNumber, string lastJobId)
    // {
    //     Console.WriteLine($"\n🔄 Планирование следующего цикла #{nextCycleNumber + 1}");
    //     
    //     // Создаем новую задачу для следующего цикла через сутки
    //     BackgroundJob.ContinueJobWith(
    //         lastJobId,
    //         () => ScheduleNextCycleJob(regions, nextCycleNumber),
    //         JobContinuationOptions.OnlyOnSucceededState
    //     );
    //     
    //     var nextCycleTime = DateTime.Now.AddHours(24).AddMinutes(regions.Count * 30);
    //     Console.WriteLine($"⏱️  Следующий цикл запланирован на: {nextCycleTime:dd.MM.yyyy HH:mm}");
    // }
    //
    // [Obsolete("Obsolete")]
    // public static void ScheduleNextCycleJob(List<string> regions, int cycleNumber)
    // {
    //     // Этот метод вызывается Hangfire и должен быть public
    //     Console.WriteLine($"\n🔄 Запуск цикла #{cycleNumber + 1}");
    //     
    //     // Запускаем первый регион нового цикла
    //     var firstJobId = BackgroundJob.Enqueue<IRegionJobService>(
    //         x => x.ProcessRegionAsync(regions[0])
    //     );
    //     
    //     Console.WriteLine($"✅ Первый регион '{regions[0]}' запланирован на выполнение");
    //     
    //     // Создаем цепочку для нового цикла
    //     CreateRegionChain(regions, cycleNumber, firstJobId);
    // }
}