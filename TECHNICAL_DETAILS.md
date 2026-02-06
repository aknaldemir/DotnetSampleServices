# RabbitMQ ?mplementasyon Detaylar?

## ?? RabbitMQ Ba?lant? Detaylar?

### Producer (WCFServiceRabbitMQ) - ConnectionFactory

```csharp
var factory = new ConnectionFactory
{
    HostName = "localhost",           // RabbitMQ sunucusu
    Port = 5672,                      // AMQP portu
    UserName = "guest",               // Varsay?lan kullan?c?
    Password = "guest",               // Varsay?lan ?ifre
    DispatchConsumersAsync = true     // Async consumer event handler
};
```

### Consumer (RabbitMQ.Consumer) - ConnectionFactory

```csharp
var factory = new ConnectionFactory
{
    HostName = rabbitMqOptions.Host,
    Port = rabbitMqOptions.Port,
    UserName = rabbitMqOptions.Username,
    Password = rabbitMqOptions.Password
    // DispatchConsumersAsync varsay?lan: false (aç?k de?il ama çal???yor)
};
```

---

## ?? Mesaj Yay?n (Publishing) Detaylar?

### RabbitMqProducer.Publish() - Ak???

```csharp
public void Publish(string message)
{
    // 1. String ? Byte Array (UTF-8)
    var body = Encoding.UTF8.GetBytes(message);

    // 2. Mesaj özellikleri olu?tur
    var properties = channel.CreateBasicProperties();
    properties.Persistent = true;  // RabbitMQ sunucusu yeniden ba?lansa
                                   // mesaj disk'te kal?r ve geri yüklenir

    // 3. Mesaj? exchange'e yay?nla
    channel.BasicPublish(
        exchange: ExchangeName,          // "app.direct.exchange"
        routingKey: RoutingKey,          // "app.routing.create"
        basicProperties: properties,     // Persistent=true
        body: body                       // Message body
    );
}
```

### Ad?mlar Detayl?:

| Ad?m | ??lem | Amaç |
|------|-------|------|
| 1 | `Encoding.UTF8.GetBytes(message)` | Metni byte dizisine çevir |
| 2 | `Persistent = true` | Sunucu ar?zas?nda mesaj? koru |
| 3 | `BasicPublish()` | Mesaj? RabbitMQ'ya gönder |
| 4 | Exchange Routing | app.direct.exchange'den queue'ya yönlendir |
| 5 | Queue Storage | "app.queue" içinde depolan?r |

---

## ?? Queue Declaration Detaylar?

### Producer Side (RabbitMqProducer Constructor)

```csharp
// Exchange olu?tur
channel.ExchangeDeclare(
    exchange: "app.direct.exchange",    // Exchange ad?
    type: ExchangeType.Direct,          // Direct tipi (routing key e?le?mesi)
    durable: true,                      // Exchange kal?c?
    autoDelete: false                   // El ile silinmeli
);

// Queue olu?tur
channel.QueueDeclare(
    queue: "app.queue",
    durable: true,                      // Sunucu yeniden ba?lansa kal?r
    exclusive: false,                   // Di?er connection'lar da ba?lanabilir
    autoDelete: false                   // Consumer ba?lant?y? kesince silinmez
);

// Queue'yu Exchange'e ba?la
channel.QueueBind(
    queue: "app.queue",
    exchange: "app.direct.exchange",
    routingKey: "app.routing.create"    // Mesaj bu key ile gelmeli
);
```

### Consumer Side (Worker.InitRabbitMqAsync())

```csharp
// Ayn? ayarlar? tekrar yap (idempotent operasyon)
await _channel.QueueDeclareAsync(
    queue: rabbitMqOptions.Queue,       // "app.queue"
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: null
);

// QoS ayar? - Backpressure kontrolü
await _channel.BasicQosAsync(
    prefetchSize: 0,                    // Byte baz?nda limit yok
    prefetchCount: rabbitMqOptions.PrefetchCount,  // 5 mesaj
    global: false                       // Channel ba??na (ba?lant? ba??na de?il)
);
```

---

## ?? Mesaj Tüketim (Consuming) Detaylar?

### AsyncEventingBasicConsumer

```csharp
var consumer = new AsyncEventingBasicConsumer(_channel!);

// Mesaj al?nd???nda tetiklenecek event handler
consumer.ReceivedAsync += async (_, ea) =>
{
    try
    {
        // 1. Byte array'den string'e dönü?tür
        var message = Encoding.UTF8.GetString(ea.Body.ToArray());
        
        _logger.LogInformation("Mesaj al?nd?: {msg}", message);

        // 2. Mesaj? i?le
        await ProcessMessageAsync(message);

        // 3. RabbitMQ'ya: "Bu mesaj? ba?ar?yla i?ledim"
        await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Mesaj i?lenemedi");

        // 4. Hata varsa: Mesaj? kuyru?a geri koy
        await _channel!.BasicNackAsync(
            ea.DeliveryTag,
            multiple: false,
            requeue: true  // true=kuyru?a geri koy, false=DLQ'ya gönder
        );
    }
};

// 5. Kuyru?u tüket
await _channel!.BasicConsumeAsync(
    queue: rabbitMqOptions.Queue,      // "app.queue"
    autoAck: false,                    // Manuel ack gerekli (try-catch içinde)
    consumer: consumer
);

// 6. Service çal??maya devam et
await Task.Delay(Timeout.Infinite, stoppingToken);
```

