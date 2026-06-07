using System.Net.Sockets;
using System.Text;

public class SensorHandler
{
    private TcpClient client;

    private CsvManager csv;

    private bool isValidated = false;

    private string? currentSensorId = null;

    public static readonly object LockObj = new object();

    private Sensor? ValidateSensor(string sensorId, NetworkStream stream)
    {

        Sensor? s;
        // evitar 30x o s == null etc
        lock (LockObj)
        {
            s = csv.GetSensor(sensorId);
        }

        if (s == null)
        {
            SendResponse(stream, "ERROR|Sensor_invalido");
            return null;
        }

        if (s.Estado != "ativo")
        {
            SendResponse(stream, "ERROR|Sensor_inativo");
            return null;
        }

        return s;
    }

    public SensorHandler(TcpClient client, CsvManager csv)
    {
        this.client = client;
        this.csv = csv;
    }

    public void Handle()
    {
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[1024];

        try
        {
            while (true)
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);

                if (bytesRead == 0)
                    break;

                string[] messages = Encoding.UTF8
                    .GetString(buffer, 0, bytesRead)
                    .Split('\n');

                foreach (var message in messages)
                {
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        Console.WriteLine("Recebido: " + message);
                        ProcessMessage(message.Trim(), stream);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERRO: " + ex.Message);
            return;
        }
    }

    private void ProcessMessage(string msg, NetworkStream stream)
    {
        msg = msg.Trim();

        string[] parts = msg.Split('|');

        if (parts.Length < 2)
        {
            Console.WriteLine("Mensagem inválida recebida");
            return;
        }

        if (parts[0] != "REGISTER" && !isValidated)
        {
            SendResponse(stream, "ERROR|Nao_registado");
            return;
        }

        switch (parts[0])
        {
            case "REGISTER":
                HandleRegister(parts, stream);
                break;

            case "DATA":
                HandleData(parts, stream);
                break;

            case "HEARTBEAT":
                HandleHeartbeat(parts);
                break;

            case "VIDEO_START":
                HandleVideo(parts, stream);
                break;

            case "DISCONNECT":
                Console.WriteLine($"Sensor {parts[1]} desligou-se");
                client.Close();
                break;
        }       
    }

    private void HandleVideo(string[] parts, NetworkStream stream)
    {
        string sensorId = parts[1].Trim();

        Sensor? s = ValidateSensor(sensorId, stream);
        if (s == null) return;

        Console.WriteLine($"A iniciar processamento de vídeo do sensor {sensorId}");

        SendResponse(stream, "OK|VIDEO");
    }

    private void HandleRegister(string[] parts, NetworkStream stream)
    {
        string sensorId = parts[1];

        Sensor? s = ValidateSensor(sensorId, stream);
        if (s == null) return;

        isValidated = true;
        currentSensorId = sensorId;

        Console.WriteLine($"Sensor {sensorId} validado com sucesso");

        Console.WriteLine("A enviar OK ao sensor...");

        SendResponse(stream, "OK");
    }

    private void HandleData(string[] parts, NetworkStream stream)
    {
        string sensorId = parts[1];
        string zona = parts[2];
        string tipo = parts[3].Trim().ToUpper();
        string valor = parts[4];

        Sensor? s = ValidateSensor(sensorId, stream);
        if (s == null) return;

        bool tipoValido = s.TiposDados
            .Select(t => t.Trim().ToUpper())
            .Contains(tipo);

        Console.WriteLine("Tipos permitidos: " + string.Join(",", s.TiposDados));
        Console.WriteLine("Tipo recebido: " + tipo);

        if (!tipoValido)
        {
            Console.WriteLine("Tipo inválido BLOQUEADO: " + tipo);
            SendResponse(stream, "ERROR|Tipo_nao_suportado");
            return;
        }
 
        lock (LockObj)
        {
            s.LastSync = DateTime.Now;
        }

        Console.WriteLine($"DATA -> {sensorId} | {zona} | {tipo} | {valor}");

        Console.WriteLine("A enviar para servidor...");
        SendToServer(string.Join("|", parts));

        SendResponse(stream, "OK");
    }

    private void SendToServer(string msg)
    {
        try
        {
            TcpClient serverClient = new TcpClient("127.0.0.1", 6000);
            NetworkStream stream = serverClient.GetStream();

            byte[] data = Encoding.UTF8.GetBytes(msg);
            stream.Write(data, 0, data.Length);

            serverClient.Close();
        }
        catch
        {
            Console.WriteLine("Erro ao enviar para servidor");
        }
    }

    private void SendResponse(NetworkStream stream, string response)
    {
        byte[] data = Encoding.UTF8.GetBytes(response);
        stream.Write(data, 0, data.Length);
    }

    private void HandleHeartbeat(string[] parts)
    {
        string sensorId = parts[1];

        Sensor? s;

        lock (LockObj)
        {
            s = csv.GetSensor(sensorId);
        }

        if (s != null)
        {
            lock (LockObj)
            {
                s.LastSync = DateTime.Now;
            }
            Console.WriteLine($"Heartbeat atualizado: {sensorId}");
        }
    }
}