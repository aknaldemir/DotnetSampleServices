namespace WCFServiceRabbitMQ
{
    public interface IRabbitMqProducer
    {
        void Publish(string message);
    }
}