namespace Maps.Models;

public class UserSettings
{
    public string? SelectedPosition { get; set; }
    public string? SelectedPilot { get; set; }
    public string? SelectedDrone { get; set; }
    public string? Height { get; set; }
    public string? SelectedTarget { get; set; }
    public List<string>? SelectedLocalCities { get; set; }
}