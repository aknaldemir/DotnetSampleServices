# WCF API Kullan?m K?lavuzu

## ?? Hizmet Bilgileri

```
Hizmet Ad?: MessageDispatcherService
Protokol: WCF (Windows Communication Foundation)
Binding: basicHttpBinding (varsay?lan)
Endpoint: http://localhost/WCFServiceRabbitMQ/MessageDispatcherService.svc
WSDL: http://localhost/WCFServiceRabbitMQ/MessageDispatcherService.svc?wsdl
```

---

## ?? Operasyonlar

### SendMessage(string message)

**Aç?klama**: Mesaj? RabbitMQ kuyru?una gönderir

**Giri? Parametreleri**:
| Parametre | Tip | Zorunlu | Aç?klama |
|-----------|-----|---------|----------|
| message | string | Evet | Gönderilecek mesaj metni |

**Ç?k??**: void (dönü? de?eri yok)

**Olas? Hatalar**:
- `BrokerUnreachableException` - RabbitMQ ba?lant?s? ba?ar?s?z
- `SqlException` - Veritaban? ba?lant? hatas?
- `Exception` (generic) - Beklenmeyen hata

---

## ?? Örnek Kullan?mlar

### 1?? WCF Test Client ile Test Etme (Visual Studio)

```
1. Visual Studio'da projeyi aç?n
2. Solution Explorer ? MessageDispatcherService.svc 
   sa? t?kla ? "Set as Start Page"
3. F5 ile debug start et
4. WCF Test Client otomatik aç?l?r
5. IMessageDispatcherService ? SendMessage() çift t?kla
6. Request Value: "Merhaba dünya"
7. Invoke t?kla
8. Response: (empty) veya Exception
```

**Beklenen Sonuç**:
```
Response: (empty)
?
Logs tablosuna eklendi:
  INSERT INTO Logs (Text) VALUES ('Merhaba dünya')
?
RabbitMQ'da message:
  exchange: app.direct.exchange
  queue: app.queue
?
Consumer al?yor ve i?liyor:
  INSERT INTO MQMessages (Message) VALUES ('Merhaba dünya')
```

### 2?? SOAP UI ile Test Etme

```xml
<!-- Request XML -->
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

```
POST http://localhost/WCFServiceRabbitMQ/MessageDispatcherService.svc
Content-Type: text/xml; charset=utf-8
SOAPAction: http://WCFServiceRabbitMQ/IMessageDispatcherService/SendMessage

[Yukar?daki XML'i body'ye yap??t?r]
[Send t?kla]
```

**Beklenen Response**:
```xml
<?xml version="1.0" encoding="utf-8"?>
<soap:Envelope 
    xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
    <soap:Body>
        <SendMessageResponse 
            xmlns="http://WCFServiceRabbitMQ"/>
    </soap:Body>
</soap:Envelope>
```

### 3?? PowerShell ile Test Etme

```powershell
# SOAP iste?i olu?tur
$uri = "http://localhost/WCFServiceRabbitMQ/MessageDispatcherService.svc"

$soapBody = @"
<?xml version="1.0" encoding="utf-8"?>
<soap:Envelope 
    xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" 
    xmlns:wcf="http://WCFServiceRabbitMQ">
    <soap:Body>
        <wcf:SendMessage>
            <wcf:message>PowerShell test message</wcf:message>
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

### 4?? C# Client ile Test Etme

```csharp
using System;
using System.ServiceModel;

// WSDL'den proxy class olu?tur
// Visual Studio ? Add Service Reference ? http://localhost/...?wsdl

var binding = new BasicHttpBinding();
var endpoint = new EndpointAddress("http://localhost/WCFServiceRabbitMQ/MessageDispatcherService.svc");
var client = new MessageDispatcherServiceClient(binding, endpoint);

try
{
    string message = "C# Client test message";
    client.SendMessage(message);
    Console.WriteLine("Mesaj ba?ar?yla gönderildi!");
}
catch (Exception ex)
{
    Console.WriteLine($"Hata: {ex.Message}");
}
finally
{
    client.Close();
}
```

### 5?? .NET Core HttpClient ile Test Etme

