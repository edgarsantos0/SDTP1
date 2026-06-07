using System.Net.Sockets;
using System.Text;

public class ClientHandler
{
    private TcpClient client;

    public ClientHandler(TcpClient client)
    {
        this.client = client;
    }

    public void Handle()
    {
        // Lê mensagens TCP enviadas pelo Gateway ou pela interface.
        NetworkStream stream = client.GetStream();

        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

            while (reader.ReadLine() is string message)
            {
                Console.WriteLine("Recebido: " + message);

                string response;
                try
                {
                    // Processa a mensagem e devolve uma resposta JSON.
                    response = DataProcessor.ProcessAsync(message).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    response = System.Text.Json.JsonSerializer.Serialize(new { ok = false, error = ex.Message });
                }

                if (!string.IsNullOrWhiteSpace(response))
                {
                    byte[] data = Encoding.UTF8.GetBytes(response + "\n");
                    stream.Write(data, 0, data.Length);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Cliente desconectado: " + ex.Message);
        }
    }
}