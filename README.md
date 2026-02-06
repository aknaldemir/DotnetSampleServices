# ?? DotnetSampleServices - Tam Dokümantasyon

> WCF Producer ve .NET 8 Worker Consumer ile RabbitMQ kullanarak end-to-end asenkron ileti i?leme sistemi.

**Repository**: https://github.com/aknaldemir/DotnetSampleServices | **License**: MIT

---

## ?? H?zl? Ba?lang?ç (5 dakika)

### Ön Ko?ullar
```bash
# Gereklilikler:
- .NET Framework 4.7.2+ (WCF için)
- .NET 8.0 LTS (Consumer için)
- RabbitMQ 3.10+
- SQL Server 2016+
- Visual Studio 2019+
```

### Ba?lat (Docker ile)
```bash
# 1. RabbitMQ
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 \
  -e RABBITMQ_DEFAULT_USER=guest \
  -e RABBITMQ_DEFAULT_PASS=guest \
  rabbitmq:4-management-alpine

# 2. SQL Server
docker run -d --name sqlserver -p 1433:1433 \
  -e SA_PASSWORD="YourPassword123!" \
  -e ACCEPT_EULA="Y" \
  mcr.microsoft.com/mssql/server:2019-latest

# 3. Consumer çal??t?r
cd RabbitMQ.Consumer
dotnet run

# 4. WCF ba?lat (Visual Studio ? IIS Express)
```

### Test Et
```bash
curl -X POST http://localhost/MessageDispatcher/MessageDispatcherService.svc \
  -H "Content-Type: text/xml" \
  -d '<?xml version="1.0"?><soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" xmlns:wcf="http://WCFServiceRabbitMQ"><soap:Body><wcf:SendMessage><wcf:message>Test</wcf:message></wcf:SendMessage></soap:Body></soap:Envelope>'

# RabbitMQ Management: http://localhost:15672 (guest/guest)
# Consumer output: "Mesaj al?nd?: Test"
```

---

## ??? S?STEM M?MAR?S?

### Genel Ak?? Diyagram?

```
????????????????????
?    ?stemci       ?
?   Uygulamas?     ?
????????????????????
         ? SOAP/HTTP
         ?
??????????????????????????????????????????????????????????????
?   WCFServiceRabbitMQ (Producer)                            ?
?   .NET Framework 4.7.2                                     ?
?                                                            ?
?   IMessageDispatcherService ? SendMessage()               ?
?        ?                                                    ?
?   MessageDispatcherService                                ?
?   ?? RabbitMqProducer.Publish(message)                   ?
?   ?? LogInfo(message) ? SQL: INSERT INTO Logs            ?
?   ?? LogError(exception) ? SQL: INSERT INTO Logs         ?
?        ?                                                    ?
?   RabbitMqProducer                                        ?
?   ?? ConnectionFactory(localhost:5672)                   ?
?      ?? BasicPublish() ? AMQP                            ?
?????????????????????????????????????????????????????????????
         ? AMQP Protocol
         ?
??????????????????????????????????????????????????????????????
?   RabbitMQ Message Broker (localhost:5672)               ?
?                                                            ?
?   Exchange: app.direct.exchange (Direct Type)            ?
?         ? RoutingKey: app.routing.create                ?
?   Queue: app.queue                                        ?
?   (Durable=true, Exclusive=false, AutoDelete=false)     ?
?                                                            ?
?   PrefetchCount: 5 (QoS)                                 ?
?????????????????????????????????????????????????????????????
         ? Message Consuming
         ?
??????????????????????????????????????????????????????????????
?   RabbitMQ.Consumer (Consumer)                            ?
?   .NET 8 Worker Service                                   ?
?                                                            ?
?   Worker (BackgroundService)                             ?
?   ?? StartAsync()                                        ?
?   ?  ?? InitRabbitMqAsync()                             ?
?   ?     ?? ConnectionFactory olu?tur                    ?
?   ?     ?? Connection & Channel aç                      ?
?   ?     ?? BasicQosAsync() - Prefetch ayarla            ?
?   ?     ?? QueueDeclareAsync() - Queue config           ?
?   ?                                                      ?
?   ?? ExecuteAsync(CancellationToken)                    ?
?      ?? AsyncEventingBasicConsumer olu?tur              ?
?      ?? ReceivedAsync += (ea) =>                        ?
?      ?  ?? Encoding.UTF8.GetString() - Decode          ?
?      ?  ?? ProcessMessageAsync(message)                ?
?      ?  ?  ?? SQL: INSERT INTO MQMessages              ?
?      ?  ?? BasicAckAsync() - Success                   ?
?      ?  ?? catch ? BasicNackAsync(requeue: true)       ?
?      ?                                                  ?
?      ?? BasicConsumeAsync() - Start consuming          ?
?????????????????????????????????????????????????????????????
         ? SQL Connection
         ?
??????????????????????????????????????????????????????????????
?   SQL Server Veritaban?                                   ?
?   (Server: 192.168.1.102, Port: 1433)                   ?
?                                                            ?
?   Logs Tablosu                                            ?
?   ?? Id (INT, Primary Key)                              ?
?   ?? Text (NVARCHAR(MAX))                               ?
?   ?? CreatedAt (DATETIME, Default: GETDATE())           ?
?                                                            ?
?   MQMessages Tablosu                                      ?
?   ?? Id (INT, Primary Key)                              ?
?   ?? Message (NVARCHAR(MAX))                            ?
?   ?? ProcessedAt (DATETIME, Default: GETDATE())         ?
??????????????????????????????????????????????????????????????
```

### ?leti Ak??? (Step-by-Step)

```
BA?ARILI SENARYO:

1. ?stemci ? WCF
   POST /MessageDispatcher/MessageDispatcherService.svc
   Body: "Merhaba RabbitMQ"

2. MessageDispatcherService.SendMessage()
   ?? RabbitMqProducer.Publish(message)
   ?  ?? ConnectionFactory ? BasicPublish()
   ?     ?? RabbitMQ'ya gönder ?
   ?
   ?? LogInfo(message)
      ?? SQL: INSERT INTO Logs VALUES ('Merhaba RabbitMQ') ?

3. RabbitMQ Kuyru?u
   Exchange: app.direct.exchange
   RoutingKey: app.routing.create
   Queue: app.queue
   Message: "Merhaba RabbitMQ" (Waiting)

4. Worker Consumer
   ?? BasicConsumeAsync() ile kuyru?u izle
   ?? ReceivedAsync event tetiklenir
   ?
   ?? Encoding.UTF8.GetString() ? "Merhaba RabbitMQ"
   ?? ProcessMessageAsync(message)
   ?  ?? SQL: INSERT INTO MQMessages VALUES ('Merhaba RabbitMQ') ?
   ?
   ?? BasicAckAsync() 
      ?? RabbitMQ: Mesaj silindi ?

5. Sonuç
   ? Logs: "Merhaba RabbitMQ"
   ? MQMessages: "Merhaba RabbitMQ"
   ? Latency: ~50-100ms


HATA SENARYOSU:

1. ProcessMessageAsync() exception att?
   ?? SQL ba?lant?s? ba?ar?s?z

2. catch blok tetiklendi
   ?? BasicNackAsync(requeue: true)
      ?? Mesaj kuyru?a geri konur

3. Consumer mesaj? tekrar al?r
   ?? Network düzelirse ba?ar?yla i?lenir
      veya yeniden ba?ar?s?z olur ? infinite loop

4. ?DEAL SENARYO: DLQ (Dead Letter Queue)
   ?? Permanent errors ? DLQ'ye gönder
   ?? Transient errors ? Requeue et
```

