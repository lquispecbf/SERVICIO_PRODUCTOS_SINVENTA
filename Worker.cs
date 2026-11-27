using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PRODUCTOS_SINVENTA.DTOs;
using PRODUCTOS_SINVENTA.Repositories;
using PRODUCTOS_SINVENTA.Services;

namespace PRODUCTOS_SINVENTA;

public sealed class Worker : BackgroundService
{
    private static readonly TimeSpan FrecuenciaRevisionReloj = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FrecuenciaLecturaConfiguracion = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions OpcionesJson = new(JsonSerializerDefaults.Web);
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ConfiguracionLogs _logs;
    private readonly ConfiguracionSAP _sap;
    private readonly ConfiguracionProgramacion _programacion;
    private readonly ConfiguracionRutina _rutina;
    private readonly string _directorioMarcas;
    private readonly HashSet<DateOnly> _fechasIntentadas = new();
    private readonly HashSet<DateOnly> _marcasDiariasObservadas = new();
    private ConfiguracionRutinaDTO? _configuracionRutinaActual;
    private int? _versionConfiguracionActual;
    private DateTime _ultimaConsultaConfiguracion = DateTime.MinValue;
    private bool? _estadoHabilitadoAnterior;
    private DateTime _ultimaAdvertenciaVersion = DateTime.MinValue;

    public Worker(
        ILogger<Worker> logger,
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime lifetime,
        IOptions<ConfiguracionLogs> logs,
        IOptions<ConfiguracionSAP> sap,
        IOptions<ConfiguracionProgramacion> programacion,
        IOptions<ConfiguracionRutina> rutina)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _lifetime = lifetime;
        _logs = logs.Value;
        _sap = sap.Value;
        _programacion = programacion.Value;
        _rutina = rutina.Value;

