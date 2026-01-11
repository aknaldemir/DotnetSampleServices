using RabbitMQ.Client;
using System;
using System.ServiceModel.Configuration;
using System.Text;

namespace WCFServiceRabbitMQ
{
    public class RabbitMqProducer : IDisposable, IRabbitMqProducer
    {
        private readonly IConnection connection;
        private readonly IModel channel;

        private const string ExchangeName = "app.direct.exhange";
        private const string QeueueName = "app.queue";
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

            channel.ExchangeDeclare(exchange: ExchangeName, type: ExchangeType.Direct, durable: true, autoDelete: false);
            channel.QueueDeclare(queue: QeueueName, durable: true, exclusive: false, autoDelete: false);
            channel.QueueBind(queue: QeueueName, exchange: ExchangeName, routingKey: RoutingKey);
        }

        public void Publish(string message)
        {
            var body = Encoding.UTF8.GetBytes(message);

            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;

            channel.BasicPublish(exchange: ExchangeName,
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
}