---

## ?? PROJE DETAYLARI

### 1. WCFServiceRabbitMQ (Producer)

**Teknoloji Stack**:
- .NET Framework 4.7.2
- WCF (Windows Communication Foundation)
- RabbitMQ.Client v7.2.0
- System.Data.SqlClient

#### Bile?enler

##### `IMessageDispatcherService.cs`
```csharp
[ServiceContract]
public interface IMessageDispatcherService
{
    [OperationContract]
    void SendMessage(string message);
}
```
- WCF hizmet sözle?mesini tan?mlar
- ?stemciler taraf?ndan ça?r?lan ana operasyon

##### `MessageDispatcherService.svc.cs`
```csharp
public class MessageDispatcherService : IMessageDispatcherService
{
    private readonly RabbitMqProducer rabbitMqProducer;

    public MessageDispatcherService()
    {
        this.rabbitMqProducer = new RabbitMqProducer();
    }

    public void SendMessage(string message)
    {
        try
        {
            rabbitMqProducer.Publish(message);
            LogInfo(message);
        }
        catch (Exception ex)
        {
            LogError(ex);
            throw;  // WCF client hata görsün
        }
    }

    private void LogInfo(string message)
    {
        var connectionString = ConfigurationManager
            .ConnectionStrings["DbConnection"].ConnectionString;

        using (var conn = new SqlConnection(connectionString))
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO Logs (Text) VALUES (@Text)";
            cmd.Parameters.AddWithValue("@Text", message);
            conn.Open();
            cmd.ExecuteNonQuery();
        }
    }

    private void LogError(Exception ex)
    {
        // LogInfo() ile ayn?, ex.ToString() gönderilir
    }
}
```

**Sorumluluklar**:
- ?stemci isteklerini kabul et
- RabbitMQ'ya mesaj gönder
- SQL'e log yaz
- Hatalar? yakala ve geri döndür

##### `RabbitMqProducer.cs`
```csharp
public class RabbitMqProducer : IDisposable, IRabbitMqProducer
{
    private readonly IConnection connection;
    private readonly IModel channel;

    private const string ExchangeName = "app.direct.exhange";
    private const string QueueName = "app.queue";
    private const string RoutingKey = "app.routing.create";

    public RabbitMqProducer()
    {
        var factory = new ConnectionFactory
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "guest",
            Password = "guest",
            DispatchConsumersAsync = true
        };

        connection = factory.CreateConnection();
        channel = connection.CreateModel();

        // Infrastructure setup
        channel.ExchangeDeclare(ExchangeName, ExchangeType.Direct, durable: true);
        channel.QueueDeclare(QueueName, durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind(QueueName, ExchangeName, RoutingKey);
    }

    public void Publish(string message)
    {
        var body = Encoding.UTF8.GetBytes(message);

        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;  // Sunucu yeniden ba?lansa kal?r

        channel.BasicPublish(
            exchange: ExchangeName,
            routingKey: RoutingKey,
            basicProperties: properties,
            body: body);
    }

    public void Dispose()
    {
        channel?.Close();
        connection?.Close();
    }
}
```

**Detayl? Ak??**:
```
Publish(message):
  1. String ? UTF-8 Bytes
  2. BasicProperties(Persistent=true) ayarla
  3. BasicPublish() ? Exchange
  4. Exchange routing key e?le?tir
  5. Queue'ye konulur
```

#### RabbitMQ Konfigürasyonu

| Parametre | De?er |
|-----------|-------|
| Host | localhost |
| Port | 5672 |
| Username | guest |
| Password | guest |
| Exchange | app.direct.exchange |
| Queue | app.queue |
| RoutingKey | app.routing.create |
| Durable | true |
| PrefetchCount | - |

---

### 2. RabbitMQ.Consumer (Consumer)

**Teknoloji Stack**:
- .NET 8.0 LTS
- Worker Service (BackgroundService)
- RabbitMQ.Client v7.2.0
- Microsoft.Data.SqlClient v6.1.3

#### Bile?enler

##### `Program.cs`
```csharp
using RabbitMQ.Consumer;

var builder = Host.CreateApplicationBuilder(args);

// Configuration
builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection("RabbitMQSettings"));

// Services
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
```

**Responsibilities**:
- Host builder olu?tur
- RabbitMqOptions'? DI'ye kaydet
- Worker service'i ekle
- Uygulamay? çal??t?r

##### `RabbitMqOptions.cs`
```csharp
public class RabbitMqOptions
{
    public string Host { get; set; } = default!;
    public int Port { get; set; }
    public string Username { get; set; } = default!;
    public string Password { get; set; } = default!;
    public string Queue { get; set; } = default!;
    public ushort PrefetchCount { get; set; }
}
```

**appsettings.json**:
```json
{
  "ConnectionStrings": {
    "DbConnection": "Server=192.168.1.102,1433;Database=Toki;User ID=sa;Password=***;..."
  },
  "RabbitMQSettings": {
    "Host": "192.168.1.102",
    "Port": 5672,
    "Username": "guest",
    "Password": "guest",
    "Queue": "app.queue",
    "PrefetchCount": 5
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

##### `Worker.cs` (BackgroundService)
```csharp
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly RabbitMqOptions rabbitMqOptions;
    private readonly IConfiguration configuration;

    private IConnection? _connection;
    private IChannel? _channel;

    public Worker(
        ILogger<Worker> logger,
        IOptions<RabbitMqOptions> rabbitMqOptions,
        IConfiguration configuration)
    {
        _logger = logger;
        this.rabbitMqOptions = rabbitMqOptions.Value;
        this.configuration = configuration;
    }

    // 1. SERVICE STARTUP
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await InitRabbitMqAsync();
        await base.StartAsync(cancellationToken);
    }

    // 2. MAIN EXECUTION LOOP
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumer = new AsyncEventingBasicConsumer(_channel!);

        // 2a. EVENT HANDLER - Mesaj al?nd???nda
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                // Decode message
                var message = Encoding.UTF8.GetString(ea.Body.ToArray());
                _logger.LogInformation("Mesaj al?nd?: {msg}", message);

                // Process message
                await ProcessMessageAsync(message);

                // ACK - Ba?ar?l? i?lendi
                await _channel!.BasicAckAsync(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mesaj i?lenemedi");

                // NACK - Hata, kuyru?a geri koy
                await _channel!.BasicNackAsync(
                    ea.DeliveryTag,
                    multiple: false,
                    requeue: true);  // true=kuyru?a geri, false=DLQ
            }
        };

        // 2b. START CONSUMING
        await _channel!.BasicConsumeAsync(
            queue: rabbitMqOptions.Queue,
            autoAck: false,  // Manual ack gerekli
            consumer: consumer);

        // 2c. KEEP ALIVE
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    // 3. MESSAGE PROCESSING
    private Task ProcessMessageAsync(string message)
    {
        var connectionString = configuration.GetConnectionString("DbConnection");

        using (var conn = new Microsoft.Data.SqlClient.SqlConnection(connectionString))
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO MQMessages (Message)
                VALUES (@Message)
            ";

            cmd.Parameters.AddWithValue("@Message", message);

            conn.Open();
            cmd.ExecuteNonQuery();
        }
        return Task.CompletedTask;
    }

    // 4. RABBITMQ INITIALIZATION
    private async Task InitRabbitMqAsync()
    {
        var factory = new ConnectionFactory
        {
            HostName = rabbitMqOptions.Host,
            Port = rabbitMqOptions.Port,
            UserName = rabbitMqOptions.Username,
            Password = rabbitMqOptions.Password
        };

        _connection = await factory.CreateConnectionAsync();
        _channel = await _connection.CreateChannelAsync();

        // QoS - Backpressure control
        await _channel.BasicQosAsync(
            prefetchSize: 0,                                // Byte limit yok
            prefetchCount: rabbitMqOptions.PrefetchCount,  // 5 mesaj
            global: false);                                 // Channel ba??na

        // Queue configuration
        await _channel.QueueDeclareAsync(
            queue: rabbitMqOptions.Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        _logger.LogInformation("RabbitMQ ba?lant?s? kuruldu (v7.x)");
    }
}
```

**Lifecycle Ak???**:
```
Host.Run()
  ?? Worker.StartAsync()
  ?  ?? InitRabbitMqAsync() ? RabbitMQ ba?lant? kur
  ?
  ?? Worker.ExecuteAsync() (Parallel çal???r)
     ?? AsyncEventingBasicConsumer olu?tur
     ?? BasicConsumeAsync() ? Kuyru?u dinle
     ?? Task.Delay(Infinite) ? Sonsuza kadar bekle