        var rutaLogs = string.IsNullOrWhiteSpace(_logs.Ruta) ? "Logs" : _logs.Ruta;
        if (!Path.IsPathRooted(rutaLogs))
            rutaLogs = Path.Combine(AppContext.BaseDirectory, rutaLogs);
        _directorioMarcas = Path.Combine(rutaLogs, "ControlEjecucion");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Servicio iniciado en {HoraLocal}; zona horaria local: {Zona}. Ejecución diaria: {Hora}; CompanyDB: {CompanyDB}; días locales de respaldo: {Dias}.",
            DateTime.Now,
            TimeZoneInfo.Local.Id,
            _programacion.HoraEjecucion,
            _sap.CompanyDB,
            _sap.DiasAtras);

        if (!TimeOnly.TryParseExact(
                _programacion.HoraEjecucion,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var horaEjecucion))
        {
            throw new InvalidOperationException("Programacion:HoraEjecucion debe tener formato HH:mm (24 horas).");
        }

        if (_programacion.EjecutarAhora)
        {
            _logger.LogWarning("Modo de prueba: se ejecutará una consulta inmediatamente y el proceso finalizará.");
            await EjecutarUnaVezAsync(
                stoppingToken,
                omitirCorreo: !_programacion.EnviarCorreoEnPrueba,
                omitirNotificacionIntranet: !_programacion.EnviarNotificacionIntranetEnPrueba,
                fechaReferencia: DateTime.Today);
            _lifetime.StopApplication();
            return;
        }

        Directory.CreateDirectory(_directorioMarcas);
        DateTime? proximaEjecucionAnunciada = null;
        var primeraRevision = true;

        _logger.LogInformation(
            "El planificador revisará el reloj local cada {Segundos} segundos.",
            FrecuenciaRevisionReloj.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ActualizarConfiguracionRutinaAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (_configuracionRutinaActual is { Habilitado: false })
            {
                if (_estadoHabilitadoAnterior != false)
                {
                    _logger.LogWarning(
                        "La rutina {CodigoRutina} fue deshabilitada en intranet. La ejecución diaria queda pausada; el servicio de Windows sigue activo.",
                        _rutina.CodigoRutina);
                }
                _estadoHabilitadoAnterior = false;

                try
                {
                    await Task.Delay(FrecuenciaRevisionReloj, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                continue;
            }

            if (_estadoHabilitadoAnterior == false)
            {
                _logger.LogInformation(
                    "La rutina {CodigoRutina} fue habilitada en intranet. Se reanuda el planificador.",
                    _rutina.CodigoRutina);
            }
            _estadoHabilitadoAnterior = true;
            var ahora = DateTime.Now;
            var fechaLocal = DateOnly.FromDateTime(ahora);
            var horaProgramadaHoy = ahora.Date.Add(horaEjecucion.ToTimeSpan());
            var marcaHoy = ObtenerRutaMarca(fechaLocal);
            var esPrimeraRevision = primeraRevision;
            primeraRevision = false;
            var existeMarcaHoy = File.Exists(marcaHoy);
            var reenvioSolicitado = _fechasIntentadas.Contains(fechaLocal)
                && _marcasDiariasObservadas.Contains(fechaLocal)
                && !existeMarcaHoy;

            if (reenvioSolicitado)
            {
                _fechasIntentadas.Remove(fechaLocal);
                _marcasDiariasObservadas.Remove(fechaLocal);
                proximaEjecucionAnunciada = null;
                _logger.LogWarning(
                    "Se eliminó la marca diaria de {Fecha}; se volverá a ejecutar el ciclo ahora.",
                    fechaLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            if (_fechasIntentadas.Contains(fechaLocal) || existeMarcaHoy)
            {
                _fechasIntentadas.Add(fechaLocal);
                if (existeMarcaHoy)
                    _marcasDiariasObservadas.Add(fechaLocal);

                var proxima = ahora.Date.AddDays(1).Add(horaEjecucion.ToTimeSpan());
                if (proximaEjecucionAnunciada != proxima)
                {
                    _logger.LogInformation(
                        "La fecha {Fecha} ya tiene una ejecución registrada. Próxima ejecución: {Proxima} ({Zona}).",
                        fechaLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        proxima.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        TimeZoneInfo.Local.Id);
                    proximaEjecucionAnunciada = proxima;
                }
            }
            else if (esPrimeraRevision || ahora >= horaProgramadaHoy || reenvioSolicitado)
            {
                _fechasIntentadas.Add(fechaLocal);
                if (!IntentarCrearMarca(marcaHoy, ahora))
                {
                    proximaEjecucionAnunciada = null;
                    continue;
                }

                if (File.Exists(marcaHoy))
                    _marcasDiariasObservadas.Add(fechaLocal);

                proximaEjecucionAnunciada = null;
                _logger.LogInformation(
                    reenvioSolicitado
                        ? "Se solicitó repetir la ejecución de {Fecha} al eliminar su marca diaria. Fecha de referencia: {FechaReferencia}."
                        : esPrimeraRevision
                        ? "No existe marca para {Fecha}; se hará la ejecución inicial ahora. Fecha de referencia: {FechaReferencia}."
                        : "No existe marca para {Fecha}; se alcanzó la hora programada. Fecha de referencia: {FechaReferencia}.",
                    fechaLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    fechaLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                try
                {
                    await EjecutarUnaVezAsync(
                        stoppingToken,
                        omitirCorreo: false,
                        omitirNotificacionIntranet: false,
                        fechaReferencia: ahora.Date,
                        configuracionRutina: _configuracionRutinaActual);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
            else if (proximaEjecucionAnunciada != horaProgramadaHoy)
            {
                _logger.LogInformation(
                    "Próxima ejecución: {FechaHoraLocal} ({Zona}).",
                    horaProgramadaHoy.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    TimeZoneInfo.Local.Id);
                proximaEjecucionAnunciada = horaProgramadaHoy;
            }

            try
            {
                await Task.Delay(FrecuenciaRevisionReloj, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Servicio detenido.");
    }

    private async Task ActualizarConfiguracionRutinaAsync(CancellationToken cancellationToken)
    {
        var ahora = DateTime.Now;
        if (ahora - _ultimaConsultaConfiguracion < FrecuenciaLecturaConfiguracion)
            return;

        _ultimaConsultaConfiguracion = ahora;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IProductosSinVentaRepo>();
            var versionRemota = await repo.ObtenerVersionConfiguracionAsync(cancellationToken);
            if (!versionRemota.HasValue && ahora - _ultimaAdvertenciaVersion >= TimeSpan.FromMinutes(5))
            {
                _logger.LogWarning("No se pudo leer VERSION_CONFIG; se conserva la configuración en memoria y se reintentará.");
                _ultimaAdvertenciaVersion = ahora;
            }
            else if (versionRemota.HasValue)
            {
                _ultimaAdvertenciaVersion = DateTime.MinValue;
            }

            var configuracionInicial = _configuracionRutinaActual is null;
            var versionCambio = versionRemota.HasValue && versionRemota != _versionConfiguracionActual;

            if (!configuracionInicial && !versionCambio)
                return;

            var configuracion = await repo.ObtenerConfiguracionRutinaAsync(
                _rutina.CodigoRutina,
                cancellationToken);

            _configuracionRutinaActual = configuracion;
            if (versionRemota.HasValue)
                _versionConfiguracionActual = versionRemota;

            _logger.LogInformation(
                "Configuración de intranet cargada/actualizada. Versión: {Version}; rutina: {CodigoRutina}; habilitada: {Habilitada}; días atrás: {DiasAtras}; correo: {CanalCorreo}; intranet: {CanalIntranet}.",
                versionRemota?.ToString(CultureInfo.InvariantCulture) ?? "no disponible",
                _rutina.CodigoRutina,
                configuracion.Habilitado,
                configuracion.DiasAtras,
                configuracion.CanalCorreo,
                configuracion.CanalIntranet);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo comprobar/recargar la configuración de la rutina {CodigoRutina}.", _rutina.CodigoRutina);
        }
    }
    private string ObtenerRutaMarca(DateOnly fecha) =>
        Path.Combine(_directorioMarcas, $"EjecucionProductosSinVenta_{fecha:yyyyMMdd}.done");

    private bool IntentarCrearMarca(string rutaMarca, DateTime fechaHora)
    {
        try
        {
            using var stream = new FileStream(rutaMarca, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(fechaHora.ToString("O", CultureInfo.InvariantCulture));
            return true;
        }
        catch (IOException) when (File.Exists(rutaMarca))
        {
            _logger.LogInformation("Ya existe una marca para la fecha local {Fecha}.", fechaHora.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "No se pudo crear la marca diaria {Ruta}. Se continuará con la consulta; revise permisos de Logs.",
                rutaMarca);
            return true;
        }
    }

    private async Task EjecutarUnaVezAsync(
        CancellationToken cancellationToken,
        bool omitirCorreo,
        bool omitirNotificacionIntranet,
        DateTime fechaReferencia,
        ConfiguracionRutinaDTO? configuracionRutina = null)
    {
        var inicio = DateTime.Now;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IProductosSinVentaRepo>();
            var consulta = scope.ServiceProvider.GetRequiredService<ConsultaProductosService>();
            var email = scope.ServiceProvider.GetRequiredService<IEmailSenderProductos>();

            var configRutina = configuracionRutina ?? await repo.ObtenerConfiguracionRutinaAsync(
                _rutina.CodigoRutina,
                cancellationToken);

            if (!configRutina.Habilitado)
            {
                _logger.LogWarning(
                    "La rutina {CodigoRutina} está deshabilitada en la configuración de alertas.",
                    _rutina.CodigoRutina);
                return;
            }

            var diasAtras = configRutina.DiasAtras;
            if (diasAtras < 0)
                throw new InvalidOperationException("DIAS_ATRAS de la rutina debe ser cero o un número positivo.");

            _logger.LogInformation(
                "Consultando artículos con stock disponible y sin ventas en los últimos {Dias} día(s), usando fecha de referencia local {FechaReferencia}...",
                diasAtras,
                fechaReferencia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            var filas = await consulta.ConsultarAsync(cancellationToken, fechaReferencia, diasAtras);
            var reporte = ReporteProductos.Formatear(filas, _sap, fechaReferencia, diasAtras);

            Console.WriteLine(reporte);
            _logger.LogInformation("Reporte completo de la consulta:\n{Reporte}", reporte.TrimEnd());
            _logger.LogInformation("Consulta completada. Registros: {Cantidad}.", filas.Count);

            if (filas.Count == 0)
            {
                _logger.LogInformation("No hay resultados; no se enviará correo ni se crearán notificaciones intranet.");
                return;
            }

            if (configRutina.CanalIntranet && !omitirNotificacionIntranet)
            {
                try
                {
                    var detalleJson = JsonSerializer.Serialize(filas, OpcionesJson);
                    var fechaDeteccion = DateTime.Now;
                    var titulo = $"Productos sin venta: {filas.Count}";
                    var mensaje = CrearMensajeIntranet(filas, fechaReferencia, diasAtras);
                    var resultado = await repo.RegistrarNotificacionIntranetAsync(
                        _rutina.CodigoRutina,
                        fechaReferencia,
                        fechaDeteccion,
                        filas.Count,
                        detalleJson,
                        titulo,
                        mensaje,
                        cancellationToken);

                    if (resultado.EventoNuevo && resultado.NotificacionesCreadas == 0)
                    {
                        _logger.LogWarning(
                            "Se registró el evento intranet {IdEvento}, pero no hubo suscriptores activos con RECIBE_INTRANET habilitado.",
                            resultado.IdEvento);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Notificación intranet procesada. Evento: {IdEvento}; evento nuevo: {EventoNuevo}; notificaciones creadas: {Cantidad}.",
                            resultado.IdEvento,
                            resultado.EventoNuevo,
                            resultado.NotificacionesCreadas);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "No se pudo registrar la notificación intranet para {CodigoRutina}.", _rutina.CodigoRutina);
                }
            }
            else if (omitirNotificacionIntranet)
            {
                _logger.LogInformation("La notificación intranet está omitida para esta ejecución.");
            }
            else
            {
                _logger.LogInformation("El canal intranet está deshabilitado para {CodigoRutina}.", _rutina.CodigoRutina);
            }

            if (omitirCorreo)
            {
                _logger.LogInformation("El correo está omitido para esta ejecución de prueba.");
                return;
            }

            if (!configRutina.CanalCorreo)
            {
                _logger.LogInformation(
                    "El canal de correo está deshabilitado para {CodigoRutina}.",
                    _rutina.CodigoRutina);
                return;
            }

            var destinatarios = await repo.ObtenerSuscriptoresCorreoAsync(
                _rutina.CodigoRutina,
                cancellationToken);
            if (destinatarios.Count == 0)
            {
                _logger.LogWarning(
                    "El canal de correo está activo, pero no hay suscriptores para {CodigoRutina}.",
                    _rutina.CodigoRutina);
                return;
            }

            var enviado = await email.EnviarAsync(
                filas,
                destinatarios,
                fechaReferencia,
                cancellationToken);
            if (!enviado)
                _logger.LogWarning("No se pudo enviar el correo de esta ejecución.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falló la ejecución de productos sin venta.");
        }
        finally
        {
            _logger.LogInformation(
                "Ciclo finalizado en {Duracion:F1} segundos.",
                (DateTime.Now - inicio).TotalSeconds);
        }
    }

    private string CrearMensajeIntranet(
        IReadOnlyList<ProductoSinVenta> filas,
        DateTime fechaReferencia,
        int diasAtras)
    {
        if (filas == null || filas.Count == 0)
            return "No se encontraron productos sin venta.";

        var cantidad = filas.Count;

        var encabezado =
            $"Se detectaron {cantidad} productos con stock disponible " +
            $"que no registran ventas durante los últimos {diasAtras} días.";

        var recomendacion =
            "Te recomendamos revisar su inventario y evaluar acciones " +
            "para mejorar la rotación de estos productos.";

        var codigos = filas
            .Select(x => x.Codigo)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .Take(15)
            .ToList();

        var detalle = codigos.Count > 0
            ? $"Algunos productos detectados: {string.Join(", ", codigos)}"
            : "No se encontraron códigos de productos disponibles.";

        var restantes = cantidad - codigos.Count;

        if (restantes > 0)
            detalle += $" y {restantes} producto(s) más.";

        return $"{encabezado}\n\n{recomendacion}\n\n{detalle}";
    }
}





