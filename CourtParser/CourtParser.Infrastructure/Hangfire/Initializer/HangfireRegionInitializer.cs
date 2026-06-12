using CourtParser.Common.Interfaces;
using CourtParser.Models.Regions;
using Hangfire;
using Hangfire.Storage;

namespace CourtParser.Infrastructure.Hangfire.Initializer;

/// <summary>
/// Класс, для инициализации воркера для запуска задач
/// </summary>
public static class HangfireRegionInitializer
{
    private static volatile bool _initialized;
    private static readonly object Lock = new();
    
    /// <summary>
    /// Планирует последовательные Hangfire задачи для обработки всех регионов
    /// </summary>
    [Obsolete("Obsolete")]
    public static void ScheduleRegionJobs()
    {
        lock (Lock)
        {
            if (_initialized)
            {
                Console.WriteLine("Hangfire задачи уже инициализированы, пропускаем...");
                return;
            }
        
            Console.WriteLine("Регистрация Hangfire задач...");
        
            CleanupOldJobs();
        
            var allRegions = RussianRegions.GetAllRegions();
        
            if (allRegions.Count == 0) return;
        
            string? lastJobId = BackgroundJob.Enqueue<IRegionJobService>(
                x => x.ProcessRegionAsync(allRegions[0])
            );
        
            for (var i = 1; i < allRegions.Count; i++)
            {
                var region = allRegions[i];
                var previousJobId = lastJobId;
            
                lastJobId = BackgroundJob.ContinueJobWith<IRegionJobService>(
                    previousJobId,
                    x => x.ProcessRegionAsync(region)
                );
            }
        
            _initialized = true;
        
            Console.WriteLine($"Зарегистрировано {allRegions.Count} задач (последовательно)");
            Console.WriteLine($"Initialized at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }
    }
    
    /// <summary>
    /// Очищает старые Hangfire задачи, связанные с обработкой регионов
    /// </summary>
    [Obsolete("Obsolete")]
    private static void CleanupOldJobs()
    {
        try
        {
            using var connection = JobStorage.Current.GetConnection();
            
            var monitoringApi = JobStorage.Current.GetMonitoringApi();
            var queues = monitoringApi.Queues();
            
            foreach (var queue in queues)
            {
                var enqueuedJobs = monitoringApi.EnqueuedJobs(queue.Name, 0, (int)queue.Length);
                foreach (var job in enqueuedJobs)
                {
                    if (job.Value.Job.Type == typeof(IRegionJobService))
                    {
                        BackgroundJob.Delete(job.Key);
                    }
                }
            }
            
            var recurringJobs = connection.GetRecurringJobs();
            foreach (var job in recurringJobs)
            {
                if (job.Id.Contains("region") || job.Job?.Type == typeof(IRegionJobService))
                {
                    RecurringJob.RemoveIfExists(job.Id);
                }
            }
            
            var scheduledJobs = monitoringApi.ScheduledJobs(0, 1000);
            foreach (var job in scheduledJobs)
            {
                if (job.Value.Job.Type == typeof(IRegionJobService))
                {
                    BackgroundJob.Delete(job.Key);
                }
            }
            
            var processingJobs = monitoringApi.ProcessingJobs(0, 1000);
            foreach (var job in processingJobs)
            {
                if (job.Value.Job.Type == typeof(IRegionJobService))
                {
                    BackgroundJob.Delete(job.Key);
                }
            }
            
        }
        catch (Exception)
        {
            // 
        }
    }
}