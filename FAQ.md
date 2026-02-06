# ? S?kça Sorulan Sorular (FAQ)

## ??? Mimari ve Tasar?m

### S1: Neden RabbitMQ kullan?yoruz?

**C**: RabbitMQ ?u avantajlar? sunar:
- **Asenkron ?leti Tabanl?**: Producer ve Consumer ba??ms?z olarak çal??abilir
- **Güvenilirlik**: Durable queue'ler, ACK, NACKx mekanizmalar?
- **Ölçeklenebilirlik**: Birden fazla consumer otomatik load-balancing
- **Standart AMQP Protokolü**: Çe?itli diller/framework'ler destekler
- **Management UI**: Monitoring ve debugging kolay

**Alternatifler**:
- Azure Service Bus (Azure-native)
- Amazon SQS (AWS-native)
- Kafka (High-throughput, event streaming)
- Redis (In-memory, simpler use cases)

---

### S2: Neden WCF (Producer olarak) kullan?yoruz?

**C**: WCF seçilen nedenler:
- **.NET Framework 4.7.2 compatibility**: Legacy systemi desteklemek
- **SOAP/XML**: Enterprise integration patterns
- **Service Contract**: Type-safe, contract-based ileti?im
- **Built-in Security**: Authentication, encryption, message signing

**Modernize alternatifi**:
```csharp
// ASP.NET Core + REST API
app.MapPost("/api/messages", (SendMessageRequest req) => 
{
    rabbitMqProducer.Publish(req.Message);
    return Results.Ok();
});
```

---

### S3: Neden Direct Exchange kullan?yoruz? Topic olmaz m??

**C**: Direct vs Topic kar??la?t?rmas?:

```
Direct Exchange (?u anki):
???????????????
?  Producer   ?
???????????????
       ? RoutingKey: "app.routing.create"
       ?
????????????????????????
? app.direct.exchange  ?
????????????????????????
       ? Exact match: "app.routing.create"
       ?
????????????????
? app.queue    ?
????????????????

Topic Exchange (Alternatif):
???????????????
?  Producer   ?
???????????????
       ? RoutingKey: "messages.orders.create"
       ?
????????????????????????
? app.topic.exchange   ?
????????????????????????
       ? Pattern: "messages.orders.*"
       ?
??????????????????????????????????
? orders.queue | archive.queue    ? (multiple queues)
??????????????????????????????????
```

**Sonuç**: 
- Direct: Basit, h?zl?, bir queue ? bir consumer
- Topic: Kompleks, flexibility, multi-consumer patterns

?u an Direct yeterli, scalability için Topic'e migrate edilebilir.

---

### S4: Neden single connection/channel kullan?yoruz?

**C**: Geçerli tasar?m:

```csharp
// RabbitMqProducer (Singleton)
private readonly IConnection connection;  // Tüm isteklerde reuse
private readonly IModel channel;          // Tüm publish'ler burada

// RabbitMQ.Consumer (Single connection)
private IConnection? _connection;         // Worker ba?lang?c?nda aç?l?r
private IChannel? _channel;               // Tüm consume'lar burada
```

**Gerekçe**:
- Connection pool'? yönetmek kompleks
- RabbitMQ client otomatik reconnect destekler
- Memory-efficient (bir connection = tüm istekler)

**Üretim'de iyile?tirme**:
```csharp
// Multi-threaded scenarios için:
builder.Services.AddSingleton<IRabbitMqProducer>(sp =>
    new RabbitMqProducer(
        enableAutoRecovery: true,
        networkRecoveryInterval: TimeSpan.FromSeconds(10)
    )
);
```

---

## ?? ?? Mant???

### S5: Mesaj silindi?inde ne olur? (BasicNack)

**C**: ?ki senaryo:

```csharp
// Scenario 1: Normal ?artlarda
await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
// Sonuç: Mesaj queue'den silinir, tüm bitti

// Scenario 2: Hata olu?tu?unda
await _channel.BasicNackAsync(
    ea.DeliveryTag,
    multiple: false,
    requeue: true  // ? Bu k?s?m önemli
);
// requeue=true: Mesaj kuyru?a geri konur, ba?ka consumer al?r
// requeue=false: Mesaj Dead Letter Queue (DLQ) gider
```