### Event Handler Ak??? Görselle?tirilmi?:

```
???????????????????????????????????????????
?   Mesaj Al?nd? (ReceivedAsync Trigger)  ?
???????????????????????????????????????????
                 ?
                 ?
         ?????????????????
         ? try blok      ?
         ?????????????????
         ? 1. Decode     ?
         ? 2. Process    ?
         ? 3. BasicAck() ? ???? Ba?ar?l?
         ?????????????????
                 ?
         ??????????????????????
         ?                    ?
      Ba?ar?              Hata (catch)
         ?                    ?
         ?            ?????????????????
         ?            ? BasicNack()   ?
         ?            ? requeue:true  ? ???? Kuyra?a geri koy
         ?            ?????????????????
         ?
      Sonraki döngü
```

---

## ?? Veritaban? ??lemleri

### Producer - LogInfo()

```csharp
private void LogInfo(string message)
{
    var connectionString = ConfigurationManager
        .ConnectionStrings["DbConnection"].ConnectionString;

    using (var conn = new SqlConnection(connectionString))
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = @"
            INSERT INTO Logs (Text)
            VALUES (@Text)
        ";

        cmd.Parameters.AddWithValue("@Text", message);
        // ? SqlParameter kullan?l?yor - SQL Injection'dan güvenli

        conn.Open();
        cmd.ExecuteNonQuery();
        // ExecuteNonQuery: INSERT için uygun (sat?r say?s? döner)
    }
}
```

### Consumer - ProcessMessageAsync()

```csharp
private Task ProcessMessageAsync(string message)
{
    var connectionString = configuration.GetConnectionString("DbConnection");

    using (var conn = new Microsoft.Data.SqlClient.SqlConnection(connectionString))
    // ? Microsoft.Data.SqlClient (daha yeni, modern)
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = @"
            INSERT INTO MQMessages (Message)
            VALUES (@Message)
        ";

        cmd.Parameters.AddWithValue("@Message", message);
        // ? SqlParameter kullan?l?yor

        conn.Open();
        cmd.ExecuteNonQuery();
        // ExecuteNonQuery: INSERT için uygun
    }
    return Task.CompletedTask;
}
```

### Farklar:

| Özellik | Producer | Consumer |
|---------|----------|----------|
| SqlClient | System.Data.SqlClient | Microsoft.Data.SqlClient |
| Configuration | ConfigurationManager (app.config) | IConfiguration (appsettings.json) |
| Async | Senkron | Task döner (ama async de?il) |
| Tablo | Logs | MQMessages |

---

## ?? Lifecycle Detaylar?

### Worker Service Startup Sequence

```
Host.Run()
  ??? Worker.StartAsync()
      ??? InitRabbitMqAsync()
      ?   ??? ConnectionFactory olu?tur
      ?   ??? _connection = CreateConnection()
      ?   ??? _channel = CreateChannel()
      ?   ??? BasicQosAsync()
      ?   ??? QueueDeclareAsync()
      ?
      ??? base.StartAsync() ???? BackgroundService'in ba?lat?lmas?
```

```
Ard?ndan paralel olarak:
  ??? ExecuteAsync(stoppingToken)  ???? Asenkron çal???r
      ??? AsyncEventingBasicConsumer olu?tur
      ??? BasicConsumeAsync() ça?r?
      ??? Task.Delay(Timeout.Infinite) ???? Sonsuza kadar bekle
```

### Shutdown Sequence

```
Ctrl+C (StoppingToken tetiklenir)
  ??? ExecuteAsync() CancellationToken al?r
      ??? Task.Delay(..., stoppingToken) 
          ??? OperationCanceledException throw
              ??? ExecuteAsync() ç?kar
```

**Not**: Consumer kapat?l?rken RabbitMQ connection otomatik kapat?lm?yor. `IAsyncDisposable` eklenmelidir:

```csharp
public async ValueTask DisposeAsync()
{
    _channel?.Close();
    _connection?.Close();
}
```

---

## ?? Error Handling Detaylar?

### Producer Exception Handling

```csharp
public void SendMessage(string message)
{
    try
    {
        rabbitMqProducer.Publish(message);  // RabbitMQ'ya gönder
        LogInfo(message);                   // Ba?ar?y? logla
    }
    catch (Exception ex)
    {
        LogError(ex);
        throw;  // ???? WCF client'ina FaultContract olarak döner
    }
}
```

