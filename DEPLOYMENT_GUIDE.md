# ?? Kurulum ve Da??t?m K?lavuzu

## ?? Ön Ko?ullar

### Yaz?l?m Gereksinimleri

| Bile?en | Versiyon | ?ndirme Linki |
|---------|----------|---------------|
| .NET Framework | 4.7.2+ | ?çinde Visual Studio |
| .NET | 8.0 LTS | https://dotnet.microsoft.com/en-us/download/dotnet/8.0 |
| RabbitMQ Server | 3.10+ | https://www.rabbitmq.com/download.html |
| SQL Server | 2016+ | https://www.microsoft.com/en-us/sql-server/sql-server-downloads |
| Visual Studio | 2019+ | https://visualstudio.microsoft.com/ |
| IIS | 7.5+ | Windows Feature'dan aktifle?tir |

### Hardware Gereksinimleri

```
Minimum:
- CPU: 2 Core
- RAM: 4 GB
- Disk: 10 GB

Önerilen:
- CPU: 4 Core
- RAM: 8 GB
- Disk: 20 GB
```

---

## ?? Geli?tirme Ortam? Kurulumu

### 1. Git Repository Klonla

```bash
git clone https://github.com/aknaldemir/DotnetSampleServices
cd DotnetSampleServices
```

### 2. Visual Studio Projesini Aç

```
File ? Open ? Solution
C:\Users\aknal\source\repos\DotnetSampleServices\DotnetSampleServices.sln
```

### 3. RabbitMQ Sunucusunu Ba?lat

#### Windows'ta (Direct Install)

```batch
@echo off
REM Erlang yüklenmi? olmal?: https://www.erlang.org/download

REM RabbitMQ Windows Binary indir ve kur
@REM C:\Program Files\RabbitMQ Server\rabbitmq_server-x.x.x\

REM Servisi ba?lat
net start RabbitMQ

REM Management plugin'i aktifle?tir
cd "C:\Program Files\RabbitMQ Server\rabbitmq_server-x.x.x\sbin"
rabbitmq-plugins enable rabbitmq_management

REM Management UI aç?l?r: http://localhost:15672
```

#### Docker ile (Önerilen)

```bash
# RabbitMQ Docker container ba?lat
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 `
  -e RABBITMQ_DEFAULT_USER=guest `
  -e RABBITMQ_DEFAULT_PASS=guest `
  rabbitmq:4-management-alpine

# Kontrol et
docker ps | grep rabbitmq
# Management UI: http://localhost:15672
```

#### Docker Compose ile (Üretim-like)

```yaml
# docker-compose.yml
version: '3.8'

services:
  rabbitmq:
    image: rabbitmq:4-management-alpine
    container_name: rabbitmq
    ports:
      - "5672:5672"    # AMQP
      - "15672:15672"  # Management UI
    environment:
      RABBITMQ_DEFAULT_USER: guest
      RABBITMQ_DEFAULT_PASS: guest
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
    networks:
      - app-network
    healthcheck:
      test: rabbitmq-diagnostics -q ping
      interval: 30s
      timeout: 10s
      retries: 5

  sqlserver:
    image: mcr.microsoft.com/mssql/server:2019-latest
    container_name: sqlserver
    ports:
      - "1433:1433"
    environment:
      SA_PASSWORD: "YourPassword123!"
      ACCEPT_EULA: "Y"
    volumes:
      - sqlserver_data:/var/opt/mssql
    networks:
      - app-network

volumes:
  rabbitmq_data:
  sqlserver_data:

networks:
  app-network:
    driver: bridge
```

```bash
# Ba?lat
docker-compose up -d

# Durumu kontrol et
docker-compose ps

# Loglar? izle
docker-compose logs -f
```

### 4. SQL Server Veritaban?n? Haz?rla

#### Ba?lant? Bilgileri

```
Server: 192.168.1.102,1433
Database: Toki
User: sa
Password: ak2100382
```

#### T-SQL Script ile Tablolar Olu?tur

```sql
-- Veritaban? seç
USE Toki;
GO

-- Logs tablosu (WCFServiceRabbitMQ taraf?ndan)
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
ELSE
    PRINT 'Logs tablosu zaten mevcut.';

