using System.Globalization;
using System.Text;

namespace PRODUCTOS_SINVENTA;

public static class ReporteProductos
{
    public static string Formatear(
        IReadOnlyList<ProductoLoteSinVenta> filas,
        ConfiguracionSAP sap,
        DateTime? fechaReferencia = null,
        int? diasAtras = null)
    {
        var diasAtrasReporte = diasAtras ?? sap.DiasAtras;
        var sb = new StringBuilder();
        sb.AppendLine("PRODUCTOS / LOTES SIN VENTA");
        sb.AppendLine($"Ejecución: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Esquema SAP: {sap.CompanyDB}");
        sb.AppendLine($"Días atrás: {diasAtrasReporte}");
        sb.AppendLine($"Fecha de referencia: {(fechaReferencia ?? DateTime.Today):yyyy-MM-dd}");
        sb.AppendLine($"Registros: {filas.Count}");
        sb.AppendLine("CODIGO | PRODUCTO | LOTE | FECHA_INGRESO | DIAS_SIN_VENTA | STOCK");

        if (filas.Count == 0)
        {
            sb.AppendLine("(No se encontraron lotes que cumplan los filtros.)");
        }
        else
        {
            foreach (var fila in filas)
            {
                sb.Append(Celda(fila.Codigo)).Append(" | ")
                    .Append(Celda(fila.Producto)).Append(" | ")
                    .Append(Celda(fila.Lote)).Append(" | ")
                    .Append(fila.FechaIngreso.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(fila.DiasSinVenta.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                    .AppendLine(fila.Stock.ToString("0.####", CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    private static string Celda(string valor) =>
        valor.Replace("\r", " ").Replace("\n", " ").Replace("|", "/");
}

