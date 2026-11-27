using System.Globalization;
using System.Text;

namespace PRODUCTOS_SINVENTA;

public static class ReporteProductos
{
    public static string Formatear(
        IReadOnlyList<ProductoSinVenta> filas,
        ConfiguracionSAP sap,
        DateTime? fechaReferencia = null,
        int? diasAtras = null)
    {
        var diasAtrasReporte = diasAtras ?? sap.DiasAtras;
        var sb = new StringBuilder();
        sb.AppendLine("PRODUCTOS SIN VENTA");
        sb.AppendLine($"Ejecución: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Esquema SAP: {sap.CompanyDB}");
        sb.AppendLine($"Período sin ventas (días): {diasAtrasReporte}");
        sb.AppendLine($"Fecha de referencia: {(fechaReferencia ?? DateTime.Today):yyyy-MM-dd}");
        sb.AppendLine($"Registros: {filas.Count}");
        sb.AppendLine("CODIGO | PRODUCTO | UNIDAD_DE_MEDIDA | DIAS_SIN_VENTA | STOCK_CAJAS | COSTO_X_CAJA | VALORIZACION");

        if (filas.Count == 0)
        {
            sb.AppendLine("(No se encontraron productos que cumplan los filtros.)");
        }
        else
        {
            foreach (var fila in filas)
            {
                sb.Append(Celda(fila.Codigo)).Append(" | ")
                    .Append(Celda(fila.Producto)).Append(" | ")
                    .Append(Celda(fila.UnidadMedida)).Append(" | ")
                    .Append(fila.DiasSinVenta.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(FormatearOpcional(fila.StockCajas, "0.####")).Append(" | ")
                    .Append(FormatearOpcional(fila.CostoPorCaja, "0.00")).Append(" | ")
                    .AppendLine(FormatearOpcional(fila.Valorizacion, "0.00"));
            }
        }

        return sb.ToString();
    }

    private static string Celda(string valor) =>
        valor.Replace("\r", " ").Replace("\n", " ").Replace("|", "/");

    private static string FormatearOpcional(decimal? valor, string formato) =>
        valor?.ToString(formato, CultureInfo.InvariantCulture) ?? "N/D";
}

