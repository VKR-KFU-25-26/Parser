using System.Text;
using CourtParser.Common.Interfaces;
using CourtParser.Infrastructure;
using CourtParser.Infrastructure.Hangfire.Initializer;
using CourtParser.Infrastructure.Hangfire.Services;
using CourtParser.Worker;
using Hangfire;
using Hangfire.Dashboard.BasicAuthorization;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Builder;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

//builder.Services.AddHostedService<Mock>();
// Hangfire + PostgreSQL
builder.Services.AddHangfire(config =>
{
#pragma warning disable CS0618 // Type or member is obsolete
    config.UsePostgreSqlStorage(builder.Configuration.GetConnectionString("DefaultConnection"));
#pragma warning restore CS0618 // Type or member is obsolete
});
builder.Services.AddHangfireServer();

builder.Services.AddScoped<IRegionJobService, RegionJobService>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();

#pragma warning disable ASP0014
app.UseEndpoints(endpoints =>
{
    // Hangfire Dashboard с базовой авторизацией
    endpoints.MapHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization =
        [
            new BasicAuthAuthorizationFilter(new BasicAuthAuthorizationFilterOptions
            {
                RequireSsl = false,
                SslRedirect = false,
                LoginCaseSensitive = true,
                Users =
                [
                    new BasicAuthAuthorizationUser
                    {
                        Login = "admin",
                        PasswordClear = "admin"
                    }
                ]
            })
        ]
    });
});
#pragma warning restore ASP0014

using (app.Services.CreateScope())
{
    Console.WriteLine("📅 Регистрация Hangfire задач...");
#pragma warning disable CS0618 // Type or member is obsolete
    HangfireRegionInitializer.ScheduleRegionJobs();
#pragma warning restore CS0618 // Type or member is obsolete
}

app.Run("http://0.0.0.0:5000");