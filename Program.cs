using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PRODUCTOS_SINVENTA;
using PRODUCTOS_SINVENTA.Configuration;
using PRODUCTOS_SINVENTA.Repositories;
using PRODUCTOS_SINVENTA.Services;
using Serilog;

var configuracionInicial = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var rutaLogsConfigurada = configuracionInicial["ConfiguracionLogs:Ruta"];
var rutaLogs = string.IsNullOrWhiteSpace(rutaLogsConfigurada) ? "Logs" : rutaLogsConfigurada;
if (!Path.IsPathRooted(rutaLogs))
    rutaLogs = Path.Combine(AppContext.BaseDirectory, rutaLogs);
Directory.CreateDirectory(rutaLogs);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        Path.Combine(rutaLogs, "ProductosSinVenta-.log"),
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Iniciando servicio Productos sin venta.");
    await Host.CreateDefaultBuilder(args)
        .UseContentRoot(AppContext.BaseDirectory)
        .UseWindowsService(options => options.ServiceName = "PRODUCTOS_SINVENTA")
        .UseSerilog()
        .ConfigureServices((context, services) =>
        {
            services.Configure<ConfiguracionLogs>(context.Configuration.GetSection("ConfiguracionLogs"));
            services.Configure<ConfiguracionSAP>(context.Configuration.GetSection("ConfiguracionSAP"));
            services.Configure<ConfiguracionSQLServer>(context.Configuration.GetSection("ConfiguracionSQLServer"));
            services.Configure<ConfiguracionProgramacion>(context.Configuration.GetSection("Programacion"));
            services.Configure<ConfiguracionRutina>(context.Configuration.GetSection("ConfiguracionRutina"));
            services.Configure<SmtpSettings>(context.Configuration.GetSection("SmtpSettings"));

            services.AddSingleton<ConsultaProductosService>();
            services.AddScoped<IProductosSinVentaRepo, ProductosSinVentaRepo>();
            services.AddScoped<IEmailSenderProductos, EmailSenderProductos>();
            services.AddHostedService<Worker>();
        })
        .Build()
        .RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "El servicio Productos sin venta terminó con error.");
}
finally
{
    await Log.CloseAndFlushAsync();
}