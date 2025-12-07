namespace CourtParser.Common.Options;

public class WorkerOptions
{
    public List<string> Regions { get; set; } = [];
    public int CasesPerPage { get; set; } = 50;
    public int MaxPagesPerRegion { get; set; } = 10;
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan DelayBetweenRegions { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan DelayBetweenPages { get; set; } = TimeSpan.FromSeconds(5);
}