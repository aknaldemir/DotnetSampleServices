using System;
using System.Configuration;
using System.Data.SqlClient;

namespace WCFServiceRabbitMQ
{
    // NOTE: You can use the "Rename" command on the "Refactor" menu to change the class name "Service1" in code, svc and config file together.
    // NOTE: In order to launch WCF Test Client for testing this service, please select Service1.svc or Service1.svc.cs at the Solution Explorer and start debugging.
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
                throw; // WCF client hata görsün diye
            }
        }

        private void LogInfo(string message)
        {
            var connectionString =
                ConfigurationManager.ConnectionStrings["DbConnection"].ConnectionString;

            using (var conn = new SqlConnection(connectionString))
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                INSERT INTO Logs (Text)
                VALUES (@Text)
            ";

                cmd.Parameters.AddWithValue("@Text", message);

                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private void LogError(Exception ex)
        {
            var connectionString =
                ConfigurationManager.ConnectionStrings["DbConnection"].ConnectionString;

            using (var conn = new SqlConnection(connectionString))
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                INSERT INTO Logs (Text)
                VALUES (@Text)
            ";

                cmd.Parameters.AddWithValue("@Text", ex.ToString());

                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }
    }
}
