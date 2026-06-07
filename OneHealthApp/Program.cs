using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

var app = new TerminalInterface();
await app.RunAsync();

class TerminalInterface
{
    private readonly List<Process> startedProcesses = new();
    private readonly string root = Directory.GetCurrentDirectory();

    public async Task RunAsync()
    {
        // Mostra o menu principal da aplicação.
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("=== Sistema Distribuído - Interface Terminal ===");
            Console.WriteLine("1. Iniciar RabbitMQ local");
            Console.WriteLine("2. Iniciar serviço RPC de pré-processamento");
            Console.WriteLine("3. Iniciar serviço RPC de análise");
            Console.WriteLine("4. Iniciar Servidor");
            Console.WriteLine("5. Iniciar Gateway");
            Console.WriteLine("6. Iniciar tudo");
            Console.WriteLine("7. Publicar leitura de sensor");
            Console.WriteLine("8. Simular leituras automáticas");
            Console.WriteLine("9. Ver últimas leituras");
            Console.WriteLine("10. Pedir nova análise");
            Console.WriteLine("11. Ver últimas análises");
            Console.WriteLine("0. Sair e parar processos iniciados");
            Console.Write("Opção: ");

            string option = Console.ReadLine()?.Trim() ?? "";
            Console.WriteLine();

            switch (option)
            {
                case "1":
                    await StartRabbitMqLocal();
                    break;
                case "2":
                    StartTerminalCommand("dotnet", "run --project PreProcessingService", "Pré-processamento RPC");
                    break;
                case "3":
                    StartTerminalCommand("python", "analysis_service.py", "Análise RPC");
                    break;
                case "4":
                    StartTerminalCommand("dotnet", "run --project Servidor", "Servidor");
                    break;
                case "5":
                    StartTerminalCommand("dotnet", "run --project Gateway", "Gateway");
                    break;
                case "6":
                    await StartRabbitMqLocal();
                    await Task.Delay(3000);
                    StartTerminalCommand("dotnet", "run --project PreProcessingService", "Pré-processamento RPC");
                    StartTerminalCommand("python", "analysis_service.py", "Análise RPC");
                    StartTerminalCommand("dotnet", "run --project Servidor", "Servidor");
                    StartTerminalCommand("dotnet", "run --project Gateway", "Gateway");
                    break;
                case "7":
                    await PublishSingleReading();
                    break;
                case "8":
                    await SimulateReadings();
                    break;
                case "9":
                    await SendAndPrintServerCommand(new { command = "latest" });
                    break;
                case "10":
                    await RequestAnalysis();
                    break;
                case "11":
                    await SendAndPrintServerCommand(new { command = "analyses" });
                    break;
                case "0":
                    StopStartedProcesses();
                    return;
                default:
                    Console.WriteLine("Opção inválida.");
                    break;
            }
        }
    }

    private void StartTerminalCommand(string fileName, string arguments, string name)
    {
        // Abre um novo terminal para executar um componente.
        string command = $"& '{EscapePowerShell(fileName)}' {arguments}";
        StartTerminalPowerShell(command, name);
    }

    private void StartTerminalPowerShell(string command, string name)
    {
        string terminalCommand = $"Set-Location -LiteralPath '{EscapePowerShell(root)}'; Write-Host '{EscapePowerShell(name)}'; {command}";
        string escapedCommand = terminalCommand.Replace("\"", "\\\"");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"{name}\" powershell -NoExit -ExecutionPolicy Bypass -Command \"{escapedCommand}\"",
                WorkingDirectory = root,
                UseShellExecute = false
            }
        };

        process.Start();
        startedProcesses.Add(process);
        Console.WriteLine($"{name} iniciado numa nova janela de terminal.");
    }

    private async Task StartRabbitMqLocal()
    {
        // Garante que o RabbitMQ local está disponível.
        Console.WriteLine("A tentar iniciar RabbitMQ local...");

        if (await IsTcpPortOpen("127.0.0.1", 5672))
        {
            Console.WriteLine("RabbitMQ já está ativo em localhost:5672.");
            return;
        }

        var serviceResult = await RunCommand(
            "powershell",
            "-NoProfile -ExecutionPolicy Bypass -Command \"if (Get-Service RabbitMQ -ErrorAction SilentlyContinue) { Start-Service RabbitMQ; Write-Output 'Serviço RabbitMQ iniciado.'; exit 0 } else { exit 1 }\"");

        if (serviceResult.ExitCode == 0)
        {
            Console.WriteLine(serviceResult.Output);
            await Task.Delay(2000);
            return;
        }

        string? rabbitMqServer = FindRabbitMqServerCommand();
        if (rabbitMqServer != null)
        {
            StartTerminalCommand(rabbitMqServer, "", "RabbitMQ local");
            return;
        }

        Console.WriteLine("Instala RabbitMQ localmente e volta a escolher esta opção.");
        Console.WriteLine("Depois da instalação, confirma com: rabbitmqctl status");
    }

    private static async Task<bool> IsTcpPortOpen(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(2));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<(int ExitCode, string Output)> RunCommand(string fileName, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }

        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, string.IsNullOrWhiteSpace(error) ? output : output + Environment.NewLine + error);
    }

    private static string? FindRabbitMqServerCommand()
    {
        var candidates = new List<string>();

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        candidates.Add(Path.Combine(userProfile, "scoop", "apps", "rabbitmq", "current", "sbin", "rabbitmq-server.bat"));

        string scoopRabbitMq = Path.Combine(userProfile, "scoop", "apps", "rabbitmq");
        if (Directory.Exists(scoopRabbitMq))
        {
            candidates.AddRange(Directory
                .GetDirectories(scoopRabbitMq)
                .Select(directory => Path.Combine(directory, "sbin", "rabbitmq-server.bat")));
        }

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string rabbitMqProgramFiles = Path.Combine(programFiles, "RabbitMQ Server");
        if (Directory.Exists(rabbitMqProgramFiles))
        {
            candidates.AddRange(Directory
                .GetDirectories(rabbitMqProgramFiles)
                .Select(directory => Path.Combine(directory, "sbin", "rabbitmq-server.bat")));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private async Task PublishSingleReading()
    {
        // Recolhe os dados de uma leitura e lança o Sensor.
        string sensorId = Prompt("Sensor ID", "S102");
        string zona = Prompt("Zona", "ZONA_ESCOLAR");
        string tipo = Prompt("Tipo", "PM2.5");
        string valor = Prompt("Valor", "18.5");

        await RunSensorOnce(sensorId, zona, tipo, valor);
    }

    private async Task SimulateReadings()
    {
        // Lança uma simulação com várias leituras de sensor.
        string sensorId = Prompt("Sensor ID", "S102");
        string zona = Prompt("Zona", "ZONA_ESCOLAR");
        StartTerminalCommand("dotnet", $"run --project Sensor -- --auto {sensorId} {zona} 10 1000", $"Simulação {sensorId}");
        await Task.CompletedTask;
    }

    private async Task RunSensorOnce(string sensorId, string zona, string tipo, string valor)
    {
        StartTerminalCommand("dotnet", $"run --project Sensor -- --once {sensorId} {zona} {tipo} {valor}", $"Sensor {sensorId} {tipo}");
        await Task.CompletedTask;
    }

    private async Task RequestAnalysis()
    {
        // Pede ao Servidor uma nova análise dos dados guardados.
        string sensorId = Prompt("Sensor ID opcional", "");
        string zona = Prompt("Zona opcional", "");
        string tipo = Prompt("Tipo opcional", "PM2.5");

        await SendAndPrintServerCommand(new
        {
            command = "analyze",
            sensorId = EmptyToNull(sensorId),
            zona = EmptyToNull(zona),
            tipo = EmptyToNull(tipo),
            fromUtc = (string?)null,
            toUtc = (string?)null
        });
    }

    private async Task SendAndPrintServerCommand(object command)
    {
        // Envia comandos da interface para o Servidor por TCP.
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", 6000);
            await using NetworkStream stream = client.GetStream();

            string json = JsonSerializer.Serialize(command) + "\n";
            byte[] data = Encoding.UTF8.GetBytes(json);
            await stream.WriteAsync(data);

            using var reader = new StreamReader(stream, Encoding.UTF8);
            string? response = await reader.ReadLineAsync();
            Console.WriteLine(PrettyJson(response ?? "{}"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("Erro ao comunicar com o Servidor: " + ex.Message);
        }
    }

    private static string PrettyJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }

    private static string Prompt(string label, string defaultValue)
    {
        Console.Write($"{label} ({defaultValue}): ");
        string value = Console.ReadLine()?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string EscapePowerShell(string value)
    {
        return value.Replace("'", "''");
    }

    private void StopStartedProcesses()
    {
        foreach (var process in startedProcesses)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignorar processos já terminados.
            }
        }
    }
}
