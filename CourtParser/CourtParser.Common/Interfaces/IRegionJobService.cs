namespace CourtParser.Common.Interfaces;

public interface IRegionJobService
{
    Task ProcessRegionAsync(string regionName);
}