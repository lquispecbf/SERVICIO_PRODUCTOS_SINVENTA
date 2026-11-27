using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace PRODUCTOS_SINVENTA;

public sealed record ProductoSinVenta(
    string Codigo,
    string Producto,
    string UnidadMedida,
    decimal? StockUnidades,
    decimal? StockCajas,
    decimal? CostoPorCaja,
    decimal? Valorizacion,
    int DiasSinVenta);

public sealed class ConsultaProductosService
{
    private readonly ConfiguracionSQLServer _sql;
    private readonly ConfiguracionSAP _sap;

    public ConsultaProductosService(
        IOptions<ConfiguracionSQLServer> sql,
        IOptions<ConfiguracionSAP> sap)
    {
        _sql = sql.Value;
        _sap = sap.Value;
    }

    public async Task<IReadOnlyList<ProductoSinVenta>> ConsultarAsync(
            CancellationToken cancellationToken,
            DateTime? fechaReferencia = null,
            int? diasAtras = null)
    {
        if (string.IsNullOrWhiteSpace(_sql.CadenaConexion))
        {
            throw new InvalidOperationException(
                "Falta ConfiguracionSQLServer:CadenaConexion en appsettings.json.");
        }

        if (string.IsNullOrWhiteSpace(_sap.CompanyDB) ||
            !_sap.CompanyDB.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            throw new InvalidOperationException(
                "ConfiguracionSAP:CompanyDB debe ser un esquema válido, por ejemplo SBO_CBF.");
        }

        var diasAtrasConsulta = diasAtras ?? _sap.DiasAtras;

        if (diasAtrasConsulta < 0)
        {
            throw new InvalidOperationException(
                "ConfiguracionSAP:DiasAtras debe ser cero o un número positivo.");
        }

        var filas = new List<ProductoSinVenta>();

        await using var conexion = new SqlConnection(_sql.CadenaConexion);

        await conexion.OpenAsync(cancellationToken);

        await using var comando = new SqlCommand(
            "dbo.SP_BUSCAR_PRODUCTOS_LOTES_SIN_VENTAS",
            conexion)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 120
        };

        comando.Parameters.Add("@BD_HANA", SqlDbType.VarChar, 100)
            .Value = _sap.CompanyDB.Trim();

        comando.Parameters.Add("@DIAS_ATRAS", SqlDbType.Int)
            .Value = diasAtrasConsulta;

        comando.Parameters.Add("@FECHA_REFERENCIA", SqlDbType.Date)
            .Value = (fechaReferencia ?? DateTime.Today).Date;

        await using var lector = await comando.ExecuteReaderAsync(
            cancellationToken);

        // Columnas devueltas por el SP
        var iCodigo = lector.GetOrdinal("CODIGO");
        var iProducto = lector.GetOrdinal("PRODUCTO");
        var iUnidadMedida = lector.GetOrdinal("UNIDAD_MEDIDA");

        var iStockUnidades = lector.GetOrdinal("STOCK_UNIDADES");
        var iStockCajas = lector.GetOrdinal("STOCK_CAJAS");

        var iCostoPorCaja = lector.GetOrdinal("COSTO_X_CAJA");
        var iValorizacion = lector.GetOrdinal("VALORIZACION");

        while (await lector.ReadAsync(cancellationToken))
        {
            filas.Add(new ProductoSinVenta(
                lector.IsDBNull(iCodigo)
                    ? ""
                    : Convert.ToString(
                        lector.GetValue(iCodigo),
                        CultureInfo.InvariantCulture) ?? "",

                lector.IsDBNull(iProducto)
                    ? ""
                    : Convert.ToString(
                        lector.GetValue(iProducto),
                        CultureInfo.InvariantCulture) ?? "",

                lector.IsDBNull(iUnidadMedida)
                    ? ""
                    : Convert.ToString(
                        lector.GetValue(iUnidadMedida),
                        CultureInfo.InvariantCulture) ?? "",

                LeerDecimalOpcional(lector, iStockUnidades),
                LeerDecimalOpcional(lector, iStockCajas),
                LeerDecimalOpcional(lector, iCostoPorCaja),
                LeerDecimalOpcional(lector, iValorizacion),

                diasAtrasConsulta
            ));
        }

        return filas;
    }

    private static decimal? LeerDecimalOpcional(SqlDataReader lector, int ordinal) =>
        lector.IsDBNull(ordinal)
            ? null
            : Convert.ToDecimal(lector.GetValue(ordinal), CultureInfo.InvariantCulture);
}