```

**Error Handling Pattern**:
```csharp
try
{
    // 1. Decode
    var message = Encoding.UTF8.GetString(ea.Body.ToArray());
    
    // 2. Process
    await ProcessMessageAsync(message);
    
    // 3. ACK Success
    await _channel.BasicAckAsync(ea.DeliveryTag, false);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Mesaj i?lenemedi");
    
    // NACK Failure
    await _channel.BasicNackAsync(
        ea.DeliveryTag,
        multiple: false,
        requeue: true);  // ? Kuyra?a geri koy
}
```

---

## ?? RABBITMQ TASARIMI

### Exchange & Queue Konsepti

```
Producer taraf?nda (WCFServiceRabbitMQ):
???????????????????????????????????????
? RabbitMqProducer.Publish()          ?
? ?? message = "Hello"                ?
? ?? body = UTF8.GetBytes(message)    ?
? ?? BasicPublish(                    ?
?     exchange: "app.direct.exchange" ?
?     routingKey: "app.routing.create"?
?     body: body                      ?
?   )                                 ?
???????????????????????????????????????
             ?
             ?
???????????????????????????????????????
? RabbitMQ Inside                     ?
?                                     ?
? Exchange: app.direct.exchange       ?
? (Type: Direct)                      ?
?                                     ?
? Routing Table:                      ?
? ??????????????????????????????????? ?
? ? RoutingKey        ? Queue       ? ?
? ? app.routing.create? app.queue   ? ?
? ??????????????????????????????????? ?
?                                     ?
? Action: "Hello" message             ?
? ?? Exchange'den aç                 ?
? ?? RoutingKey e?le?tir: MATCH! ?   ?
? ?? app.queue'ye koy                ?
???????????????????????????????????????
             ?
             ?
???????????????????????????????????????
? Queue: app.queue                    ?
? ??????????????????????????????????? ?
? ? Message 1: "Hello"              ? ?
? ? Message 2: "World"              ? ?
? ? Message 3: (waiting...)         ? ?
? ??????????????????????????????????? ?
? Status: Ready=2, Unacked=0          ?
???????????????????????????????????????
             ?
             ?
Consumer taraf?nda (RabbitMQ.Consumer):
???????????????????????????????????????
? Worker.ExecuteAsync()               ?
? ?? BasicConsumeAsync(queue: app.queue)
? ?? AsyncEventingBasicConsumer       ?
?    ?? ReceivedAsync event           ?
?    ?? Get "Hello"                   ?
?    ?? ProcessMessageAsync()         ?
?    ?? BasicAckAsync() ?             ?
???????????????????????????????????????
```

### Queue Özellikleri

```csharp
// Producer taraf?nda (setup)
channel.QueueDeclare(
    queue: "app.queue",
    durable: true,        // ? Sunucu crash ? disk'e yaz?l?r
    exclusive: false,     // ? Birden fazla consumer
    autoDelete: false,    // ? El ile silinmeli
    arguments: null
);

// Consumer taraf?nda (da setup yap?l?r - idempotent)
await _channel.QueueDeclareAsync(
    queue: "app.queue",
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: null
);
```

### QoS (Quality of Service) - Prefetch

```csharp
await _channel.BasicQosAsync(
    prefetchSize: 0,              // Byte baz?nda limit yok
    prefetchCount: 5,             // ? ?UNUN DE?ER? ÖNEML?!
    global: false                 // Channel ba??na (conn ba??na de?il)
);
```

**PrefetchCount = 5 Anlam?**:
```
Consumer'a ayn? anda max 5 mesaj gönderilir:

????????????????
? Consumer 1   ?
? Prefetch: 5  ?
? ?? Msg 1: Processing
? ?? Msg 2: Processing
? ?? Msg 3: Processing
? ?? Msg 4: Processing
? ?? Msg 5: Processing
? (Msg 6 bekleniyor)
????????????????

Her mesaj i?lendikten sonra:
?? BasicAckAsync() ? Tamamland?, Msg 6 gönder
?? BasicNackAsync() ? Hata, mesaj kuyru?a geri koy
```

### Direct vs Topic Exchange

```
DIRECT (?u anki):
???????????????????????????????????
? Exchange: app.direct.exchange   ?
? Type: Direct                    ?
? RoutingKey exact match gerekli  ?
???????????????????????????????????

Producer: BasicPublish(routingKey: "app.routing.create")
         ? Exact match
Queue: "app.queue" ? (ba?l?: "app.routing.create")
       ?
Consumer al?r

Avantaj: Basit, h?zl?, deterministic
Dezavantaj: Bir queue bir consumer pattern


TOPIC (Alternatif):
???????????????????????????????????
? Exchange: app.topic.exchange    ?
? Type: Topic                     ?
? RoutingKey pattern matching     ?
???????????????????????????????????

Producer: BasicPublish(routingKey: "messages.orders.create")
         ? Pattern match
Queues:
?? orders.queue (ba?l?: "messages.orders.*") ?
?? archive.queue (ba?l?: "messages.*.create") ?
?? audit.queue (ba?l?: "messages.#") ?

Avantaj: Flexible, multi-consumer, event routing
Dezavantaj: Kompleks, daha yava?
```

---

## ?? VER?TABANI ?EMASI

### SQL Server Setup

```sql
-- Database: Toki
-- Server: 192.168.1.102,1433
-- User: sa
-- Password: ***

USE Toki;
GO

-- Producer taraf?ndan (WCFServiceRabbitMQ)
IF OBJECT_ID('dbo.Logs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Logs (
        Id INT IDENTITY(1,1) PRIMARY KEY CLUSTERED,
        [Text] NVARCHAR(MAX) NOT NULL,
        CreatedAt DATETIME DEFAULT GETDATE()
    );
    
    CREATE NONCLUSTERED INDEX IX_Logs_CreatedAt 
        ON dbo.Logs([CreatedAt]) INCLUDE ([Text]);
    
    PRINT 'Logs tablosu olu?turuldu.';
END
GO

