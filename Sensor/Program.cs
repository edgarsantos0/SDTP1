using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

class Program
{
    private const string ExchangeName = "sensor_data";

    static async Task Main(string[] args)
    {
        // Cria a ligação ao RabbitMQ para publicar leituras do sensor.
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
            // Publica uma única leitura quando o Sensor é chamado pela interface.
            await PublishReading(channel, args[1], args[2], args[3], args[4]);
            return;
        }

        if (args.Length >= 3 && args[0] == "--auto")
        {
            // Executa uma simulação automática sem pedir dados no terminal.
            int count = args.Length >= 4 && int.TryParse(args[3], out int parsedCount) ? parsedCount : 10;
            int delayMs = args.Length >= 5 && int.TryParse(args[4], out int parsedDelay) ? parsedDelay : 1000;
            await RunAutomaticSimulation(channel, args[1], args[2], count, delayMs);
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
        await RunAutomaticSimulation(channel, sensorId, zona, count: 10, delayMs: 1000);
    }

    private static async Task RunAutomaticSimulation(IChannel channel, string sensorId, string zona, int count, int delayMs)
    {
        // Gera leituras de teste para simular um sensor real.
        var random = new Random();

        for (int i = 0; i < count; i++)
        {
            await PublishReading(channel, sensorId, zona, "PM2.5", random.Next(5, 95).ToString());
            await PublishReading(channel, sensorId, zona, "TEMP", random.Next(12, 35).ToString());
            await Task.Delay(delayMs);
        }
    }

    private static async Task PublishReading(IChannel channel, string sensorId, string zona, string tipo, string valor)
    {
        // Monta a mensagem e publica no tópico correspondente à zona e ao tipo.
        var reading = new SensorReading(
            SensorId: sensorId.Trim().ToUpperInvariant(),
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