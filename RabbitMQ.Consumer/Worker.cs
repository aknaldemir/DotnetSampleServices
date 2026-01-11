using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

namespace RabbitMQ.Consumer
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly RabbitMqOptions rabbitMqOptions;
        private readonly IConfiguration configuration;

        private IConnection? _connection;
        private IChannel? _channel;

        public Worker(ILogger<Worker> logger, IOptions<RabbitMqOptions> rabbitMqOptions, IConfiguration configuration)
        {
            _logger = logger;
            this.rabbitMqOptions = rabbitMqOptions.Value;
            this.configuration = configuration;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            await InitRabbitMqAsync();
            await base.StartAsync(cancellationToken);
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var consumer = new AsyncEventingBasicConsumer(_channel!);

            consumer.ReceivedAsync += async (_, ea) =>
            {
                try
                {
                    var message = Encoding.UTF8.GetString(ea.Body.ToArray());

                    _logger.LogInformation("Mesaj alındı: {msg}", message);

                    await ProcessMessageAsync(message);

                    await _channel!.BasicAckAsync(ea.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Mesaj işlenemedi");

                    await _channel!.BasicNackAsync(
                        ea.DeliveryTag,
                        multiple: false,
                        requeue: true);
                }
            };

            await _channel!.BasicConsumeAsync(
                queue: rabbitMqOptions.Queue,
                autoAck: false,
                consumer: consumer);

            // Worker ayakta kalsın
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

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

            await _channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: rabbitMqOptions.PrefetchCount,
                global: false);

            await _channel.QueueDeclareAsync(
                queue: rabbitMqOptions.Queue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            _logger.LogInformation("RabbitMQ bağlantısı kuruldu (v7.x)");
        }

    }
}
