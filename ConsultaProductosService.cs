using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace PRODUCTOS_SINVENTA;

public sealed record ProductoLoteSinVenta(
    string Codigo, string Producto, string Lote,
    DateTime FechaIngreso, decimal Stock, int DiasSinVenta);

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

    public async Task<IReadOnlyList<ProductoLoteSinVenta>> ConsultarAsync(
        CancellationToken cancellationToken, DateTime? fechaReferencia = null, int? diasAtras = null)
    {
        if (string.IsNullOrWhiteSpace(_sql.CadenaConexion))
            throw new InvalidOperationException(
                "Falta ConfiguracionSQLServer:CadenaConexion en appsettings.json.");

        if (string.IsNullOrWhiteSpace(_sap.CompanyDB) ||
            !_sap.CompanyDB.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new InvalidOperationException(
                "ConfiguracionSAP:CompanyDB debe ser un esquema válido, por ejemplo SBO_CBF.");

        var diasAtrasConsulta = diasAtras ?? _sap.DiasAtras;
        if (diasAtrasConsulta < 0)
            throw new InvalidOperationException(
                "ConfiguracionSAP:DiasAtras debe ser cero o un número positivo.");

        var filas = new List<ProductoLoteSinVenta>();
        await using var conexion = new SqlConnection(_sql.CadenaConexion);
        await conexion.OpenAsync(cancellationToken);

        await using var comando = new SqlCommand(
            "dbo.SP_BUSCAR_PRODUCTOS_LOTES_SIN_VENTAS", conexion)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 120
        };

        comando.Parameters.Add("@BD_HANA", SqlDbType.VarChar, 100).Value = _sap.CompanyDB.Trim();
        comando.Parameters.Add("@DIAS_ATRAS", SqlDbType.Int).Value = diasAtrasConsulta;
        comando.Parameters.Add("@FECHA_REFERENCIA", SqlDbType.Date).Value = (fechaReferencia ?? DateTime.Today).Date;

        await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
        var iCodigo = lector.GetOrdinal("CODIGO");
        var iProducto = lector.GetOrdinal("PRODUCTO");
        var iLote = lector.GetOrdinal("LOTE");
        var iFecha = lector.GetOrdinal("FECHA_INGRESO");
        var iStock = lector.GetOrdinal("STOCK");

        while (await lector.ReadAsync(cancellationToken))
        {
            var fecha = Convert.ToDateTime(lector.GetValue(iFecha), CultureInfo.InvariantCulture);
            filas.Add(new ProductoLoteSinVenta(
                lector.IsDBNull(iCodigo) ? "" : Convert.ToString(lector.GetValue(iCodigo), CultureInfo.InvariantCulture) ?? "",
                lector.IsDBNull(iProducto) ? "" : Convert.ToString(lector.GetValue(iProducto), CultureInfo.InvariantCulture) ?? "",
                lector.IsDBNull(iLote) ? "" : Convert.ToString(lector.GetValue(iLote), CultureInfo.InvariantCulture) ?? "",
                fecha,
                Convert.ToDecimal(lector.GetValue(iStock), CultureInfo.InvariantCulture),
                diasAtrasConsulta));
        }

        return filas;
    }
}

