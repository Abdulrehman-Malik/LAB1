using Autoscaler;

var builder = Host.CreateApplicationBuilder(args);

// One line per log entry, easy to follow with: docker service logs -f lab_autoscaler
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

builder.Services.AddSingleton(new DockerClient(builder.Configuration["Docker:Socket"] ?? "/var/run/docker.sock"));
builder.Services.AddHostedService<AutoscalerWorker>();

builder.Build().Run();