-- MQMessages tablosu (RabbitMQ.Consumer taraf?ndan)
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
ELSE
    PRINT 'MQMessages tablosu zaten mevcut.';

-- Verileri kontrol et
SELECT 'Logs' AS TableName, COUNT(*) AS RowCount FROM dbo.Logs
UNION ALL
SELECT 'MQMessages' AS TableName, COUNT(*) FROM dbo.MQMessages;
GO
```

#### SQL Server Management Studio (SSMS) ile

```
1. SSMS aç
2. Connect:
   Server: 192.168.1.102,1433
   Login: sa / ak2100382
3. New Query
4. Yukar?daki T-SQL'i yap??t?r
5. F5 ile çal??t?r
```

### 5. Yap?land?rma Dosyalar?n? Güncelle

#### WCFServiceRabbitMQ - app.config

```xml
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <appSettings>
    <add key="aspnet:UseTaskFriendlySynchronizationContext" value="true" />
  </appSettings>
  
  <system.web>
    <compilation debug="true" targetFramework="4.7.2" />
    <httpRuntime targetFramework="4.7.2" />
  </system.web>
  
  <system.webServer>
    <modules runAllManagedModulesForAllRequests="true"/>
  </system.webServer>

  <system.serviceModel>
    <behaviors>
      <serviceBehaviors>
        <behavior>
          <serviceMetadata httpGetEnabled="true" />
          <serviceDebug includeExceptionDetailInFaults="false" />
        </behavior>
      </serviceBehaviors>
    </behaviors>
    
    <protocolMapping>
      <add binding="basicHttpBinding" scheme="http" />
    </protocolMapping>
    
    <serviceHostingEnvironment 
        aspNetCompatibilityEnabled="true" 
        multipleSiteBindingsEnabled="true" />
  </system.serviceModel>
  
  <connectionStrings>
    <add name="DbConnection" 
         connectionString="Server=192.168.1.102,1433;Database=Toki;User ID=sa;Password=ak2100382;MultipleActiveResultSets=True;TrustServerCertificate=True;"
         providerName="System.Data.SqlClient" />
  </connectionStrings>

  <startup>
    <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.7.2" />
  </startup>
</configuration>
```

#### RabbitMQ.Consumer - appsettings.json

```json
{
  "ConnectionStrings": {
    "DbConnection": "Server=192.168.1.102,1433;Database=Toki;User ID=sa;Password=ak2100382;MultipleActiveResultSets=True;TrustServerCertificate=True;"
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
      "Microsoft.Hosting.Lifetime": "Information",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  }
}
```

### 6. Projeleri Build Et

```bash
# Solution root'ta

# RabbitMQ.Consumer build et
cd RabbitMQ.Consumer
dotnet build -c Release

# WCFServiceRabbitMQ build et (Visual Studio ile)
cd ..\WCFServiceRabbitMQ
# Visual Studio'dan Ctrl+Shift+B
```

### 7. Geli?tirme Ortam?nda Çal??t?r

#### Terminal 1 - RabbitMQ.Consumer

```bash
cd RabbitMQ.Consumer
dotnet run --configuration Release

