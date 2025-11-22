namespace Maps.Models;

public class Locality
{
    public string Name { get; set; } = "";
    public PointF UtmCoord { get; set; }

    public Locality(string name, float easting, float northing)
    {
        Name = name;
        UtmCoord = new PointF(easting, northing);
    }
}