**Pratik örnek**:
```
Durum 1: ProcessMessageAsync() hata att? (SQL connection lost)
  ?
  BasicNack(requeue: true)
  ?
  Mesaj queue'ye geri konur
  ?
  Consumer 1 tekrar al?r veya Consumer 2 al?r
  ?
  Network düzelirse ba?ar?yla i?lenir

Durum 2: ProcessMessageAsync() hatas?: "Invalid message format"
  ?
  BasicNack(requeue: true)
  ?
  Sonsuz loop (hep hata atacak)
  ?
  DLQ implementation gerekli:
    await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
    // Dead Letter Queue'ye gönder
```

---

### S6: Consumer crash olursa mesajlar ne olur?

**C**: RabbitMQ'nun 3 durumu:

```
Durum 1: Consumer crash, Message processing'te (mid-way)
?????????????????????????????????????????????
? RabbitMQ'da                              ?
? ?? Message 1: ACK ? (i?lendi, deleted)  ?
? ?? Message 2: PROCESSING... ? CRASH!    ?
? ?? Message 3: Waiting                   ?
? ?? Message 4: Waiting                   ?
?????????????????????????????????????????????
  ?
  Consumer reconnect olur (RabbitMQ client taraf?ndan)
  ?
  Message 2: Unack ? Queue'ye geri konur (Head'de)
  ?
  Consumer tekrar ba?lar, Message 2'den devam eder

Durum 2: PrefetchCount=5, 5 message al?nd? ama Consumer crash
?????????????????????????????????????????????
? Consumer Memory (Prefetch)               ?
? ?? Message A: Ack pending               ?
? ?? Message B: Ack pending               ?
? ?? Message C: Ack pending               ?
? ?? Message D: Ack pending               ?
? ?? Message E: Ack pending ? CRASH!      ?
?????????????????????????????????????????????
  ?
  5 message hiçbir ack almad??? için queue'de kal?r
  ?
  Timeout sonras?nda (Delivery Timeout) queue'ye geri konur
  ?
  Ba?ka consumer (veya ayn? consumer restarted) al?r
```

**Koruma Mekanizmalar?**:
```csharp
// 1. Explicit ACK (?u anki) ?
autoAck: false,  // Manual ack gerekli

// 2. PrefetchCount (?u anki)
prefetchCount: 5,  // 5 message'ten fazla alma

// 3. Auto Recovery (?u anki client'ta)
// RabbitMQ.Client otomatik reconnect eder

// 4. Deadletter Queue (Ek feature)
await _channel.QueueBindAsync(
    queue: "dlq.queue",
    exchange: "dlx.exchange",
    routingKey: "dlx.*"
);
```

---

### S7: ?f ayn? mesaj iki kere i?lenirse?

**C**: ?dempotency pattern uygulanmal?:

```csharp
// Kötü: Idempotent olmayan
private Task ProcessMessageAsync(string message)
{
    // Ayn? message iki kere gelirse, iki kere insert olur!
    // MessageId: 1, MessageId: 1 (duplicate!)
    
    using (var conn = new SqlConnection(connectionString))
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "INSERT INTO MQMessages (Message) VALUES (@Message)";
        cmd.Parameters.AddWithValue("@Message", message);
        conn.Open();
        cmd.ExecuteNonQuery();
    }
    return Task.CompletedTask;
}

// ?yi: Idempotent (Recommended)
private Task ProcessMessageAsync(string message)
{
    // Message unique ID içer: "123|Hello"
    var parts = message.Split('|');
    var messageId = parts[0];
    var content = parts[1];
    
    using (var conn = new SqlConnection(connectionString))
    using (var cmd = conn.CreateCommand())
    {
        // Unique constraint ile duplicate'i engelle
        cmd.CommandText = @"
            INSERT INTO MQMessages (MessageId, Message)
            VALUES (@MessageId, @Message)
        ";
        cmd.Parameters.AddWithValue("@MessageId", messageId);
        cmd.Parameters.AddWithValue("@Message", content);
        
        try
        {
            conn.Open();
            cmd.ExecuteNonQuery();
        }
        catch (SqlException ex) when (ex.Number == 2627) // Unique constraint
        {
            // Duplicate mesaj ? log ve devam et
            _logger.LogWarning("Duplicate message received: {msgId}", messageId);
        }
    }
    return Task.CompletedTask;
}
```

---

## ?? Güvenlik

### S8: Production'da credentials hard-code mi?

**C**: HAYIR! ?u anki code bu:

```csharp
// ? YANLI? (?u anki code'da)
var factory = new ConnectionFactory
{
    HostName = "localhost",  // Hard-coded!
    UserName = "guest",      // Hard-coded!
    Password = "guest"       // Hard-coded!
};
```