-- Consumer taraf?ndan (RabbitMQ.Consumer)
IF OBJECT_ID('dbo.MQMessages', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MQMessages (
        Id INT IDENTITY(1,1) PRIMARY KEY CLUSTERED,
        [Message] NVARCHAR(MAX) NOT NULL,
        ProcessedAt DATETIME DEFAULT GETDATE()
    );
    
    CREATE NONCLUSTERED INDEX IX_MQMessages_ProcessedAt 
        ON dbo.MQMessages([ProcessedAt]) INCLUDE ([Message]);
    
    PRINT 'MQMessages tablosu olu?turuldu.';
END
GO

-- Kontrol et
SELECT 'Logs' AS TableName, COUNT(*) AS RowCount FROM dbo.Logs
UNION ALL
SELECT 'MQMessages' AS TableName, COUNT(*) FROM dbo.MQMessages;
GO
```

### Insert Operations

**Producer (Logs)**:
```sql
INSERT INTO Logs (Text)
VALUES ('Merhaba dünya')  -- SendMessage'den gelen mesaj
```

**Consumer (MQMessages)**:
```sql
INSERT INTO MQMessages (Message)
VALUES ('Merhaba dünya')  -- ProcessMessageAsync'ten gelen mesaj
```

---

## ?? API KULLANIMI

### WCF Endpoint Bilgileri

```
Service: MessageDispatcherService
Protocol: WCF (SOAP)
Binding: basicHttpBinding
Endpoint: http://localhost/MessageDispatcher/MessageDispatcherService.svc
WSDL: http://localhost/MessageDispatcher/MessageDispatcherService.svc?wsdl
```

### SendMessage Operasyonu

```csharp
[OperationContract]
void SendMessage(string message);
```

| Parametre | Tip | Zorunlu | Aç?klama |
|-----------|-----|---------|----------|
| message | string | Evet | Gönderilecek mesaj |

**Ç?k??**: void (bo? döndürür)

**Exceptions**: 
- `BrokerUnreachableException` - RabbitMQ ba?lant?s? ba?ar?s?z
- `SqlException` - Veritaban? hatas?
- `Exception` - Di?er hatalar

---

### 6 Test Yöntemi

#### 1?? WCF Test Client (Visual Studio)
```
1. Visual Studio ? MessageDispatcherService.svc
2. Sa? t?kla ? Set as Start Page
3. F5 (Debug ba?lat)
4. WCF Test Client aç
5. SendMessage() çift t?kla
6. Request Value: "Test Mesaj?"
7. Invoke t?kla
```

#### 2?? SOAP UI
```xml
POST http://localhost/MessageDispatcher/MessageDispatcherService.svc

<?xml version="1.0" encoding="utf-8"?>
<soap:Envelope 
    xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" 
    xmlns:wcf="http://WCFServiceRabbitMQ">
    <soap:Body>
        <wcf:SendMessage>
            <wcf:message>Test mesaj? 123</wcf:message>
        </wcf:SendMessage>
    </soap:Body>
</soap:Envelope>
```

#### 3?? PowerShell
```powershell
$uri = "http://localhost/MessageDispatcher/MessageDispatcherService.svc"

$soapBody = @"
<?xml version="1.0" encoding="utf-8"?>
<soap:Envelope 
    xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" 
    xmlns:wcf="http://WCFServiceRabbitMQ">
    <soap:Body>
        <wcf:SendMessage>
            <wcf:message>PowerShell test</wcf:message>
        </wcf:SendMessage>
    </soap:Body>
</soap:Envelope>
"@

$response = Invoke-WebRequest -Uri $uri `
    -Method Post `
    -ContentType "text/xml; charset=utf-8" `
    -Body $soapBody `
    -Headers @{"SOAPAction" = "http://WCFServiceRabbitMQ/IMessageDispatcherService/SendMessage"}

Write-Host $response.Content
```

#### 4?? C# Client
```csharp
var binding = new BasicHttpBinding();
var endpoint = new EndpointAddress("http://localhost/MessageDispatcher/MessageDispatcherService.svc");
var client = new MessageDispatcherServiceClient(binding, endpoint);

try
{
    client.SendMessage("C# test message");
    Console.WriteLine("? Ba?ar?l?");
}
catch (Exception ex)
{
    Console.WriteLine($"? Hata: {ex.Message}");
}
finally
{
    client.Close();
}
```

#### 5?? .NET Core HttpClient
```csharp
var httpClient = new HttpClient();

var soapRequest = @"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:wcf=""http://WCFServiceRabbitMQ"">
    <soap:Body>
        <wcf:SendMessage>
            <wcf:message>Test message</wcf:message>
        </wcf:SendMessage>
    </soap:Body>
</soap:Envelope>";

var response = await httpClient.PostAsync(
    "http://localhost/MessageDispatcher/MessageDispatcherService.svc",
    new StringContent(soapRequest, Encoding.UTF8, "text/xml")
);

if (response.IsSuccessStatusCode)
    Console.WriteLine("? Ba?ar?l?");
```

#### 6?? cURL
```bash
curl -X POST \
  "http://localhost/MessageDispatcher/MessageDispatcherService.svc" \
  -H "Content-Type: text/xml; charset=utf-8" \
  -H "SOAPAction: http://WCFServiceRabbitMQ/IMessageDispatcherService/SendMessage" \
  -d '<?xml version="1.0"?><soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" xmlns:wcf="http://WCFServiceRabbitMQ"><soap:Body><wcf:SendMessage><wcf:message>cURL test</wcf:message></wcf:SendMessage></soap:Body></soap:Envelope>'
```

---

### Test Senaryolar?

#### Senaryo 1: Ba?ar?l? Gönderim
```
1. Consumer çal???yor: dotnet run
2. WCFServiceRabbitMQ çal???yor
3. Test client ? "Senaryo1Test"

Beklenen:
- Logs: 1 sat?r ("Senaryo1Test")
- MQMessages: 1 sat?r ("Senaryo1Test")
- Consumer: "Mesaj al?nd?: Senaryo1Test"
```

#### Senaryo 2: RabbitMQ Offline
```
1. RabbitMQ durdur
2. Test client ? "Senaryo2Test"

Beklenen:
- Exception: BrokerUnreachableException
- Logs: 0 sat?r (exception LogInfo'ye ula?mad?)
```

#### Senaryo 3: Consumer Offline
```
1. Consumer durdur
2. Mesajlar? gönder: "Msg1", "Msg2", "Msg3"

Beklenen:
- Logs: 3 sat?r
- MQMessages: 0 sat?r (henüz i?lenmediler)
- RabbitMQ: 3 mesaj beklemede

Consumer ba?lat?rken:
- 3 mesaj i?lenir
- MQMessages: 3 sat?r
```

#### Senaryo 4: Yüksek Hacim
```
for (int i = 1; i <= 1000; i++) {
    client.SendMessage($"Bulk message {i}");
}

Performance:
- Throughput: ~100 msg/sec
- Latency: ~50-100ms (average)
- Queue depth: 5-20 mesaj
```

---

## ?? KURULUM VE DA?ITIM

### Geli?tirme Ortam? (Local)

#### 1. Repository Klonla
```bash
git clone https://github.com/aknaldemir/DotnetSampleServices
cd DotnetSampleServices
```

#### 2. RabbitMQ Ba?lat (Docker)
```bash
docker run -d --name rabbitmq \
  -p 5672:5672 \
  -p 15672:15672 \
  -e RABBITMQ_DEFAULT_USER=guest \
  -e RABBITMQ_DEFAULT_PASS=guest \
  rabbitmq:4-management-alpine

# Management UI: http://localhost:15672
```

#### 3. SQL Server Ba?lat (Docker)
```bash
docker run -d --name sqlserver \
  -p 1433:1433 \
  -e SA_PASSWORD="YourPassword123!" \
  -e ACCEPT_EULA="Y" \
  mcr.microsoft.com/mssql/server:2019-latest
```

#### 4. Veritaban? Setup
```powershell
# SQL Server Management Studio'da ya da:

sqlcmd -S 127.0.0.1,1433 -U sa -P "YourPassword123!" << EOF
USE master;
CREATE DATABASE Toki;
GO
USE Toki;
GO

CREATE TABLE dbo.Logs (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    [Text] NVARCHAR(MAX),
    CreatedAt DATETIME DEFAULT GETDATE()
);

CREATE TABLE dbo.MQMessages (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    [Message] NVARCHAR(MAX),
    ProcessedAt DATETIME DEFAULT GETDATE()
);
EOF
```

#### 5. Consumer Çal??t?r
```bash
cd RabbitMQ.Consumer
dotnet run --configuration Release

# Output:
# info: RabbitMQ.Consumer.Worker[0]
#       RabbitMQ ba?lant?s? kuruldu (v7.x)
```

#### 6. WCF Ba?lat
```
Visual Studio:
1. Open Solution
2. WCFServiceRabbitMQ ? Set as Startup Project
3. F5 (IIS Express'te ba?la)
```

### Üretim Ortam? (Server)

#### WCFServiceRabbitMQ - IIS'e Da??t

```powershell
# 1. Publish
cd WCFServiceRabbitMQ
dotnet publish -c Release -o "C:\inetpub\wwwroot\MessageDispatcher"

# 2. IIS'de uygulama olu?tur
Import-Module WebAdministration

New-WebAppPool -Name "MessageDispatcher" -RuntimeVersion "4.0" -Force

New-WebApplication -Name "MessageDispatcher" `
    -Site "Default Web Site" `
    -PhysicalPath "C:\inetpub\wwwroot\MessageDispatcher" `
    -ApplicationPool "MessageDispatcher" `
    -Force

# 3. Test
Start-BitsTransfer -Source "http://localhost/MessageDispatcher/MessageDispatcherService.svc" `
    -Destination "C:\Temp\test.html"
```

#### RabbitMQ.Consumer - Windows Service

```powershell
# 1. Publish
cd RabbitMQ.Consumer
dotnet publish -c Release -o "C:\Services\RabbitMQ.Consumer"

# 2. Service olu?tur (NSSM ile)
# NSSM indir: https://nssm.cc/download

nssm install RabbitMQConsumer "C:\Services\RabbitMQ.Consumer\RabbitMQ.Consumer.exe"
nssm set RabbitMQConsumer AppDirectory "C:\Services\RabbitMQ.Consumer"
nssm set RabbitMQConsumer Start SERVICE_AUTO_START

# 3. Service ba?lat
Start-Service RabbitMQConsumer

# 4. Kontrol
Get-Service RabbitMQConsumer
```

#### Firewall Rules

```powershell
# HTTP
New-NetFirewallRule -DisplayName "Allow HTTP" `
    -Direction Inbound -Action Allow -Protocol TCP -LocalPort 80

# HTTPS
New-NetFirewallRule -DisplayName "Allow HTTPS" `
    -Direction Inbound -Action Allow -Protocol TCP -LocalPort 443

# RabbitMQ
New-NetFirewallRule -DisplayName "Allow RabbitMQ" `
    -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5672

# SQL Server
New-NetFirewallRule -DisplayName "Allow SQL" `
    -Direction Inbound -Action Allow -Protocol TCP -LocalPort 1433
```

---

## ?? ?LET? ??LEME DETAYLARI

### Message Publishing Flow

```csharp
// 1. Mesaj alma
public void SendMessage(string message) { }  // "Merhaba"

// 2. Publisher'da i?lem
channel.BasicPublish(
    exchange: "app.direct.exchange",
    routingKey: "app.routing.create",
    basicProperties: properties,  // Persistent=true
    body: Encoding.UTF8.GetBytes(message)
);

// ?çeride RabbitMQ:
// 1. Message ? exchange gider
// 2. Direct type ? routingKey e?le?: "app.routing.create" ?
// 3. Queue bulundu: "app.queue"
// 4. Message ? queue konur
// 5. Status: Ready=1, Unacked=0
```

### Message Consuming Flow

```csharp
// 1. Consumer haz?rlan?r
var consumer = new AsyncEventingBasicConsumer(channel);

// 2. Event handler kurulur
consumer.ReceivedAsync += async (_, ea) =>
{
    try
    {
        // Message al?nd?
        var message = Encoding.UTF8.GetString(ea.Body.ToArray());
        // "Merhaba" decode
        
        // Process
        await ProcessMessageAsync(message);
        // SQL INSERT
        
        // ACK
        await channel.BasicAckAsync(ea.DeliveryTag, false);
        // RabbitMQ: "Sildin mesaj?"
    }
    catch (Exception ex)
    {
        // NACK
        await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: true);
        // RabbitMQ: "Hata, kuyru?a geri koy"
    }
};

// 3. Consuming ba?lat?l?r
await channel.BasicConsumeAsync(
    queue: "app.queue",
    autoAck: false,  // Manuel ack gerekli
    consumer: consumer
);
```

### QoS ve Prefetch Timing

```
Zaman: T=0
????????????????         ??????????????????
? RabbitMQ     ? Message ? Consumer (v1)  ?
? Queue        ??????????? Prefetch: 5    ?
? ???????????? ?         ? ?????????????? ?
? ? Msg 1    ? ? ?????   ? ? Msg 1: ... ? ?
? ? Msg 2    ? ? ?????   ? ? Msg 2: ... ? ?
? ? Msg 3    ? ? ?????   ? ? Msg 3: ... ? ?
? ? Msg 4    ? ? ?????   ? ? Msg 4: ... ? ?
? ? Msg 5    ? ? ?????   ? ? Msg 5: ... ? ?
? ? Msg 6    ? ?         ? ? (waiting)  ? ?
? ? Msg 7    ? ?         ? ?????????????? ?
? ???????????? ?         ?                ?
????????????????         ??????????????????

Zaman: T=100ms (Msg 1 i?lendi)
Consumer.BasicAckAsync(Msg 1)
?
RabbitMQ: Msg 1 remove
RabbitMQ: Msg 6 ? Consumer gönder

????????????????         ??????????????????
? RabbitMQ     ?         ? Consumer (v1)  ?
? Queue        ?         ? Prefetch: 5    ?
? ???????????? ?         ? ?????????????? ?
? ? Msg 2    ? ?         ? ? Msg 2: ... ? ?
? ? Msg 3    ? ? ?????   ? ? Msg 3: ... ? ?
? ? Msg 4    ? ? ?????   ? ? Msg 4: ... ? ?
? ? Msg 5    ? ? ?????   ? ? Msg 5: ... ? ?
? ? Msg 6    ? ? ?????   ? ? Msg 6: ... ? ?
? ? Msg 7    ? ?         ? ? (waiting)  ? ?
? ? Msg 8    ? ?         ? ?????????????? ?
? ???????????? ?         ?                ?
????????????????         ??????????????????

Result: Continuous pipelining
```

---

## ? ERROR HANDLING

### Exception Types

```csharp
// Producer
catch (BrokerUnreachableException ex)
    // RabbitMQ servisi DOWN
    // Ba?lant? ba?ar?s?z

catch (SqlException ex)
    // Veritaban? ba?lant?s? veya komut hatas?
    // ?ifre yanl??, server offline, etc.

catch (Exception ex)
    // Di?er hatalar
    // Messaging pipeline errors


// Consumer
catch (TimeoutException ex)
    // Transient: Network latency
    // Requeue: true ?

catch (OperationCanceledException ex)
    // Transient: CancellationToken tetiklendi
    // Requeue: true ?

catch (FormatException ex)
    // Permanent: Message format geçersiz
    // Requeue: false ? DLQ

catch (SqlException ex)
    // Transient veya Permanent?
    // Context'e ba?l?:
    // - Timeout ? Transient ? Requeue: true
    // - Syntax error ? Permanent ? DLQ
```

### Ideal DLQ Implementation

```csharp
private async Task InitDeadLetterQueueAsync()
{
    // DLQ exchange ve queue olu?tur
    await _channel.ExchangeDeclareAsync("dlx.exchange", "direct", true);
    await _channel.QueueDeclareAsync(
        "dlq.queue",
        durable: true,
        exclusive: false,
        autoDelete: false,
        arguments: new Dictionary<string, object>()
    );
    
    await _channel.QueueBindAsync(
        "dlq.queue",
        "dlx.exchange",
        "dlx.*"
    );
}

// Consumer'da:
if (IsTransient(ex))
{
    // Requeue ? ba?ka consumer'lar alabilir
    await _channel.BasicNackAsync(ea.DeliveryTag, false, requeue: true);
}
else
{
    // Permanent ? DLQ'ye gönder
    var deadLetterProperties = _channel.CreateBasicProperties();
    deadLetterProperties.Headers = new Dictionary<string, object>
    {
        { "x-original-error", ex.Message },
        { "x-original-message", message }
    };
    
    await _channel.BasicPublishAsync(
        "dlx.exchange",
        "dlx.error",
        deadLetterProperties,
        ea.Body
    );
    
    // Orijinal mesaj? sil
    await _channel.BasicNackAsync(ea.DeliveryTag, false, requeue: false);
}
```

---

## ?? GÜVENL?K

### Credentials Management

? **YANLI?** (Hard-coded):
```csharp
var factory = new ConnectionFactory
{
    HostName = "localhost",     // Hard-coded!
    UserName = "guest",         // Hard-coded!
    Password = "guest"          // Hard-coded!
};

var connectionString = 
    "Server=192.168.1.102,1433;User ID=sa;Password=ak2100382;...";
    // Hard-coded!
```

? **DO?RU** (appsettings.json):
```json
{
  "RabbitMQSettings": {
    "Host": "192.168.1.103",
    "Username": "guest",
    "Password": "guest"
  },
  "ConnectionStrings": {
    "DbConnection": "..."
  }
}
```

? **DAHA ?Y?** (Environment Variables):
```bash
export RABBITMQ_HOST=192.168.1.103
export RABBITMQ_USER=guest
export RABBITMQ_PASS=guest
export SQL_CONNECTION_STRING=...
```

? **EN ?Y?** (Azure Key Vault):
```csharp
var keyVaultUrl = "https://mykeyvault.vault.azure.net/";
var credential = new DefaultAzureCredential();
var client = new SecretClient(new Uri(keyVaultUrl), credential);

var password = (await client.GetSecretAsync("RabbitMQPassword")).Value.Value;
```

### WCF Security

```csharp
// ?u anki: AÇIK
[OperationBehavior]
public void SendMessage(string message) { }  // Herkes ça??rabilir

// Do?ru: BasicAuth
[OperationBehavior(Impersonation = ImpersonationOption.Required)]
public void SendMessage(string message)
{
    if (ServiceSecurityContext.Current?.PrimaryIdentity?.IsAuthenticated != true)
        throw new FaultException("Unauthorized");
}

// Daha iyi: Bearer Token (JWT)
public void SendMessage(string message)
{
    var authHeader = OperationContext.Current.IncomingMessageHeaders
        .GetHeader<string>("Authorization", "");
    
    var token = authHeader.Replace("Bearer ", "");
    var handler = new JwtSecurityTokenHandler();
    handler.ValidateToken(token, validationParameters, out _);
}
```

### SQL Injection Prevention

? **Do?ru** (SqlParameter):
```csharp
cmd.CommandText = "INSERT INTO Logs (Text) VALUES (@Text)";
cmd.Parameters.AddWithValue("@Text", userInput);  // ? Safe
```

? **YANLI?** (String concatenation):
```csharp
cmd.CommandText = $"INSERT INTO Logs (Text) VALUES ('{userInput}')";  // ? Vulnerable!
```

---

## ?? PERFORMANCE

### Latency Analysis

```
Total Path: ~50-100ms

0ms     ?? Client POST request
5ms     ?? WCF ProcessMessage()
10ms    ?? RabbitMqProducer.Publish()
15ms    ?? RabbitMQ BasicPublish()
20ms    ?? Message in Queue (waiting)
30ms    ?? Consumer detects message
35ms    ?? ProcessMessageAsync()
40ms    ?? SQL INSERT
50ms    ?? BasicAckAsync()
60ms    ?? Response to client
~100ms  ?? Total roundtrip
```

### Throughput

```
Senaryo 1: Single Consumer
??????????????????????????????????
? Messages/sec: 100              ?
? Latency: 50-100ms              ?
? Throughput: 8,640/hour         ?
??????????????????????????????????

Senaryo 2: 5 Consumers
??????????????????????????????????
? Messages/sec: 100 × 5 = 500    ?
? Latency: 50-100ms (per msg)    ?
? Throughput: 43,200/hour        ?
??????????????????????????????????

Bottlenecks:
1. SQL INSERT (~10ms)
2. Network latency (~5ms)
3. RabbitMQ processing (~2ms)

Optimization:
?? Batch SQL inserts (20ms ? 5ms)
?? Connection pooling
?? Index tuning
?? More consumers
```

### PrefetchCount Tuning

```
PrefetchCount = 1
?? Safest (no message loss risk)
?? Latency: High (consumer waits)
?? Throughput: Low

PrefetchCount = 5 (Current)
?? Balanced
?? Latency: Medium
?? Throughput: Good (43,200/hour)

PrefetchCount = 100
?? Fastest
?? Risk: Consumer crash ? 100 msgs lost
?? Throughput: Highest

Recommendation:
?? Development: 5
?? Production (single consumer): 10-20
?? Production (many consumers): 5-10
```

---

## ?? SKALANLANDIRILMA

### Horizontal Scaling

```
Tek Consumer:
???????????????
? Consumer 1  ?
???????????????
       ?
       ?? Msg 1: Processing
       ?? Msg 2: Processing
       ?? Msg 3: Processing
       ?? Throughput: 100 msg/sec


Birden Fazla Consumer:
????????????????????????
? Consumer 1           ?
?? Msg 1: Processing  ?
????????????????????????

????????????????????????
? Consumer 2           ?
?? Msg 2: Processing  ?
????????????????????????

????????????????????????
? Consumer 3           ?
?? Msg 3: Processing  ?
????????????????????????

         ? All consumers listen same queue

???????????????????
?  app.queue      ?
? Ready: 0        ?  (RabbitMQ load-balances)
? Unacked: 3      ?
???????????????????

Throughput: 100 × 3 = 300 msg/sec
```

### Kubernetes Deployment

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: rabbitmq-consumer
spec:
  replicas: 5  # 5 consumer pods
  selector:
    matchLabels:
      app: rabbitmq-consumer
  template:
    metadata:
      labels:
        app: rabbitmq-consumer
    spec:
      containers:
      - name: consumer
        image: myregistry.azurecr.io/rabbitmq-consumer:1.0
        env:
        - name: RABBITMQ_HOST
          valueFrom:
            configMapKeyRef:
              name: app-config
              key: rabbitmq-host
        - name: RABBITMQ_USER
          valueFrom:
            secretKeyRef:
              name: app-secrets
              key: rabbitmq-user
        - name: RABBITMQ_PASS
          valueFrom:
            secretKeyRef:
              name: app-secrets
              key: rabbitmq-pass
        resources:
          requests:
            memory: "128Mi"
            cpu: "100m"
          limits:
            memory: "256Mi"
            cpu: "500m"
        livenessProbe:
          exec:
            command:
            - /bin/sh
            - -c
            - ps aux | grep RabbitMQ.Consumer
          initialDelaySeconds: 10
          periodSeconds: 30
```

---

## ?? DEBUGGING

### RabbitMQ Management UI
```
URL: http://192.168.1.103:15672
User: guest
Pass: guest

Kontrol Noktalar?:
?? Connections: Kaç consumer ba?l??
?? Channels: Kaç kanal aç?k?
?? Queues
?  ?? app.queue
?     ?? Ready: Kaç mesaj beklemede?
?     ?? Unacked: Kaç mesaj i?leniyor?
?     ?? Total Consumers: Kaç consumer?
?     ?? Ack rate: Kaç msg/sec ACK?
?? Messages
   ?? Publish rate: Kaç msg/sec?
```

### Consumer Log Monitoring
```powershell
# Windows Service logs
Get-EventLog -LogName Application -Source RabbitMQConsumer -Newest 50

# Or file-based logging
Get-Content "C:\Logs\consumer.log" -Tail 100

# Output gözle:
# - "Mesaj al?nd?: ..." ? Success
# - "Mesaj i?lenemedi" ? Error
# - "RabbitMQ ba?lant?s? kuruldu" ? Startup
```

### Common Issues & Solutions

**Problem: Consumer mesaj alm?yor**
```
Debug steps:
1. Service çal???yor mu?
   Get-Service RabbitMQConsumer

2. RabbitMQ'ya ba?lanabiliyor mu?
   Test-NetConnection -ComputerName 192.168.1.103 -Port 5672

3. Queue'de mesajlar var m??
   RabbitMQ UI ? Queues

4. Prefetch=5 ve hiç consumer ba?l? m??
   RabbitMQ UI ? Queues ? app.queue ? Consumer count

5. Consumer crash olmu? mu?
   Event Viewer ? Application ? Errors
```

**Problem: BrokerUnreachableException**
```
Nedenler:
?? RabbitMQ servisi DOWN
?  ?? Fix: docker start rabbitmq
?? Firewall 5672 blokl?yor
?  ?? Fix: netsh advfirewall firewall add rule ...
?? Yanl?? host/port
?  ?? Fix: appsettings.json kontrol et
?? Network connectivity
   ?? Fix: ping 192.168.1.103
```

**Problem: SqlException - Login failed**
```
Nedenler:
?? SQL Server servisi DOWN
?? Yanl?? kullan?c?/?ifre
?? Veritaban? mevcut de?il
?? Server firewall

Fix:
1. sqlcmd -S server -U sa -P password
2. Connection string kontrol et
3. appsettings.json güncel mi?
```

---

## ? BEST PRACTICES

### Yap?lmas? Gerekenler ?

```
1. ? Use SqlParameter (SQL Injection prevention)
   cmd.Parameters.AddWithValue("@Message", message);

2. ? Explicit ACK (Data integrity)
   autoAck: false,
   BasicAckAsync() on success

3. ? Error logging (Debugging)
   _logger.LogError(ex, "...");

4. ? Connection string from config
   appsettings.json or Key Vault

5. ? Credentials from environment
   Environment variables or secrets

6. ? Monitoring & alerting
   Application Insights, DataDog

7. ? Health checks
   Liveness, readiness probes

8. ? DLQ for permanent errors
   Dead Letter Queue setup
```

### Yap?lmamas? Gerekenler ?

```
1. ? Hard-coded credentials
   password = "guest"  // NEVER!

2. ? Auto-ack without verification
   autoAck: true  // Risk: Message loss

3. ? No error handling
   // Silent failures

4. ? String concatenation for SQL
   $"INSERT ... WHERE id = {id}"

5. ? Single connection
   // Scalability issue

6. ? No monitoring
   // Can't diagnose issues

7. ? Infinite requeue loops
   // Always requeue ? CPU waste

8. ? Storing secrets in git
   // Security breach
```

---

## ?? SIK SORULAN SORULAR (FAQ)

### Mimari Kararlar?

**S1: Neden RabbitMQ kullan?yoruz?**

C: RabbitMQ asenkron ileti i?lemesi için idealdir:
- Decoupling (Producer/Consumer ba??ms?z)
- Reliability (Durable queues, ACK/NACK)
- Scalability (Horizontal scaling kolay)
- Maturity (Üretim-tested)

Alternatifler: Azure Service Bus, Kafka, Redis

**S2: Neden WCF kullan?yoruz?**

C: .NET Framework 4.7.2 uyumlulu?u ve enterprise integration:
- SOAP/XML standardlar?
- Service contracts (type-safe)
- Built-in security features
- Legacy system compatibility

Modernize: ASP.NET Core + REST API

**S3: Neden Direct Exchange?**

C: Basit, h?zl?, deterministic routing:
```
Direct:  RoutingKey exact match
         ? H?zl?, bir queue bir consumer

Topic:   RoutingKey pattern (wildcard)
         ? Flexible, multi-consumer
```

?htiyaca göre migrate edilebilir.

**S4: Neden single connection?**

C: Basitlik ve efficiency:
```
Single conn:  Tüm istekler reuse
              ? Memory-efficient
              ? Uygun dev/test

Multiple:     Connection pool
              ? Complex management
              ? Better production
```

Üretim'de: Connection pool + auto-recovery

---

### ?? Mant???

**S5: Message nerede depolan?yor?**

C: Üç yerde:
```
1. RabbitMQ Queue (geçici, in-memory)
   - Consumer almay? bekler
   - Startup'ta kaybolabilir (durable=false ise)

2. Logs tablosu (SQL)
   - Producer taraf?ndan insert
   - Durable, permanent

3. MQMessages tablosu (SQL)
   - Consumer taraf?ndan insert
   - Durable, permanent
```

**S6: Consumer crash olursa ne olur?**

C: Iki senaryo:
```
Senario 1: Mid-processing crash
?? Consumer reconnect otomatik
?? Unack mesaj queue'ye geri konur
?? Ba?ka consumer yeniden i?ler

Senario 2: PrefetchCount=5, hepsi unack
?? 5 mesaj consumer memory'de
?? Consumer crash ? hiçbir ack
?? Timeout sonras?nda (default 30min)
?? Queue'ye geri konur
```

**S7: Duplicate message nas?l handle edilir?**

C: Idempotency pattern:
```csharp
// ? Kötü: ?lk INSERT ba?ar?s?z, sonra tekrar ? duplicate
// ? ?yi: MessageId unique constraint

CREATE TABLE MQMessages (
    Id INT PRIMARY KEY,
    MessageId NVARCHAR(50) UNIQUE,  // ? Key!
    Message NVARCHAR(MAX),
    ProcessedAt DATETIME
);

// Same message twice ? duplicate key exception
// Yeniden i?lenen mesaj ignor? edilir
```

---

### Güvenlik

**S8: Production'da credentials hard-code mi?**

C: **HAYIR!**

```csharp
// ? NEVER
var password = "guest";

// ? Use environment variable
var password = Environment.GetEnvironmentVariable("RABBITMQ_PASS");

// ? Use appsettings (machine-specific)
// .gitignore'a appsettings.Production.json ekle

// ?? Use Azure Key Vault (BEST)
var client = new SecretClient(keyVaultUri, new DefaultAzureCredential());
var secret = await client.GetSecretAsync("RabbitMQPassword");
```

**S9: WCF'ye authentication ekleyelim mi?**

C: Kesinlikle!

```csharp
// ? BasicAuth (simple)
[OperationBehavior(Impersonation = ImpersonationOption.Required)]
public void SendMessage(string message)
{
    if (!ServiceSecurityContext.Current.PrimaryIdentity.IsAuthenticated)
        throw new FaultException("Unauthorized");
}

// ?? Bearer Token/JWT (modern)
// ASP.NET Core'a migrate et + [Authorize]
```

**S10: SQL injection riski var m??**

C: **YOK!** SqlParameter kullan?l?yor:

```csharp
// ? Safe
cmd.Parameters.AddWithValue("@Text", userInput);

// ? Unsafe (kodda yok)
cmd.CommandText = $"INSERT ... VALUES ('{userInput}')";
```

---

### Debugging

**S11: Consumer mesaj alm?yor ama queue'de mesajlar var?**

C: Debug checklist:

```
1. Service çal???yor m??
   Get-Service RabbitMQConsumer ? Running?

2. RabbitMQ ba?lant?s? var m??
   Consumer logs: "RabbitMQ ba?lant?s? kuruldu"?

3. Prefetch maxed out?
   RabbitMQ UI ? Queues ? Consumer count
   (If 0 ? Hiçbir consumer ba?l? de?il!)

4. Configuration do?ru mu?
   appsettings.json ? Host, Port, Queue name

5. Network connectivity?
   Test-NetConnection -ComputerName 192.168.1.103 -Port 5672
```

**S12: "BrokerUnreachableException" nas?l düzeltilir?**

C: Ad?m ad?m:

```
1. RabbitMQ çal???yor mu?
   docker ps | grep rabbitmq

2. Port aç?k m??
   netstat -ano | findstr :5672

3. Firewall izin veriyor mu?
   netsh advfirewall firewall show rule name="RabbitMQ"

4. Host/Port do?ru mu?
   appsettings.json kontrol et

5. DNS resolution?
   ping 192.168.1.103
```

---

### Scaling & Performance

**S13: Consumer'lar? nas?l scale ederiz?**

C: Horizontal scaling:

```
1 Consumer:    100 msg/sec
5 Consumers:   500 msg/sec  (auto load-balance)
10 Consumers: 1000 msg/sec

RabbitMQ mesajlar? otomatik da??t?r.

Kubernetes'te:
replicas: 5  # 5 pod

Docker Compose:
services:
  consumer:
    deploy:
      replicas: 5
```

**S14: Latency nas?l optimize edilir?**

C: Tuning parameters:

```
1. PrefetchCount: 5 ? 10-20
   (Daha fazla message pipelining)

2. Connection pooling
   (Single conn ? pool)

3. Batch SQL inserts
   (20 msg INSERT ? 1 transaction)

4. Index tuning
   CREATE INDEX ON Logs(CreatedAt)
   CREATE INDEX ON MQMessages(ProcessedAt)

5. Move to same region
   (Network latency minimize)
```

---

### Deployment

**S15: Development ? Production migration?**

C: Checklist:

```
Pre-Deployment:
?? Code review ?
?? Security scan ?
?? Load test ?
?? Backup test ?
?? Documentation ?

Deployment:
?? Maintenance window ?
?? Rollback plan ?
?? Deploy to staging ?
?? Smoke test ?

Post-Deployment:
?? Health check ?
?? Log monitoring ?
?? Performance baseline ?
?? Stakeholder notification ?
```

**S16: Zero-downtime deployment nas?l yap?l?r?**

C: Blue-Green deployment:

```
PRODUCTION (v1)
?? Blue environment: ACTIVE
?  ?? Load Balancer ? Blue
?  ?? Handling all traffic
?
?? Green environment: STANDBY
   ?? New v2 deployed
   ?? Health checks: OK

Switch:
1. Green: All health checks pass
2. LB switch: Blue ? Green (atomic)
3. All traffic ? Green v2
4. Blue: Idle (ready for rollback)

Rollback:
1. Issue detected
2. LB: Green ? Blue
3. Service restored in seconds
```

---

## ?? HIZLI REFERANS

### Komutlar

```bash
# RabbitMQ
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 -e RABBITMQ_DEFAULT_USER=guest -e RABBITMQ_DEFAULT_PASS=guest rabbitmq:4-management-alpine

# SQL Server
docker run -d --name sqlserver -p 1433:1433 -e SA_PASSWORD="YourPassword123!" -e ACCEPT_EULA="Y" mcr.microsoft.com/mssql/server:2019-latest

# Consumer
cd RabbitMQ.Consumer && dotnet run

# Build Release
dotnet publish -c Release -o "C:\Services\RabbitMQ.Consumer"

# Service
nssm install RabbitMQConsumer "C:\Services\RabbitMQ.Consumer\RabbitMQ.Consumer.exe"
Start-Service RabbitMQConsumer
```

### URLs

```
RabbitMQ Management: http://192.168.1.103:15672
WCF Endpoint: http://localhost/MessageDispatcher/MessageDispatcherService.svc
WCF WSDL: http://localhost/MessageDispatcher/MessageDispatcherService.svc?wsdl
```

### Connection Strings

```json
{
  "RabbitMQSettings": {
    "Host": "192.168.1.103",
    "Port": 5672,
    "Username": "guest",
    "Password": "guest",
    "Queue": "app.queue",
    "PrefetchCount": 5
  },
  "ConnectionStrings": {
    "DbConnection": "Server=192.168.1.102,1433;Database=Toki;User ID=sa;Password=***;MultipleActiveResultSets=True;TrustServerCertificate=True;"
  }
}
```

### SQL Queries

```sql
-- Kontrol
SELECT COUNT(*) FROM Logs;
SELECT COUNT(*) FROM MQMessages;

-- Clear (test için)
DELETE FROM Logs;
DELETE FROM MQMessages;

-- Stats
SELECT 
    'Logs' AS TableName, COUNT(*) AS Count 
    FROM Logs
UNION ALL
SELECT 
    'MQMessages' AS TableName, COUNT(*) 
    FROM MQMessages;
```

---

## ?? KAYNAKLAR

- **RabbitMQ**: https://www.rabbitmq.com/documentation.html
- **WCF**: https://learn.microsoft.com/en-us/dotnet/framework/wcf/
- **.NET Worker**: https://learn.microsoft.com/en-us/dotnet/core/extensions/workers
- **SQL Server**: https://learn.microsoft.com/en-us/sql/sql-server/

---

## ?? PROJE ?NFO

| Bilgi | De?er |
|-------|-------|
| **Repository** | https://github.com/aknaldemir/DotnetSampleServices |
| **License** | MIT |
| **Framework** | .NET Framework 4.7.2 + .NET 8 |
| **Message Broker** | RabbitMQ 3.10+ |
| **Database** | SQL Server 2016+ |
| **Version** | 1.0 |
| **Last Update** | 2024 |

