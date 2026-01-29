namespace ovudb.SystemDatabase.Models;

/// <summary>
/// System table time_zone_transition - time zone transitions
/// </summary>
public class SystemTimeZoneTransition
{
    public int Id { get; set; }
    public int TimeZoneId { get; set; }
    public long TransitionTime { get; set; } // Unix timestamp
    public int TransitionTypeId { get; set; }
}