**Do?ru ?ekiller**:

```csharp
// 1. appsettings.json (?u anki RabbitMQ.Consumer)
{
  "RabbitMQSettings": {
    "Host": "${RABBITMQ_HOST}",  // Environment variable
    "Username": "${RABBITMQ_USER}",
    "Password": "${RABBITMQ_PASS}"
  }
}

// 2. Environment Variables (Best)
var host = Environment.GetEnvironmentVariable("RABBITMQ_HOST");
var username = Environment.GetEnvironmentVariable("RABBITMQ_USER");
var password = Environment.GetEnvironmentVariable("RABBITMQ_PASS");

// 3. Azure Key Vault (Recommended)
var keyVaultUrl = "https://mykeyvault.vault.azure.net/";
var credential = new DefaultAzureCredential();
var client = new SecretClient(new Uri(keyVaultUrl), credential);

KeyVaultSecret secret = client.GetSecret("RabbitMQPassword");
var password = secret.Value;

// 4. appsettings.Production.json (Machine-specific)
// Deployed machine'e sadece production config'i kopyala
```

---

### S9: SQL Connection String'i nereye saklayaca??z?

**C**: Güvenlik s?ras?:

```
Best:  Azure Key Vault ? Managed Identity
       ? (daha uygun)
Good:  Environment Variables ? Azure AppConfig
       ?
Fair:  appsettings.Production.json (Machine-specific, .gitignore'da)
       ?
Bad:   appsettings.json (Version control'de)
       ?
Worst: Hard-coded code (? Asla!)
```

**Uygulama**:
```csharp
// Program.cs
var builder = Host.CreateApplicationBuilder(args);

// Development: appsettings.json
// Production: Azure Key Vault
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .AddAzureKeyVault(
        vaultUri: new Uri(keyVaultUrl),
        credential: new DefaultAzureCredential()
    );

var configuration = builder.Configuration;
var connString = configuration.GetConnectionString("DbConnection");
```

---

### S10: WCF Service'e Authentication ekleyelim mi?

**C**: Kesinlikle! ?u anki implementation aç?k:

```csharp
// ? AÇIK - Herkes ça??rabilir
public void SendMessage(string message) { }

// ? Do?ru - BasicAuth ile
[OperationBehavior(Impersonation = ImpersonationOption.Required)]
public void SendMessage(string message)
{
    // WCF otomatik HTTP auth header'?n? kontrol eder
    ServiceSecurityContext context = ServiceSecurityContext.Current;
    if (context?.PrimaryIdentity?.IsAuthenticated != true)
        throw new FaultException("Unauthorized");
}

// ? Daha iyi - Token-based (JWT)
[ServiceBehavior(Namespace = "http://wcfservice")]
public class MessageDispatcherService : IMessageDispatcherService
{
    public void SendMessage(string message)
    {
        // JwtSecurityTokenHandler ile verify et
        var token = ExtractTokenFromHeader();
        var handler = new JwtSecurityTokenHandler();
        handler.ValidateToken(token, validationParameters, out _);
    }
}

// ? Modern - ASP.NET Core REST API
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MessagesController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Send([FromBody] SendMessageRequest req)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await _rabbitMqProducer.PublishAsync(req.Message, userId);
        return Ok();
    }
}
```

---

## ?? Ölçeklenme

### S11: Consumer'lar? nas?l scale ederiz?

**C**: Horizontal Scaling:

```
Tek Consumer:
????????????????     ??????????????
? Consumer 1   ??????? app.queue  ?
????????????????     ??????????????
  Throughput: 100 msg/sec
  
Birden fazla Consumer:
????????????????
? Consumer 1   ?
????????????????
? Consumer 2   ?    ??????????????
????????????????????? app.queue  ?
? Consumer 3   ?    ??????????????
????????????????
? Consumer N   ?
????????????????
  Throughput: 100 × N msg/sec
  
  Load Balancing:
  - Message 1 ? Consumer 1
  - Message 2 ? Consumer 2
  - Message 3 ? Consumer 3
  - Message 4 ? Consumer 1 (round-robin)
  ...
```

**Deployment**:
```yaml
# Kubernetes deployment.yaml
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
        image: myregistry.azurecr.io/rabbitmq-consumer:latest
        env:
        - name: RABBITMQ_HOST
          valueFrom:
            secretKeyRef:
              name: rabbitmq-secrets
              key: host
        - name: RABBITMQ_USER
          valueFrom:
            secretKeyRef:
              name: rabbitmq-secrets
              key: user
        - name: RABBITMQ_PASS
          valueFrom:
            secretKeyRef:
              name: rabbitmq-secrets
              key: password
```

