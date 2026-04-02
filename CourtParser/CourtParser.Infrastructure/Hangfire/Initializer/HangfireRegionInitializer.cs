using CourtParser.Common.Interfaces;
using CourtParser.Models.Regions;
using Hangfire;
using Hangfire.Storage;

namespace CourtParser.Infrastructure.Hangfire.Initializer;

public static class HangfireRegionInitializer
{
    private static volatile bool _initialized;
    private static readonly object Lock = new();
    
    [Obsolete("Obsolete")]
    public static void ScheduleRegionJobs()
    {
        // Первая проверка без блокировки
        lock (Lock)
        {
            if (_initialized)
            {
                Console.WriteLine("Hangfire задачи уже инициализированы, пропускаем...");
                return;
            }
        }

        lock (Lock)
        {
            // Вторая проверка внутри блокировки
            if (_initialized)
            {
                Console.WriteLine("Hangfire задачи уже инициализированы (второй поток), пропускаем...");
                return;
            }
            
            Console.WriteLine("Регистрация Hangfire задач...");
            
            // Очищаем старые задачи перед добавлением новых
            CleanupOldJobs();
            
            var allRegions = RussianRegions.GetAllRegions();
            
            foreach (var region in allRegions)
            {
                BackgroundJob.Enqueue<IRegionJobService>(
                    x => x.ProcessRegionAsync(region)
                );
            }
            
            // Используем volatile запись
            _initialized = true;
            
            Console.WriteLine($"Зарегистрировано {allRegions.Count} задач");
            Console.WriteLine($"Initialized at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }
    }
    
    [Obsolete("Obsolete")]
    private static void CleanupOldJobs()
    {
        try
        {
            using var connection = JobStorage.Current.GetConnection();
            
            // Очищаем все enqueued задачи для IRegionJobService
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
            
            // Очищаем recurring задачи
            var recurringJobs = connection.GetRecurringJobs();
            foreach (var job in recurringJobs)
            {
                if (job.Id.Contains("region") || job.Job?.Type == typeof(IRegionJobService))
                {
                    RecurringJob.RemoveIfExists(job.Id);
                }
            }
            
            // Очищаем scheduled задачи
            var scheduledJobs = monitoringApi.ScheduledJobs(0, 1000);
            foreach (var job in scheduledJobs)
            {
                if (job.Value.Job.Type == typeof(IRegionJobService))
                {
                    BackgroundJob.Delete(job.Key);
                }
            }
            
            // Очищаем processing задачи
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
            // Продолжаем выполнение, даже если очистка не удалась
        }
    }
}