# Beklenen output:
# info: RabbitMQ.Consumer.Worker[0]
#       RabbitMQ ba?lant?s? kuruldu (v7.x)
# info: Microsoft.Hosting.Lifetime[0]
#       Application started.
```

#### Terminal 2 - WCFServiceRabbitMQ

```
Visual Studio ? WCFServiceRabbitMQ project sa? t?kla
? "Set as Startup Project"
? F5 (IIS Express'te ba?lat)
```

---

## ?? Üretim Ortam? Da??t?m?

### 1. WCFServiceRabbitMQ - IIS'e Da??t

#### 1.1 Publish Et

```
Visual Studio:
1. WCFServiceRabbitMQ ? sa? t?kla ? Publish
2. Create new profile ? IIS, FTP, etc. seç
3. Target location: C:\inetpub\wwwroot\MessageDispatcher
4. Publish
```

Veya komut sat?r?ndan:

```powershell
cd WCFServiceRabbitMQ
dotnet publish -c Release -o "C:\inetpub\wwwroot\MessageDispatcher"
```

#### 1.2 IIS'de Uygulama Olu?tur

```powershell
# IIS ManagerModule'ü aç
inetmgr

# Veya PowerShell ile:
Import-Module WebAdministration

# App Pool olu?tur
New-WebAppPool -Name "MessageDispatcher" -RuntimeVersion "4.0" -Force

# Web Application olu?tur
New-WebApplication -Name "MessageDispatcher" `
    -Site "Default Web Site" `
    -PhysicalPath "C:\inetpub\wwwroot\MessageDispatcher" `
    -ApplicationPool "MessageDispatcher" `
    -Force

# App Pool identity'si de?i?tir
Set-WebConfigurationProperty `
    -PSPath "IIS:\AppPools\MessageDispatcher" `
    -Name processModel.identityType `
    -Value "ApplicationPoolIdentity"
```

#### 1.3 Kontrol Et

```
http://localhost/MessageDispatcher/MessageDispatcherService.svc
http://localhost/MessageDispatcher/MessageDispatcherService.svc?wsdl
```

### 2. RabbitMQ.Consumer - Windows Service olarak Yükle

#### 2.1 Executable'? Yay?nla

```bash
cd RabbitMQ.Consumer
dotnet publish -c Release -o "C:\Services\RabbitMQ.Consumer"
```

#### 2.2 Windows Service Olu?tur

```powershell
# SC (Service Control) ile olu?tur
sc create RabbitMQConsumer ^
    binPath= "C:\Services\RabbitMQ.Consumer\RabbitMQ.Consumer.exe" ^
    start= auto ^
    DisplayName= "RabbitMQ Message Consumer Service"

# Ya da NSSM (Non-Sucking Service Manager) kullan
# NSSM'i indir: https://nssm.cc/download

nssm install RabbitMQConsumer "C:\Services\RabbitMQ.Consumer\RabbitMQ.Consumer.exe"
nssm set RabbitMQConsumer AppDirectory "C:\Services\RabbitMQ.Consumer"
nssm set RabbitMQConsumer Start SERVICE_AUTO_START
nssm set RabbitMQConsumer AppExit Default Restart
```

#### 2.3 Service'i Ba?lat

```powershell
Start-Service RabbitMQConsumer

# Durumu kontrol et
Get-Service RabbitMQConsumer

# Loglar? izle
eventvwr.msc
# Windows Logs ? Application ? Source: RabbitMQConsumer
```

### 3. A? ve Güvenlik Konfigürasyonu

#### Firewall Kurallar?

```powershell
# HTTP (IIS)
New-NetFirewallRule -DisplayName "Allow HTTP" `
    -Direction Inbound -Action Allow -Protocol TCP -LocalPort 80

# HTTPS (IIS)
New-NetFirewallRule -DisplayName "Allow HTTPS" `
    -Direction Inbound -Action Allow -Protocol TCP -LocalPort 443

# RabbitMQ AMQP (d?? eri?im varsa)
New-NetFirewallRule -DisplayName "Allow RabbitMQ AMQP" `
    -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5672

# RabbitMQ Management (d?? eri?im varsa)
New-NetFirewallRule -DisplayName "Allow RabbitMQ Management" `
    -Direction Inbound -Action Allow -Protocol TCP -LocalPort 15672

# SQL Server (d?? eri?im varsa)
New-NetFirewallRule -DisplayName "Allow SQL Server" `
    -Direction Inbound -Action Allow -Protocol TCP -LocalPort 1433
```

#### SSL/TLS Sertifikas? (HTTPS)

```powershell
# Self-signed sertifika olu?tur (test için)
$cert = New-SelfSignedCertificate -DnsName "yourdomain.com" `
    -CertStoreLocation cert:\LocalMachine\My `
    -NotAfter (Get-Date).AddYears(1)

# IIS Binding'e ekle
New-IISSiteBinding -Name "Default Web Site" `
    -Protocol https -Port 443 -Thumbprint $cert.Thumbprint

# Üretim için: DigiCert, Let's Encrypt vb. sertifika kullan
```

---

## ?? Backup ve Restore

### SQL Server Backup

```sql
-- Logs tablosunu backup et
BACKUP TABLE Logs
TO DISK = 'C:\Backups\Logs.bak'

-- MQMessages tablosunu backup et
BACKUP TABLE MQMessages
TO DISK = 'C:\Backups\MQMessages.bak'

-- Yada tüm veritaban?n?
BACKUP DATABASE Toki
TO DISK = 'C:\Backups\Toki.bak'
WITH INIT, COMPRESSION
```

### RabbitMQ Definitions Export

```bash
# Management API ile export et
curl -u guest:guest http://localhost:15672/api/definitions > rabbitmq_definitions.json

# Restore et
curl -i -u guest:guest -H "Content-Type: application/json" ^
    -XPOST http://localhost:15672/api/definitions ^
    -d @rabbitmq_definitions.json
```

---

## ?? Monitoring ve Logging

### Application Insights (Azure)

```csharp
// RabbitMQ.Consumer/Program.cs
builder.Services.AddApplicationInsightsTelemetry("instrumentation-key");
```

### Serilog ile Structured Logging

```csharp
// Program.cs
builder.Host.UseSerilog((context, config) =>
    config
        .MinimumLevel.Information()
        .WriteTo.Console()
        .WriteTo.File("logs/consumer-.txt", rollingInterval: RollingInterval.Day)
        .Enrich.FromLogContext()
);
```

### Performance Counters

```powershell
# CPU, Memory, Disk monitoring
wmic os get TotalVisibleMemorySize, FreePhysicalMemory
wmic logicaldisk get name, size, freespace
```

---

## ?? Health Checks

### RabbitMQ Health Check

```bash
rabbitmq-diagnostics status
# Veya
curl -i -u guest:guest http://localhost:15672/api/health/checks/virtual-hosts
```

### SQL Server Health Check

```sql
SELECT 
    SERVERPROPERTY('Servername') AS ServerName,
    SERVERPROPERTY('Edition') AS Edition,
    SERVERPROPERTY('ProductVersion') AS Version,
    SYSDATETIME() AS CurrentTime;
```

### Service Health Check

```powershell
# WCFServiceRabbitMQ
$response = Invoke-WebRequest -Uri "http://localhost/MessageDispatcher/MessageDispatcherService.svc" -UseBasicParsing
if ($response.StatusCode -eq 200) { Write-Host "? WCF Service Running" } else { Write-Host "? WCF Service Down" }

# RabbitMQ.Consumer
$service = Get-Service RabbitMQConsumer
if ($service.Status -eq "Running") { Write-Host "? Consumer Service Running" } else { Write-Host "? Consumer Service Down" }

# RabbitMQ
$response = Invoke-WebRequest -Uri "http://guest:guest@localhost:15672/api/overview" -UseBasicParsing
if ($response.StatusCode -eq 200) { Write-Host "? RabbitMQ Running" } else { Write-Host "? RabbitMQ Down" }
```

---

## ?? Troubleshooting Checklist

- [ ] RabbitMQ Server çal???yor mu? (`net start RabbitMQ` veya `docker ps`)
- [ ] SQL Server ba?lant?s? aç?k m?? (SSMS test et)
- [ ] WCF Service endpoint'e ula??labiliyor mu? (browser'da .svc dosyas?n? aç)
- [ ] Consumer service çal???yor mu? (Services ? RabbitMQConsumer)
- [ ] Firewall kurallar? tamam m?? (inbound rules kontrol et)
- [ ] Veritaban? tablolar mevcut mi? (SSMS'de kontrol et)
- [ ] Configuration dosyalar? güncel mi? (connection strings vs)
- [ ] Log dosyalar? kontrol edildi mi? (Errors var m??)

---

## ?? Deployment Checklist

### Üretim Öncesi

- [ ] Security scan yap?ld? m??
- [ ] Load test yap?ld? m??
- [ ] Backup/Restore test edildi mi?
- [ ] Disaster recovery plan haz?r m??
- [ ] Monitoring aktif m??
- [ ] Alerting konfigüre edildi mi?
- [ ] Documentation güncel m??
- [ ] Team training yap?ld? m??

### Deployment S?ras?nda

- [ ] Maintenance window planland? m??
- [ ] Rollback plan haz?rland? m??
- [ ] Stakeholder'lar bilgilendirildi mi?
- [ ] Change log olu?turuldu mu?

### Deployment Sonras?nda

- [ ] Health checks geçti mi?
- [ ] Monitoring normal mi?
- [ ] No alerts/errors mi?
- [ ] Stakeholder'lar bilgilendirildi mi?
- [ ] Post-deployment review yap?ld? m??

