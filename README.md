# SistemasDistribuidos2026
Repositório para o trabalho de Sistemas Distribuídos

## Componentes

- `Sensor`: publica leituras no RabbitMQ através de Pub/Sub.
- `Gateway`: subscreve tópicos RabbitMQ, valida sensores e chama o RPC de pré-processamento.
- `PreProcessingService`: serviço RPC HTTP em C# para normalização de dados.
- `Servidor`: recebe leituras normalizadas, persiste em SQLite e chama o RPC de análise.
- `analysis_service.py`: serviço RPC HTTP em Python para estatísticas, deteção de padrões simples e risco.
- `OneHealthApp`: interface básica de terminal para iniciar componentes, publicar leituras e pedir análises.

## Execução recomendada

Pré-requisitos:

- .NET 8 SDK
- Python 3
- Erlang/OTP
- RabbitMQ Server local

Instalar/restaurar dependências:

```powershell
dotnet restore Gateway\Gateway.sln
```

Executar a interface principal:

```powershell
dotnet run --project OneHealthApp
```

No menu, usar a opção `6. Iniciar tudo`. Depois é possível publicar leituras, simular sensores, consultar últimas leituras e pedir análises.

Guia completo de execução: [GUIA_EXECUCAO.md](GUIA_EXECUCAO.md)

RabbitMQ fica disponível em:

- AMQP: `localhost:5672`
- Interface web: `http://localhost:15672`
- Utilizador/password: `guest` / `guest`
