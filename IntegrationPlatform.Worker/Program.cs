using IntegrationPlatform.Common.Interfaces;
using IntegrationPlatform.Worker;
using IntegrationPlatform.Worker.Engine;
using IntegrationPlatform.Worker.Interfaces.Services;
using IntegrationPlatform.Worker.Plugins;
using IntegrationPlatform.Worker.Services;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Serilog yapýlandýrmasý
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/worker-.txt", rollingInterval: RollingInterval.Day)
    .WriteTo.Seq("http://localhost:5341") // Ýsteðe baðlý Seq logging
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

// Konfigürasyon
builder.Services.Configure<WorkerSettings>(
    builder.Configuration.GetSection("Worker"));

builder.Services.AddHttpClient("apiClient", client =>
{
    var settings = builder.Configuration.GetSection("Worker").Get<WorkerSettings>();
    client.BaseAddress = new Uri(settings.ApiBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "IntegrationPlatform-Worker/1.0");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    // GELÝÞTÝRME ORTAMINDA SSL doðrulamasýný kapat
    ServerCertificateCustomValidationCallback =
        (message, cert, chain, errors) => true  // HER ÞEYE GÜVEN
});

builder.Services.AddSingleton<WorkerSettings>();
// Plugin Manager ve Workflow Engine (Geçici implementasyon)
builder.Services.AddSingleton<IPluginManager, PluginManager>();
builder.Services.AddSingleton<IAdapterFactory, AdapterFactory>();
builder.Services.AddSingleton<IWorkflowEngine, WorkflowEngine>();
// ApiClientService'i DI'a ekle
builder.Services.AddSingleton<IApiClientService>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient("apiClient");
    var logger = sp.GetRequiredService<ILogger<ApiClientService>>();

    return new ApiClientService(httpClient, logger);
});

// Worker Service
builder.Services.AddHostedService<WorkerService>();
builder.Services.AddHostedService<TestPollingService>();

// Health Checks
builder.Services.AddHealthChecks()
    .AddCheck<WorkerHealthCheck>("worker_health");

var host = builder.Build();

// Health Checks middleware'i
await host.RunAsync();


