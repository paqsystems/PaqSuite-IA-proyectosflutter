using Microsoft.Extensions.Options;
using PaqAgent;
using PaqAgent.Auth;
using PaqAgent.Diagnostics;
using PaqAgent.Options;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

builder.Services.AddOptions<AgentOptions>()
    .Bind(builder.Configuration)
    .Validate(
        options => !string.Equals(options.AgentToken, "dev-agent-token", StringComparison.Ordinal),
        "AgentToken 'dev-agent-token' is forbidden.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ISqlConnectionPinger, SqlConnectionPinger>();
builder.Services.AddSingleton<DiagnosticsRunner>();
builder.Services.AddSingleton<IAuthLoginSpExecutor, SqlAuthLoginSpExecutor>();
builder.Services.AddSingleton<AuthLoginRunner>();
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "PaqAgent";
});

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine("logs", "paqagent-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14)
    .CreateLogger();

builder.Services.AddSerilog();
builder.Services.AddHostedService<AgentGatewayConnector>();

try
{
    var host = builder.Build();
    Log.Information("PaqAgent host starting");
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "PaqAgent host terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