```csharp
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

var httpClient = new HttpClient();
var uri = "http://localhost/WCFServiceRabbitMQ/MessageDispatcherService.svc";

// SOAP Request olu?tur
var soapRequest = @"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope 
    xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" 
    xmlns:wcf=""http://WCFServiceRabbitMQ"">
    <soap:Body>
        <wcf:SendMessage>
            <wcf:message>Test message from .NET Core</wcf:message>
        </wcf:SendMessage>
    </soap:Body>
</soap:Envelope>";

var content = new StringContent(soapRequest, Encoding.UTF8, "text/xml");

try
{
    var response = await httpClient.PostAsync(uri, content);
    var responseContent = await response.Content.ReadAsStringAsync();
    
    if (response.IsSuccessStatusCode)
    {
        Console.WriteLine("? Mesaj ba?ar?yla gönderildi");
        Console.WriteLine(responseContent);
    }
    else
    {
        Console.WriteLine($"? Hata: {response.StatusCode}");
        Console.WriteLine(responseContent);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Exception: {ex.Message}");
}
```

### 6?? cURL ile Test Etme

```bash
curl -X POST \
  "http://localhost/WCFServiceRabbitMQ/MessageDispatcherService.svc" \
  -H "Content-Type: text/xml; charset=utf-8" \
  -H "SOAPAction: http://WCFServiceRabbitMQ/IMessageDispatcherService/SendMessage" \
  -d '<?xml version="1.0" encoding="utf-8"?>
<soap:Envelope 
    xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" 
    xmlns:wcf="http://WCFServiceRabbitMQ">
    <soap:Body>
        <wcf:SendMessage>
            <wcf:message>cURL test message</wcf:message>
        </wcf:SendMessage>
    </soap:Body>
</soap:Envelope>'
```

---

## ?? Entegrasyon Test Senaryolar?

### Senaryo 1: Ba?ar?l? Mesaj Gönderimi

**A?amalar**:
1. Consumer'? çal??t?r: `dotnet run` (RabbitMQ.Consumer/)
2. WCFServiceRabbitMQ'yu ba?lat (IIS Express)
3. Test client'tan mesaj gönder: "Senaryo1Test"

**Kontrol Noktalar?**:
```sql
-- SQL Server'da kontrol et:

-- Logs tablosu (Producer taraf?ndan)
SELECT * FROM Logs WHERE Text = 'Senaryo1Test'
-- Sonuç: 1 sat?r (ba?ar?l? gönderim logu)

-- MQMessages tablosu (Consumer taraf?ndan)
SELECT * FROM MQMessages WHERE Message = 'Senaryo1Test'
-- Sonuç: 1 sat?r (ba?ar?l? i?lem logu)

-- Consumer console'unda
-- "Mesaj al?nd?: Senaryo1Test"
```

### Senaryo 2: RabbitMQ Çevrimd??? (Offline)

**A?amalar**:
1. RabbitMQ'yu durdur
2. Test client'tan mesaj gönder

**Beklenen Sonuç**:
```
WCF Exception:
  BrokerUnreachableException: 
  None of the specified endpoints were reachable
  
WCF Client ç?kt?:
  "Sunucuda bir hata olu?tu"
```

**Logs Tablosu**:
```sql
-- Logs tablosunda kay?t YOK (LogInfo() exception att??? için)
```

### Senaryo 3: Consumer Çevrimd??? (Offline)

**A?amalar**:
1. Consumer'? durdur
2. Mesajlar? gönder: "Senaryo3Msg1", "Senaryo3Msg2", "Senaryo3Msg3"

**Beklenen Sonuç**:
```
WCF Response: Ba?ar?l? (mesajlar RabbitMQ'ya gitti)
RabbitMQ Queue: 3 mesaj bekleniyor
Logs Tablosu: 3 sat?r ("Senaryo3Msg1", "Senaryo3Msg2", "Senaryo3Msg3")
MQMessages Tablosu: 0 sat?r (i?lenemedi)

Consumer ba?lat?ld???nda:
  ? 3 mesaj al?n?r ve i?lenir
  ? 3 sat?r MQMessages'a eklenir
```

### Senaryo 4: Yüksek Hacim Test

**A?amalar**:
```csharp
// Test Script (PowerShell veya C#)
for (int i = 1; i <= 1000; i++)
{
    client.SendMessage($"Bulk message {i}");
    if (i % 100 == 0)
        System.Threading.Thread.Sleep(1000); // K?sa bekleme
}
```