**Faultlar**:
- `RabbitMQ.Client.Exceptions.BrokerUnreachableException` - RabbitMQ ba?lant?s? ba?ar?s?z
- `System.IO.IOException` - A? hatas?
- `SqlException` - Veritaban? hatas?

### Consumer Exception Handling

```csharp
consumer.ReceivedAsync += async (_, ea) =>
{
    try
    {
        var message = Encoding.UTF8.GetString(ea.Body.ToArray());
        await ProcessMessageAsync(message);
        await _channel!.BasicAckAsync(ea.DeliveryTag, false);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Mesaj i?lenemedi");
        // BasicNack: requeue=true ? Mesaj kuyru?a geri konur
        // Ba?ka consumer'lar yeniden i?lemeye çal???r
        await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
    }
};
```

---

## ?? RabbitMQ Routing Logic

### Message Routing Flow

```
Producer ça?r?s?:
  channel.BasicPublish(
      exchange: "app.direct.exchange",
      routingKey: "app.routing.create",
      body: messageBytes
  )
  
RabbitMQ Inside:
  1. Message ? "app.direct.exchange" gider
  2. Direct exchange, routingKey'e (RoutingKey e?le?mesi) bakar
  3. Binding aramak:
     - Queue: "app.queue"
     - RoutingKey: "app.routing.create"
     ? E?LE?ME BULUNDU!
  4. Message ? "app.queue"'ye konur

Consumer:
  BasicConsumeAsync(queue: "app.queue")
  ? Message al?n?r
```

### Neden Direct Exchange?

```
Seçenekler:
1. Fanout  - Tüm queue'lara gönder (broadcast)
2. Topic   - Pattern matching ile gönder (#, *)
3. Headers - Message headers'a göre gönder
4. Direct  ? Tam RoutingKey e?le?mesi - EN HIZLI VE BELIRGIN
```

---

## ?? Message Flow Timing

```
T=0ms     Client ? WCFServiceRabbitMQ (HTTP)
T=10ms    Publish() ? RabbitMQ (AMQP)
T=20ms    Message in queue: "app.queue"
T=30ms    Consumer detects message (async event)
T=35ms    ProcessMessageAsync() ? SQL INSERT
T=40ms    BasicAckAsync() ? RabbitMQ
T=45ms    Message removed from queue
T=50ms    SQL Logs and MQMessages tablolar? güncellendi

Toplam: ~50ms (network latency dahil)
```

---

## ?? Multiple Consumers Pattern

E?er birden fazla Consumer instance çal??t?r?rsa:

```
Consumer 1            Consumer 2            Consumer 3
  ?                      ?                      ?
queue: "app.queue"
?? Message 1 ? ACK 1 (Consumer 1 al?r)
?? Message 2 ? ACK 2 (Consumer 2 al?r)
?? Message 3 ? ACK 3 (Consumer 3 al?r)
?? Message 4 ? ACK 1 (Consumer 1 al?r, ?imdi bo?ald?)
?? Message 5 ? ACK 2 (Consumer 2 al?r, ?imdi bo?ald?)
?? Message 6 ? ACK 3 (Consumer 3 al?r, ?imdi bo?ald?)

PrefetchCount: 5 ? Her consumer ayn? anda 5 mesaj tutabilir
RabbitMQ load-balance otomatik yapar
```

---

## ??? Mesaj Güvenli?i

### S?ral? ??leme (Order Guarantee)

```
app.queue üzerinden:
- Mesajlar s?rayla tüketilir
- Tek consumer ise: FIFO garantili
- Birden fazla consumer: S?ra garantisi YOK

S?ra garantisi için:
? Single consumer kullan VEYA
? Sharded queues kullan (queue ba??na bir consumer)
```

### Mesaj Kal?c?l??? (Persistence)

```
Durable: true ile:
- RabbitMQ server crashlarsa ? Messages disk'e yaz?l?r
- Recovery sonras? ? Mesajlar geri yüklenir

Producer:
  properties.Persistent = true

Queue:
  durable: true
  
Sonuç:
  ? End-to-end durability (tam koruma)
```

---

## ?? Performance Tuning

### PrefetchCount

```
PrefetchCount: 5 (?u anki)

PrefetchCount = 1:
  - En güvenli (ba?a ba? i?leme)
  - Slowest (Consumer idle olmaz ama az message)
  
PrefetchCount = 5:
  - Balanced (?u anki)
  - ?yi throughput
  
PrefetchCount = 1000:
  - Fastest (çok paralel)
  - Risk: Consumer crash ? 1000 mesaj requeue
```

### Connection Pooling

```
?u anki: Single connection per service

Üretim:
? Connection pool kullan (ConnectionFactory.AutomaticRecoveryEnabled)
? Multiple channels (ba?lant? ba??na birden fazla channel)
```

---

