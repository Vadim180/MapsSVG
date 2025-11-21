namespace Maps.Models;

public class FlightLogEntry
{
    public string? Pilot { get; set; }
    public string? Dron { get; set; }
    public string? Position { get; set; }
    public DateTime EndTime { get; set; }
    public int Distance { get; set; }
}
