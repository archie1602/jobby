using Jobby.Core.Models.Dashboard;

namespace Jobby.Dashboard;

public sealed class JobbyDashboardOptions
{
    public TimeSpan? RefreshInterval { get; set; } = TimeSpan.FromSeconds(5);

    public int StaleServerThresholdSeconds { get; init; } = 300;

    public bool ReadOnly { get; set; }

    public string? DefaultTimeZoneId { get; set; }

    public DashboardDateStyle DefaultDateStyle { get; set; } = DashboardDateStyle.Iso;

    public DashboardTimeStyle DefaultTimeStyle { get; set; } = DashboardTimeStyle.H24WithSeconds;

    public DashboardLanguage DefaultLanguage { get; set; } = DashboardLanguage.English;
}