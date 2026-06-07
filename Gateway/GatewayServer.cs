using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;



public class GatewayServer
{
    private const string ExchangeName = "sensor_data";
    private readonly HttpClient httpClient = new HttpClient();

    public CsvManager csvManager;
    public GatewayServer()
    {
        // Carrega a lista de sensores autorizados.
        csvManager = new CsvManager();
        csvManager.Load(ResolveSensorsPath());
    }

    public async Task StartAsync()
    {
        // Inicia a monitorização dos sensores registados.
        new Thread(MonitorSensors).Start();

        var factory = new ConnectionFactory
        {
            HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost",
            UserName = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "guest",
            Password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "guest"
        };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true);

        var queue = await channel.QueueDeclareAsync(
            queue: "gateway.sensor_data",
            durable: true,
            exclusive: false,
            autoDelete: false);

        await channel.QueueBindAsync(queue.QueueName, ExchangeName, "sensor.#");

        // Consome todas as mensagens publicadas pelos sensores.
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            string message = Encoding.UTF8.GetString(ea.Body.ToArray());

            try
            {
                await ProcessRabbitMessage(message);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erro ao processar mensagem RabbitMQ: " + ex.Message);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        await channel.BasicConsumeAsync(queue.QueueName, autoAck: false, consumer);

        Console.WriteLine("Gateway iniciado. A subscrever tópicos sensor.# no RabbitMQ...");
        await Task.Delay(Timeout.Infinite);
    }

    private void MonitorSensors()
    {
        // Mostra sensores que não enviam dados há algum tempo.
        while (true)
        {
            foreach (var sensor in csvManager.Sensores.Values)
            {
                if ((DateTime.Now - sensor.LastSync).TotalSeconds > 30)
                {
                    Console.WriteLine($"Sensor OFFLINE: {sensor.Id}");
                }
            }

            Thread.Sleep(10000);
        }
    }

    private static string ResolveSensorsPath()
    {
        // Procura o CSV de sensores no output ou na pasta do projeto.
        string outputPath = Path.Combine(AppContext.BaseDirectory, "sensores.csv");
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        string projectPath = Path.Combine(Directory.GetCurrentDirectory(), "Gateway", "sensores.csv");
        if (File.Exists(projectPath))
        {
            return projectPath;
        }

        return "sensores.csv";
    }

    private async Task ProcessRabbitMessage(string message)
    {
        // Valida a leitura recebida antes de a enviar para processamento.
        Console.WriteLine("Recebido do RabbitMQ: " + message);

        var reading = JsonSerializer.Deserialize<SensorReading>(message, JsonOptions());
        if (reading == null)
        {
            Console.WriteLine("Mensagem inválida.");
            return;
        }

        Sensor? sensor;
        lock (SensorHandler.LockObj)
        {
            sensor = csvManager.GetSensor(reading.SensorId);
        }

        if (sensor == null || sensor.Estado != "ativo")
        {
            Console.WriteLine($"Sensor inválido ou inativo: {reading.SensorId}");
            return;
        }

        bool tipoValido = sensor.TiposDados
            .Select(NormalizeDataType)
            .Contains(NormalizeDataType(reading.Tipo));

        if (!tipoValido)
        {
            Console.WriteLine($"Tipo inválido bloqueado: {reading.Tipo}");
            return;
        }

        lock (SensorHandler.LockObj)
        {
            sensor.LastSync = DateTime.Now;
        }

        var normalized = await CallPreProcessingRpc(reading);
        await SendToServer(normalized);
    }

    private async Task<NormalizedReading> CallPreProcessingRpc(SensorReading reading)
    {
        // Chama o serviço RPC que normaliza os dados do sensor.
        string url = Environment.GetEnvironmentVariable("PREPROCESSING_URL") ?? "http://localhost:7001/rpc/normalize";
        using var response = await httpClient.PostAsJsonAsync(url, reading);
        response.EnsureSuccessStatusCode();

        var normalized = await response.Content.ReadFromJsonAsync<NormalizedReading>(JsonOptions());
        if (normalized == null)
        {
            throw new InvalidOperationException("Resposta vazia do serviço de pré-processamento.");
        }

        Console.WriteLine($"Pré-processado por RPC: {normalized.SensorId} | {normalized.Zona} | {normalized.Tipo} | {normalized.Valor}");
        return normalized;
    }

    private static async Task SendToServer(NormalizedReading reading)
    {
        // Envia a leitura normalizada para o Servidor.
        try
        {
            using TcpClient serverClient = new TcpClient();
            await serverClient.ConnectAsync("127.0.0.1", 6000);
            await using NetworkStream stream = serverClient.GetStream();

            string msg = JsonSerializer.Serialize(reading, JsonOptions()) + "\n";
            byte[] data = Encoding.UTF8.GetBytes(msg);
            await stream.WriteAsync(data);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Erro ao enviar para servidor: " + ex.Message);
        }
    }

    private static string NormalizeDataType(string value)
    {
        return value.Trim().ToUpperInvariant().Replace(".", "");
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    private record SensorReading(
        string SensorId,
        string Zona,
        string Tipo,
        string Valor,
        string FormatoOrigem,
        DateTime Timestamp);

    private record NormalizedReading(
        string SensorId,
        string Zona,
        string Tipo,
        double Valor,
        string Unidade,
        string FormatoOrigem,
        DateTime TimestampOriginal,
        DateTime TimestampProcessamento);
}
