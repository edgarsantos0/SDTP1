using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

public static class DataProcessor
{
    private const string ConnectionString = "Data Source=servidor.db";
    private static readonly object LockObj = new object();
    private static readonly HttpClient HttpClient = new HttpClient();

    public static void Initialize()
    {
        lock (LockObj)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS Readings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SensorId TEXT NOT NULL,
                Zona TEXT NOT NULL,
                Tipo TEXT NOT NULL,
                Valor REAL NOT NULL,
                Unidade TEXT NOT NULL,
                FormatoOrigem TEXT NOT NULL,
                TimestampOriginal TEXT NOT NULL,
                TimestampProcessamento TEXT NOT NULL,
                TimestampRececao TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AnalysisResults (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SensorId TEXT NULL,
                Zona TEXT NULL,
                Tipo TEXT NULL,
                FromUtc TEXT NULL,
                ToUtc TEXT NULL,
                ResultJson TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            );
            """;
            command.ExecuteNonQuery();
        }
    }

    public static async Task<string> ProcessAsync(string msg)
    {
        using JsonDocument document = JsonDocument.Parse(msg);

        if (document.RootElement.TryGetProperty("command", out var commandProperty))
        {
            string command = commandProperty.GetString() ?? "";
            return command.ToLowerInvariant() switch
            {
                "latest" => JsonSerializer.Serialize(GetLatest()),
                "analyses" => JsonSerializer.Serialize(GetLatestAnalyses()),
                "analyze" => await AnalyzeAsync(document.RootElement),
                _ => JsonSerializer.Serialize(new { ok = false, error = "Comando desconhecido" })
            };
        }

        var reading = JsonSerializer.Deserialize<NormalizedReading>(msg, JsonOptions());
        if (reading == null)
        {
            return JsonSerializer.Serialize(new { ok = false, error = "Leitura inválida" });
        }

        SaveReading(reading);
        return JsonSerializer.Serialize(new { ok = true, message = "Leitura armazenada" });
    }

    private static void SaveReading(NormalizedReading reading)
    {
        lock (LockObj)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
            """
            INSERT INTO Readings (
                SensorId, Zona, Tipo, Valor, Unidade, FormatoOrigem,
                TimestampOriginal, TimestampProcessamento, TimestampRececao
            )
            VALUES (
                $sensorId, $zona, $tipo, $valor, $unidade, $formatoOrigem,
                $timestampOriginal, $timestampProcessamento, $timestampRececao
            );
            """;

            command.Parameters.AddWithValue("$sensorId", reading.SensorId);
            command.Parameters.AddWithValue("$zona", reading.Zona);
            command.Parameters.AddWithValue("$tipo", reading.Tipo);
            command.Parameters.AddWithValue("$valor", reading.Valor);
            command.Parameters.AddWithValue("$unidade", reading.Unidade);
            command.Parameters.AddWithValue("$formatoOrigem", reading.FormatoOrigem);
            command.Parameters.AddWithValue("$timestampOriginal", reading.TimestampOriginal.ToString("O"));
            command.Parameters.AddWithValue("$timestampProcessamento", reading.TimestampProcessamento.ToString("O"));
            command.Parameters.AddWithValue("$timestampRececao", DateTime.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    private static List<object> GetLatest()
    {
        var readings = new List<object>();

        lock (LockObj)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
            """
            SELECT SensorId, Zona, Tipo, Valor, Unidade, TimestampRececao
            FROM Readings
            ORDER BY Id DESC
            LIMIT 10;
            """;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                readings.Add(new
                {
                    sensorId = reader.GetString(0),
                    zona = reader.GetString(1),
                    tipo = reader.GetString(2),
                    valor = reader.GetDouble(3),
                    unidade = reader.GetString(4),
                    timestampRececao = reader.GetString(5)
                });
            }
        }

        return readings;
    }

    private static List<object> GetLatestAnalyses()
    {
        var analyses = new List<object>();

        lock (LockObj)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
            """
            SELECT Id, SensorId, Zona, Tipo, ResultJson, CreatedAtUtc
            FROM AnalysisResults
            ORDER BY Id DESC
            LIMIT 10;
            """;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                analyses.Add(new
                {
                    id = reader.GetInt64(0),
                    sensorId = reader.IsDBNull(1) ? null : reader.GetString(1),
                    zona = reader.IsDBNull(2) ? null : reader.GetString(2),
                    tipo = reader.IsDBNull(3) ? null : reader.GetString(3),
                    result = JsonDocument.Parse(reader.GetString(4)).RootElement.Clone(),
                    createdAtUtc = reader.GetString(5)
                });
            }
        }

        return analyses;
    }

    private static async Task<string> AnalyzeAsync(JsonElement command)
    {
        string? sensorId = GetOptionalString(command, "sensorId");
        string? zona = GetOptionalString(command, "zona")?.ToUpperInvariant();
        string? tipo = GetOptionalString(command, "tipo")?.ToUpperInvariant().Replace(".", "");
        string? fromUtc = GetOptionalString(command, "fromUtc");
        string? toUtc = GetOptionalString(command, "toUtc");

        var readings = QueryReadings(sensorId, zona, tipo, fromUtc, toUtc);
        string url = Environment.GetEnvironmentVariable("ANALYSIS_URL") ?? "http://localhost:7002/rpc/analyze";

        var requestPayload = new
        {
            sensorId,
            zona,
            tipo,
            fromUtc,
            toUtc,
            readings
        };

        string requestJson = JsonSerializer.Serialize(requestPayload);
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        using var response = await HttpClient.PostAsync(url, content);

        string resultJson = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"RPC de análise falhou ({(int)response.StatusCode}): {resultJson}");
        }

        SaveAnalysis(sensorId, zona, tipo, fromUtc, toUtc, resultJson);

        return resultJson;
    }

    private static List<object> QueryReadings(string? sensorId, string? zona, string? tipo, string? fromUtc, string? toUtc)
    {
        var readings = new List<object>();

        lock (LockObj)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
            """
            SELECT SensorId, Zona, Tipo, Valor, Unidade, TimestampRececao
            FROM Readings
            WHERE ($sensorId IS NULL OR SensorId = $sensorId)
              AND ($zona IS NULL OR Zona = $zona)
              AND ($tipo IS NULL OR Tipo = $tipo)
              AND ($fromUtc IS NULL OR TimestampRececao >= $fromUtc)
              AND ($toUtc IS NULL OR TimestampRececao <= $toUtc)
            ORDER BY Id ASC;
            """;

            command.Parameters.AddWithValue("$sensorId", (object?)sensorId ?? DBNull.Value);
            command.Parameters.AddWithValue("$zona", (object?)zona ?? DBNull.Value);
            command.Parameters.AddWithValue("$tipo", (object?)tipo ?? DBNull.Value);
            command.Parameters.AddWithValue("$fromUtc", (object?)fromUtc ?? DBNull.Value);
            command.Parameters.AddWithValue("$toUtc", (object?)toUtc ?? DBNull.Value);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                readings.Add(new
                {
                    sensorId = reader.GetString(0),
                    zona = reader.GetString(1),
                    tipo = reader.GetString(2),
                    valor = reader.GetDouble(3),
                    unidade = reader.GetString(4),
                    timestampRececao = reader.GetString(5)
                });
            }
        }

        return readings;
    }

    private static void SaveAnalysis(string? sensorId, string? zona, string? tipo, string? fromUtc, string? toUtc, string resultJson)
    {
        lock (LockObj)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
            """
            INSERT INTO AnalysisResults (SensorId, Zona, Tipo, FromUtc, ToUtc, ResultJson, CreatedAtUtc)
            VALUES ($sensorId, $zona, $tipo, $fromUtc, $toUtc, $resultJson, $createdAtUtc);
            """;

            command.Parameters.AddWithValue("$sensorId", (object?)sensorId ?? DBNull.Value);
            command.Parameters.AddWithValue("$zona", (object?)zona ?? DBNull.Value);
            command.Parameters.AddWithValue("$tipo", (object?)tipo ?? DBNull.Value);
            command.Parameters.AddWithValue("$fromUtc", (object?)fromUtc ?? DBNull.Value);
            command.Parameters.AddWithValue("$toUtc", (object?)toUtc ?? DBNull.Value);
            command.Parameters.AddWithValue("$resultJson", resultJson);
            command.Parameters.AddWithValue("$createdAtUtc", DateTime.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.GetString()
            : null;
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

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