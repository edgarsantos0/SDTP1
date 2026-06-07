# Guia de Execução

Este guia explica como correr o sistema completo com a interface de terminal.

## 1. Pré-requisitos

Instalar antes de executar:

- .NET 8 SDK
- Python 3
- Erlang/OTP
- RabbitMQ Server instalado localmente

Confirmar no terminal:

```powershell
dotnet --version
python --version
rabbitmqctl status
```

## 2. Instalar RabbitMQ Sem Docker

O RabbitMQ precisa do Erlang/OTP instalado.

Opção recomendada no Windows:

1. Instalar Erlang/OTP pelo instalador oficial.
2. Instalar RabbitMQ Server pelo instalador oficial.
3. Abrir PowerShell como administrador.
4. Ativar a interface web de gestão:

```powershell
rabbitmq-plugins enable rabbitmq_management
```

5. Iniciar o serviço:

```powershell
Start-Service RabbitMQ
```

6. Confirmar que está ativo:

```powershell
rabbitmqctl status
```

Se o comando `rabbitmqctl` não for reconhecido, adicionar a pasta `sbin` do RabbitMQ ao `PATH`, por exemplo:

```text
C:\Program Files\RabbitMQ Server\rabbitmq_server-<versao>\sbin
```

Nesta máquina o RabbitMQ está instalado via Scoop. A interface também procura automaticamente em:

```text
C:\Users\User\scoop\apps\rabbitmq\current\sbin\rabbitmq-server.bat
C:\Users\User\scoop\apps\rabbitmq\<versao>\sbin\rabbitmq-server.bat
```

## 3. Restaurar Dependências

Na raiz do projeto:

```powershell
dotnet restore Gateway\Gateway.sln
```

## 4. Executar Pela Interface Terminal

Na raiz do projeto:

```powershell
dotnet run --project TerminalApp
```

Vai aparecer o menu principal:

```text
=== Sistema Distribuído - Interface Terminal ===
1. Iniciar RabbitMQ local
2. Iniciar serviço RPC de pré-processamento
3. Iniciar serviço RPC de análise
4. Iniciar Servidor
5. Iniciar Gateway
6. Iniciar tudo
7. Publicar leitura de sensor
8. Simular leituras automáticas
9. Ver últimas leituras
10. Pedir nova análise
11. Ver últimas análises
0. Sair e parar processos iniciados
```

## 5. Ordem Recomendada

Usar a opção:

```text
6. Iniciar tudo
```

Esta opção inicia:

1. RabbitMQ local, usando o serviço Windows `RabbitMQ`, uma instalação Scoop ou o comando `rabbitmq-server.bat`
2. Serviço RPC de pré-processamento
3. Serviço RPC de análise
4. Servidor
5. Gateway

Aguardar alguns segundos até os processos abrirem.

Cada componente iniciado pela interface abre a sua própria janela de terminal. Assim é possível ver separadamente os logs do Gateway, Servidor, serviço de pré-processamento, serviço de análise e sensores lançados pela interface.

Se o RabbitMQ ainda não estiver instalado, a interface vai mostrar um aviso e os restantes componentes não conseguirão comunicar com o broker até a instalação estar concluída.

## 6. Enviar Dados de Sensores

Depois de iniciar tudo, usar:

```text
7. Publicar leitura de sensor
```

Valores de exemplo:

```text
Sensor ID: S102
Zona: ZONA_ESCOLAR
Tipo: PM2.5
Valor: 42
```

Também é possível gerar várias leituras automaticamente:

```text
8. Simular leituras automáticas
```

Sensores válidos existentes em `Gateway/sensores.csv`:

```text
S102 - ZONA_ESCOLAR - PM2.5,TEMP
S103 - PARQUE_MUNICIPAL - PM2.5,TEMP
```

## 7. Consultar Leituras

Para ver os dados guardados no Servidor:

```text
9. Ver últimas leituras
```

As leituras são persistidas na base de dados SQLite:

```text
servidor.db
```

## 8. Pedir Análises

Para pedir uma nova análise:

```text
10. Pedir nova análise
```

Exemplo:

```text
Sensor ID opcional: S102
Zona opcional: ZONA_ESCOLAR
Tipo opcional: PM2.5
```

O Servidor consulta a base de dados, chama o serviço RPC de análise em Python e guarda o resultado.

Para consultar análises já feitas:

```text
11. Ver últimas análises
```

## 9. Execução Manual Alternativa

Se não quiser usar a opção `6`, pode iniciar cada componente manualmente em terminais separados.

RabbitMQ:

```powershell
Start-Service RabbitMQ
rabbitmqctl status
```

Serviço RPC de pré-processamento:

```powershell
dotnet run --project PreProcessingService
```

Serviço RPC de análise:

```powershell
python analysis_service.py
```

Servidor:

```powershell
dotnet run --project Servidor
```

Gateway:

```powershell
dotnet run --project Gateway
```

Sensor manual:

```powershell
dotnet run --project Sensor
```

Publicar uma leitura única:

```powershell
dotnet run --project Sensor -- --once S102 ZONA_ESCOLAR PM2.5 35
```

## 10. RabbitMQ Management

Interface web do RabbitMQ:

```text
http://localhost:15672
```

Credenciais:

```text
guest / guest
```

Portas usadas:

```text
5672  - RabbitMQ AMQP
15672 - RabbitMQ Management UI
6000  - Servidor TCP
7001  - RPC Pré-processamento
7002  - RPC Análise
```

## 11. Problemas Comuns

Se o RabbitMQ não iniciar:

```powershell
Get-Service RabbitMQ
Start-Service RabbitMQ
rabbitmqctl status
```

Se `Start-Service RabbitMQ` falhar por permissões, abrir PowerShell como administrador.

Se `rabbitmqctl` não for reconhecido, confirmar o `PATH` para a pasta `sbin` do RabbitMQ.

Se o Gateway não receber mensagens, confirmar que RabbitMQ está ativo e que o Gateway foi iniciado depois do RabbitMQ.

Se a análise falhar, confirmar que o serviço Python está ativo:

```powershell
python analysis_service.py
```

Se aparecer erro de porta ocupada, fechar os processos anteriores ou escolher `0` na interface para parar os processos iniciados por ela.

Se a base de dados parecer vazia, enviar primeiro leituras com as opções `7` ou `8`.
