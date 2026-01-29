namespace ovudb.SystemDatabase.Models;

/// <summary>
/// System table time_zone - time zone info
/// </summary>
public class SystemTimeZone
{
    public int Id { get; set; }
    public int TimeZoneId { get; set; }
    public bool UseLeapSeconds { get; set; } = false;
}
