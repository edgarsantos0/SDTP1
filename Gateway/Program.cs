class Program
{
    static async Task Main(string[] args)
    {
        GatewayServer server = new GatewayServer();
        await server.StartAsync();
    }
}