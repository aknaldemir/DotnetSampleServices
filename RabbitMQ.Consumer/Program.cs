using RabbitMQ.Consumer;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMQSettings"));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