---

### S12: Message latency'yi nas?l optimize ederiz?

**C**: Tuning parameters:

```csharp
// Consumer QoS tuning
await _channel.BasicQosAsync(
    prefetchSize: 0,
    prefetchCount: 10,  // default: 5 ? artt?r (daha fazla message yükleme)
    global: false
);

// Connection recovery tuning
var factory = new ConnectionFactory
{
    HostName = host,
    AutomaticRecoveryEnabled = true,
    NetworkRecoveryInterval = TimeSpan.FromSeconds(5),  // default: 10s
    RequestedChannelMax = 100,  // default: 0 (unlimited)
    RequestedConnectionTimeout = TimeSpan.FromSeconds(30)
};

// Database optimization
// ? Connection pooling
// ? Query optimization
// ? Batch inserts (100+ messages)

// Network optimization
// ? Local RabbitMQ (same data center)
// ? Network compression
```

---

## ?? Debugging

### S13: Consumer mesaj alm?yor ama queue'de mesajlar var?

**C**: Debug ad?mlar?:

```powershell
# 1. RabbitMQ Management UI kontrol et
http://localhost:15672
# Gözlemle:
# - Queue: app.queue
#   - Ready: X messages
#   - Unacked: Y messages
#   - Consumers: Z consumers ba?l? m??

# 2. Consumer service running m??
Get-Service RabbitMQConsumer
# Status: Running m??

# 3. Consumer logs kontrol et
Get-Content "C:\Logs\consumer.log" -Tail 50

# 4. Network connectivity
Test-NetConnection -ComputerName 192.168.1.102 -Port 5672

# 5. Consumer debug mode
dotnet run --configuration Debug
# Console'da "Mesaj al?nd?:" log'u görüyor musun?

# 6. Prefetch Check
# E?er Consumer crash olmu? ve prefetch=5 varsa
# ? 5 message pending ACK'de olur
# ? Timeout'u bekle (RabbitMQ default: 30 dakika)
# ? Veya ?u komut: rabbitmqctl reset
```

---

### S14: "BrokerUnreachableException" nas?l debug ederiz?

**C**:

```csharp
try
{
    var factory = new ConnectionFactory { HostName = host };
    var connection = await factory.CreateConnectionAsync();
}
catch (BrokerUnreachableException ex)
{
    // Inner exceptions'? kontrol et
    Console.WriteLine($"Exception: {ex.Message}");
    Console.WriteLine($"Inner: {ex.InnerException?.Message}");
    
    // Detayl? hata:
    // System.Net.Sockets.SocketException: No connection could be made...
    // ? RabbitMQ servisi DOWN
    
    // Detayl? hata:
    // System.Security.Authentication.AuthenticationException
    // ? Kullan?c?/?ifre yanl??
    
    // Detayl? hata:
    // System.TimeoutException
    // ? Network latency, firewall block
}
```

**Kontrol listesi**:
- [ ] `ping 192.168.1.102` - IP ula??labiliyor mu?
- [ ] `Test-NetConnection -ComputerName 192.168.1.102 -Port 5672` - Port aç?k m??
- [ ] `docker ps | grep rabbitmq` - Container çal???yor mu?
- [ ] `rabbitmqctl status` - RabbitMQ health OK m??
- [ ] Firewall kurallar?nda 5672 aç?k m??
- [ ] appsettings.json do?ru m??

---

### S15: SQL Insert hata veriyor, mesaj loop'a giriyor

**C**: Requeue logic yanl??, DLQ implement etmeliyiz:

```csharp
// ? ?u anki (ço?u hata requeue ediyor)
catch (Exception ex)
{
    _logger.LogError(ex, "Mesaj i?lenemedi");
    await _channel!.BasicNackAsync(ea.DeliveryTag, false, requeue: true);
    // Problem: Ayn? hata tekrar olacak ? infinite loop!
}

// ? Do?ru (Transient errors için requeue, permanent için DLQ)
catch (Exception ex)
{
    if (IsTransient(ex))  // Network, timeout gibi
    {
        _logger.LogWarning(ex, "Transient error, requeuing...");
        await _channel!.BasicNackAsync(ea.DeliveryTag, false, requeue: true);
    }
    else  // SQL syntax error, invalid message gibi
    {
        _logger.LogError(ex, "Permanent error, sending to DLQ...");
        await _channel!.BasicNackAsync(ea.DeliveryTag, false, requeue: false);
        // requeue: false ? message DLQ'ye gider
    }
}

private bool IsTransient(Exception ex) =>
    ex is TimeoutException ||
    ex is IOException ||
    ex is OperationCanceledException ||
    ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
```

