using System.Net;
using System.Net.Sockets;

public class Server
{
    private TcpListener listener;

    public Server(string ip, int port)
    {
        listener = new TcpListener(IPAddress.Parse(ip), port);
    }

    public void Start()
    {
        DataProcessor.Initialize();
        listener.Start();
        Console.WriteLine("Servidor iniciado...");

        while (true)
        {
            TcpClient client = listener.AcceptTcpClient();

            Thread t = new Thread(() =>
            {
                ClientHandler handler = new ClientHandler(client);
                handler.Handle();
            });

            t.Start();
        }
    }
}