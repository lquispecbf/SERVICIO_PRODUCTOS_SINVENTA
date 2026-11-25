using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PRODUCTOS_SINVENTA.DTOs;

namespace PRODUCTOS_SINVENTA.Repositories;

public sealed class ProductosSinVentaRepo : IProductosSinVentaRepo
{
    private readonly string _cadenaConexion;
    private readonly int _diasAtrasPredeterminado;
    private readonly ILogger<ProductosSinVentaRepo> _logger;

    public ProductosSinVentaRepo(
        IOptions<ConfiguracionSQLServer> sql,
        IOptions<ConfiguracionSAP> sap,
        ILogger<ProductosSinVentaRepo> logger)
    {
        _cadenaConexion = sql.Value.CadenaConexion;
        _diasAtrasPredeterminado = sap.Value.DiasAtras;
        _logger = logger;
    }

    public async Task<int?> ObtenerVersionConfiguracionAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_cadenaConexion))
            return null;

        try
        {
            await using var connection = new SqlConnection(_cadenaConexion);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("dbo.SP_OBTENER_VERSION_CONFIG_ALERTAS", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 15
            };

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken) && !reader.IsDBNull(reader.GetOrdinal("VERSION_CONFIG")))
                return Convert.ToInt32(reader["VERSION_CONFIG"]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "No se pudo consultar VERSION_CONFIG; se conservará la configuración cargada.");
        }

        return null;
    }

    public async Task<ConfiguracionRutinaDTO> ObtenerConfiguracionRutinaAsync(
        string codigoRutina,
        CancellationToken cancellationToken)
    {
        var config = new ConfiguracionRutinaDTO
        {
            DiasAtras = _diasAtrasPredeterminado
        };
        if (string.IsNullOrWhiteSpace(_cadenaConexion))
            return config;

        try
        {
            await using var connection = new SqlConnection(_cadenaConexion);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("dbo.SP_OBTENER_CONFIG_RUTINA", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 30
            };
            command.Parameters.Add("@CODIGO_RUTINA", SqlDbType.VarChar, 50).Value = codigoRutina;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                config.Habilitado = LeerBooleano(reader, "HABILITADO", true);
                config.CanalCorreo = LeerBooleano(reader, "CANAL_CORREO", false);
                config.CanalIntranet = LeerBooleano(reader, "CANAL_INTRANET", false);
                var diasAtras = LeerEntero(reader, "DIAS_ATRAS", _diasAtrasPredeterminado);
                if (diasAtras >= 0)
                    config.DiasAtras = diasAtras;
                else
                    _logger.LogWarning("DIAS_ATRAS de {CodigoRutina} es negativo; se conserva el valor local {DiasAtras}.", codigoRutina, _diasAtrasPredeterminado);
            }
            else
            {
                _logger.LogWarning(
                    "No existe configuración de rutina para {CodigoRutina}; se usará habilitada, sin canales y con {DiasAtras} días locales.",
                    codigoRutina,
                    _diasAtrasPredeterminado);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "No se pudo leer la configuración de la rutina {CodigoRutina}; se usarán los valores locales.",
                codigoRutina);
        }

        return config;
    }

    public async Task<IReadOnlyList<SuscriptorCorreoDTO>> ObtenerSuscriptoresCorreoAsync(
        string codigoRutina,
        CancellationToken cancellationToken)
    {
        var suscriptores = new List<SuscriptorCorreoDTO>();
        if (string.IsNullOrWhiteSpace(_cadenaConexion))
            return suscriptores;

        try
        {
            await using var connection = new SqlConnection(_cadenaConexion);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("dbo.SP_OBTENER_SUSCRIPTORES_RUTINA_CORREO", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 30
            };
            command.Parameters.Add("@CODIGO_RUTINA", SqlDbType.VarChar, 50).Value = codigoRutina;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var ordinalCorreo = reader.GetOrdinal("CORREO");
            var ordinalNombre = BuscarOrdinal(reader, "NOMBRE_COMPLETO");
            while (await reader.ReadAsync(cancellationToken))
            {
                var correo = reader.IsDBNull(ordinalCorreo) ? string.Empty : reader.GetString(ordinalCorreo).Trim();
                if (correo.Length == 0 || !correo.Contains('@'))
                    continue;

                suscriptores.Add(new SuscriptorCorreoDTO
                {
                    Correo = correo,
                    NombreCompleto = ordinalNombre < 0 || reader.IsDBNull(ordinalNombre)
                        ? string.Empty
                        : reader.GetValue(ordinalNombre).ToString()?.Trim() ?? string.Empty
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "No se pudieron cargar los suscriptores para {CodigoRutina}.", codigoRutina);
        }

        return suscriptores
            .GroupBy(x => x.Correo, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();
    }

    public async Task<ResultadoNotificacionIntranetDTO> RegistrarNotificacionIntranetAsync(
        string codigoRutina,
        DateTime fechaReferencia,
        DateTime fechaDeteccion,
        int cantidadProductos,
        string detalleJson,
        string titulo,
        string mensaje,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_cadenaConexion))
            throw new InvalidOperationException("Falta ConfiguracionSQLServer:CadenaConexion.");

        await using var connection = new SqlConnection(_cadenaConexion);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("dbo.SP_REGISTRAR_ALERTA_PRODUCTOS_SINVENTA", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 60
        };

        command.Parameters.Add("@CODIGO_RUTINA", SqlDbType.VarChar, 50).Value = codigoRutina;
        command.Parameters.Add("@FECHA_REFERENCIA", SqlDbType.Date).Value = fechaReferencia.Date;
        command.Parameters.Add("@FECHA_DETECCION", SqlDbType.DateTime).Value = fechaDeteccion;
        command.Parameters.Add("@CANTIDAD_PRODUCTOS", SqlDbType.Int).Value = cantidadProductos;
        command.Parameters.Add("@DETALLE_JSON", SqlDbType.NVarChar, -1).Value = detalleJson;
        command.Parameters.Add("@TITULO", SqlDbType.VarChar, 200).Value = titulo;
        command.Parameters.Add("@MENSAJE", SqlDbType.VarChar, 1000).Value = mensaje;

        var idEvento = command.Parameters.Add("@ID_EVENTO", SqlDbType.BigInt);
        idEvento.Direction = ParameterDirection.Output;
        var eventoNuevo = command.Parameters.Add("@EVENTO_NUEVO", SqlDbType.Bit);
        eventoNuevo.Direction = ParameterDirection.Output;
        var notificacionesCreadas = command.Parameters.Add("@NOTIFICACIONES_CREADAS", SqlDbType.Int);
        notificacionesCreadas.Direction = ParameterDirection.Output;

        await command.ExecuteNonQueryAsync(cancellationToken);
        return new ResultadoNotificacionIntranetDTO
        {
            IdEvento = idEvento.Value == DBNull.Value ? 0 : Convert.ToInt64(idEvento.Value),
            EventoNuevo = eventoNuevo.Value != DBNull.Value && Convert.ToBoolean(eventoNuevo.Value),
            NotificacionesCreadas = notificacionesCreadas.Value == DBNull.Value
                ? 0
                : Convert.ToInt32(notificacionesCreadas.Value)
        };
    }

    private static bool LeerBooleano(SqlDataReader reader, string nombre, bool valorPredeterminado)
    {
        var ordinal = BuscarOrdinal(reader, nombre);
        return ordinal < 0 || reader.IsDBNull(ordinal)
            ? valorPredeterminado
            : Convert.ToBoolean(reader.GetValue(ordinal));
    }

    private static int LeerEntero(SqlDataReader reader, string nombre, int valorPredeterminado)
    {
        var ordinal = BuscarOrdinal(reader, nombre);
        return ordinal < 0 || reader.IsDBNull(ordinal)
            ? valorPredeterminado
            : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static int BuscarOrdinal(SqlDataReader reader, string nombre)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), nombre, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}

