using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

HttpListener listener = new HttpListener();
listener.Prefixes.Add(Environment.GetEnvironmentVariable("PREPROCESSING_BIND") ?? "http://localhost:7001/");
listener.Start();

Console.WriteLine("Serviço RPC de pré-processamento iniciado em http://localhost:7001/rpc/normalize");

// Fica à espera de pedidos RPC do Gateway.
while (true)
{
    var context = await listener.GetContextAsync();
    _ = Task.Run(() => HandleRequest(context));
}

static async Task HandleRequest(HttpListenerContext context)
{
    // Trata pedidos de normalização recebidos por HTTP.
    try
    {
        if (context.Request.HttpMethod != "POST" || context.Request.Url?.AbsolutePath != "/rpc/normalize")
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
            return;
        }

        using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
        string body = await reader.ReadToEndAsync();
        var reading = JsonSerializer.Deserialize<SensorReading>(body, JsonOptions());

        if (reading == null)
        {
            await WriteJson(context.Response, new { error = "Pedido inválido" }, 400);
            return;
        }

        var normalized = Normalize(reading);
        await WriteJson(context.Response, normalized, 200);
    }
    catch (Exception ex)
    {
        await WriteJson(context.Response, new { error = ex.Message }, 500);
    }
}

static NormalizedReading Normalize(SensorReading reading)
{
    // Uniformiza o tipo, valor e unidade da leitura.
    string tipo = NormalizeType(reading.Tipo);
    double valor = ParseValue(reading.Valor);

    return new NormalizedReading(
        SensorId: reading.SensorId.Trim().ToUpperInvariant(),
        Zona: reading.Zona.Trim().ToUpperInvariant(),
        Tipo: tipo,
        Valor: valor,
        Unidade: ResolveUnit(tipo),
        FormatoOrigem: reading.FormatoOrigem,
        TimestampOriginal: reading.Timestamp,
        TimestampProcessamento: DateTime.UtcNow);
}

static string NormalizeType(string tipo)
{
    string normalized = tipo.Trim().ToUpperInvariant().Replace(".", "");
    return normalized switch
    {
        "PM25" => "PM25",
        "TEMPERATURA" => "TEMP",
        _ => normalized
    };
}

static double ParseValue(string valor)
{
    string normalized = valor.Trim().Replace(',', '.');
    return double.Parse(normalized, CultureInfo.InvariantCulture);
}

static string ResolveUnit(string tipo)
{
    return tipo switch
    {
        "PM25" => "ug/m3",
        "TEMP" => "C",
        "HUMIDADE" => "%",
        _ => "valor"
    };
}

static async Task WriteJson(HttpListenerResponse response, object value, int statusCode)
{
    response.StatusCode = statusCode;
    response.ContentType = "application/json";
    string json = JsonSerializer.Serialize(value);
    byte[] data = Encoding.UTF8.GetBytes(json);
    await response.OutputStream.WriteAsync(data);
    response.Close();
}

static JsonSerializerOptions JsonOptions()
{
    return new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
}

record SensorReading(
    string SensorId,
    string Zona,
    string Tipo,
    string Valor,
    string FormatoOrigem,
    DateTime Timestamp);

record NormalizedReading(
    string SensorId,
    string Zona,
    string Tipo,
    double Valor,
    string Unidade,
    string FormatoOrigem,
    DateTime TimestampOriginal,
    DateTime TimestampProcessamento);