---

## ?? Deployment

### S16: Development'ten Production'a nas?l migrate ederiz?

**C**: Checklist:

```
1. CODE PREPARATION
   ?? Remove debug flag (web.config)
   ?? Remove hardcoded values
   ?? Add appsettings.Production.json
   ?? Enable logging (Serilog, etc.)
   ?? Add error handling

2. INFRASTRUCTURE
   ?? RabbitMQ cluster kuruldu mu? (high availability)
   ?? SQL Server backup/restore tested mi?
   ?? IIS SSL sertifikas? kuruldu mu?
   ?? Firewall kurallar? aç?ld? m??
   ?? DNS configured mi?

3. DEPLOYMENT
   ?? Backup current (rollback için)
   ?? Deploy to production
   ?? Smoke tests çal??t?r
   ?? Monitor logs/metrics
   ?? Gradual rollout (canary deployment)

4. MONITORING
   ?? Application Insights setup
   ?? Custom metrics
   ?? Alerting configured
   ?? On-call schedule

5. DOCUMENTATION
   ?? Runbook yaz?ld? m??
   ?? Incident response plan ready?
   ?? Team trained mi?
   ?? Escalation path clear mi?
```

---

### S17: Zero-downtime deployment nas?l yapabiliriz?

**C**: Blue-Green Deployment:

```
PRODUCTION (v1)              STAGING (v2)
???????????????????         ???????????????????
? Message Sender  ?         ? New Version     ?
? ?               ?         ? (Tested, Ready) ?
? Load Balancer   ?         ???????????????????
? ?? Blue (v1)    ? ? Current
? ?? Green (v2)   ? ? Standby
???????????????????

Step 1: Green'de yeni version deploy et
Step 2: Green'de health checks
Step 3: LB'yi Green'e yönlendir (atomic switch)
Step 4: Tüm traffic Green'de
Step 5: Blue'yu reset et (next deployment'a haz?r)
```

```powershell
# PowerShell implementation
# IIS Application Pool swap

# 1. Green App Pool'u ba?lat
Start-WebAppPool -Name "MessageDispatcher-v2"

# 2. Health check
$health = Invoke-WebRequest -Uri "http://localhost:8081/health" -UseBasicParsing
if ($health.StatusCode -ne 200) { throw "Health check failed" }

# 3. Load Balancer'? switch et (ARR, Nginx, vb.)
# Tüm traffic green'e yönlenir

# 4. Blue'yu durdur
Stop-WebAppPool -Name "MessageDispatcher-v1"
```

---

## ?? Kaynaklar

### S18: Daha fazla bilgi için hangi kaynaklar? okumam gerekir?

**C**:

```
RabbitMQ:
?? Official Tutorial: https://www.rabbitmq.com/getstarted.html
?? .NET Client Library: https://www.rabbitmq.com/tutorials/tutorial-one-dotnet.html
?? Concepts: https://www.rabbitmq.com/tutorials/amqp-concepts.html
?? Patterns: https://www.rabbitmq.com/getstarted.html

WCF:
?? Microsoft Docs: https://learn.microsoft.com/en-us/dotnet/framework/wcf/
?? Best Practices: https://docs.microsoft.com/en-us/dotnet/framework/wcf/best-practices
?? Security: https://learn.microsoft.com/en-us/dotnet/framework/wcf/feature-details/security-guidance

.NET:
?? Worker Service: https://docs.microsoft.com/en-us/dotnet/core/extensions/workers
?? Dependency Injection: https://docs.microsoft.com/en-us/dotnet/core/extensions/dependency-injection
?? Configuration: https://docs.microsoft.com/en-us/dotnet/core/extensions/configuration

SQL Server:
?? Best Practices: https://docs.microsoft.com/en-us/sql/relational-databases/best-practices
?? Performance Tuning: https://docs.microsoft.com/en-us/sql/relational-databases/performance/monitoring-performance-by-using-the-query-store
?? Backup/Restore: https://docs.microsoft.com/en-us/sql/relational-databases/backup-restore/
```

---

