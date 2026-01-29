namespace ovudb.SystemDatabase.Models;

/// <summary>
/// System table time_zone_name - time zone names
/// </summary>
public class SystemTimeZoneName
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TimeZoneId { get; set; }
}
