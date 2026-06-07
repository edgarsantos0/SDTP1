using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

class Program
{
    private const string ExchangeName = "sensor_data";

    static async Task Main(string[] args)
    {
        var factory = new ConnectionFactory
        {
            HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost",
            UserName = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "guest",
            Password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "guest"
        };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true);

        if (args.Length >= 5 && args[0] == "--once")
        {
            await PublishReading(channel, args[1], args[2], args[3], args[4]);
            return;
        }

        Console.Write("Introduz o ID do sensor: ");
        string sensorId = Console.ReadLine()?.Trim() ?? "S102";

        Console.Write("Introduz a zona (ENTER = ZONA_ESCOLAR): ");
        string zona = Console.ReadLine()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(zona))
        {
            zona = "ZONA_ESCOLAR";
        }

        Console.WriteLine("Sensor ligado ao RabbitMQ.");
        Console.WriteLine("Escreve leituras no formato: TIPO VALOR (ex: PM2.5 18.4 ou TEMP 22)");
        Console.WriteLine("Comandos: auto, exit");

        while (true)
        {
            string input = Console.ReadLine()?.Trim() ?? "";

            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (input.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                await RunAutomaticSimulation(channel, sensorId, zona);
                continue;
            }

            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
            {
                Console.WriteLine("Formato inválido. Usa: TIPO VALOR");
                continue;
            }

            await PublishReading(channel, sensorId, zona, parts[0], parts[1]);
        }
    }

    private static async Task RunAutomaticSimulation(IChannel channel, string sensorId, string zona)
    {
        var random = new Random();

        for (int i = 0; i < 10; i++)
        {
            await PublishReading(channel, sensorId, zona, "PM2.5", random.Next(5, 95).ToString());
            await PublishReading(channel, sensorId, zona, "TEMP", random.Next(12, 35).ToString());
            await Task.Delay(1000);
        }
    }

    private static async Task PublishReading(IChannel channel, string sensorId, string zona, string tipo, string valor)
    {
        var reading = new SensorReading(
            SensorId: sensorId.Trim(),
            Zona: zona.Trim().ToUpperInvariant(),
            Tipo: tipo.Trim().ToUpperInvariant(),
            Valor: valor.Trim(),
            FormatoOrigem: "JSON",
            Timestamp: DateTime.UtcNow);

        string json = JsonSerializer.Serialize(reading);
        byte[] body = Encoding.UTF8.GetBytes(json);
        string routingKey = $"sensor.{NormalizeTopicPart(reading.Zona)}.{NormalizeTopicPart(reading.Tipo)}";

        await channel.BasicPublishAsync(ExchangeName, routingKey, body);
        Console.WriteLine($"Publicado em {routingKey}: {json}");
    }

    private static string NormalizeTopicPart(string value)
    {
        return value.Trim().ToUpperInvariant().Replace(".", "");
    }

    private record SensorReading(
        string SensorId,
        string Zona,
        string Tipo,
        string Valor,
        string FormatoOrigem,
        DateTime Timestamp);
}