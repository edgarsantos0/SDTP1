using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

public class CsvManager
{
    public Dictionary<string, Sensor> Sensores { get; private set; }

    public CsvManager()
    {
        Sensores = new Dictionary<string, Sensor>();
    }

    public void Load(string path)
    {
        var lines = File.ReadAllLines(path);

        foreach (var line in lines)
        {
            var parts = line.Split(':');

            Sensor s = new Sensor
            {
                Id = parts[0],
                Estado = parts[1],
                Zona = parts[2],
                TiposDados = parts[3]
                    .Replace("[", "")
                    .Replace("]", "")
                    .Split(',')
                    .ToList(),
                LastSync = DateTime.TryParse(parts[4], out DateTime dt) ? dt : DateTime.Now
            };

            Sensores[s.Id] = s;
        }
    }

    public Sensor? GetSensor(string id)
    {
        return Sensores.TryGetValue(id, out Sensor? sensor) ? sensor : null;
    }
}