**Kontrol Noktalar?**:
```sql
-- Test ba??nda
SELECT COUNT(*) FROM Logs;          -- Ba?lang?ç: X sat?r
SELECT COUNT(*) FROM MQMessages;    -- Ba?lang?ç: Y sat?r

-- Test s?ras?nda (30 saniye bekle)
SELECT COUNT(*) FROM Logs;          -- Beklenen: X+1000

-- Test sonras?nda (1-2 dakika bekle)
SELECT COUNT(*) FROM MQMessages;    -- Beklenen: Y+1000
```

**RabbitMQ Monitoring**:
```
http://192.168.1.102:15672
guest / guest

Gözlemlenecek:
- Queue depth (mesaj say?s?)
- Consumer rate (tüketim h?z?)
- Memory usage
```

---

## ?? Debugging ve Troubleshooting

### Problem 1: "BrokerUnreachableException"

**Tan?s?**:
```
Exception: 
  None of the specified endpoints were reachable
  
Nedenleri:
  1. RabbitMQ servisi çal??m?yor
  2. Firewall 5672 portunu engelliyor
  3. Yanl?? hostname/port
```

**Çözüm**:
```powershell
# RabbitMQ servisi kontrol et
Get-Process rabbitmq*

# Port aç?k m? kontrol et
Test-NetConnection -ComputerName 192.168.1.102 -Port 5672

# RabbitMQ Management UI aç?l?yor mu
Start-Process "http://192.168.1.102:15672"
```

### Problem 2: "SqlException - Login failed"

**Tan?s?**:
```
Exception:
  Login failed for user 'sa'
  
Nedenleri:
  1. SQL Server servisi çal??m?yor
  2. Yanl?? ?ifre
  3. Kullan?c? deaktif
  4. Server name yanl??
```

**Çözüm**:
```powershell
# SQL Server ba?lant? test et
$connectionString = "Server=192.168.1.102,1433;Database=master;User ID=sa;Password=ak2100382;TrustServerCertificate=True;"
$sqlConnection = New-Object System.Data.SqlClient.SqlConnection
$sqlConnection.ConnectionString = $connectionString
$sqlConnection.Open()
Write-Host "? Ba?lant? ba?ar?l?"
```

### Problem 3: "The type initializer for ... threw an exception"

**Tan?s?**:
```
Exception: 
  Static constructor hatas?
  
Nedenleri:
  1. app.config eksik veya yanl??
  2. Assembly binding yanl??
  3. Config encryption hatas?
```

**Çözüm**:
```xml
<!-- app.config kontrol et -->
<configuration>
  <connectionStrings>
    <add name="DbConnection" 
         connectionString="Server=192.168.1.102,1433;..."
         providerName="System.Data.SqlClient" />
  </connectionStrings>
</configuration>
```

---

## ?? Performans Beklentileri

```
Senaryo 1: Tek Mesaj
  Latency: 50-100ms
  
  0ms   ? WCF Request
  10ms  ? RabbitMQ Publish
  20ms  ? In Queue
  30ms  ? Consumer Processing
  40ms  ? SQL INSERT
  50ms  ? Response

Senaryo 2: 100 Mesaj/Saniye
  Queue Depth: 5-20 mesaj
  Consumer Lag: <5 saniye
  SQL Insert: ~10ms/mesaj
  
Senaryo 3: 1000 Mesaj
  Total Time: 5-10 saniye
  Memory Usage: <200MB
  CPU Usage: 20-40%
```

---

## ?? Best Practices

? **Yap?lmas? Gerekenler**:
```
1. ? Timeout ayarla (uzun i?lemler için)
2. ? Retry logic ekle (transient failures)
3. ? Correlation ID ekle (request tracking)
4. ? Logging kullan (debugging için)
5. ? Health checks ekle (monitoring için)
6. ? Circuit breaker ekle (cascade failures önleme)
```

? **Yap?lmamas? Gerekenler**:
```
1. ? Sync mesaj bekleme (async kullan)
2. ? Connection'lar? reuse etmemek
3. ? Error details client'a göstermemek
4. ? Kimlik bilgilerini code'da saklamak
5. ? Queue'yu manual silmek
```

---

## ?? Kaynaklar

- [WCF Documentation](https://docs.microsoft.com/en-us/dotnet/framework/wcf/)
- [SOAP Protocol](https://www.w3.org/TR/soap/)
- [WCF Samples](https://github.com/dotnet/samples/tree/main/framework/wcf)

