using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PRODUCTOS_SINVENTA.Configuration;
using PRODUCTOS_SINVENTA.DTOs;

namespace PRODUCTOS_SINVENTA.Services;

public sealed class EmailSenderProductos : IEmailSenderProductos
{
    private readonly SmtpSettings _smtp;
    private readonly ConfiguracionSAP _sap;
    private readonly ILogger<EmailSenderProductos> _logger;

    public EmailSenderProductos(
        IOptions<SmtpSettings> smtp,
        IOptions<ConfiguracionSAP> sap,
        ILogger<EmailSenderProductos> logger)
    {
        _smtp = smtp.Value;
        _sap = sap.Value;
        _logger = logger;
    }

    public async Task<bool> EnviarAsync(
        IReadOnlyList<ProductoLoteSinVenta> productos,
        IReadOnlyList<SuscriptorCorreoDTO> destinatarios,
        DateTime fechaReferencia,
        CancellationToken cancellationToken)
    {
        if (productos.Count == 0 || destinatarios.Count == 0)
            return false;

        if (string.IsNullOrWhiteSpace(_smtp.Host) ||
            string.IsNullOrWhiteSpace(_smtp.From) ||
            string.IsNullOrWhiteSpace(_smtp.Password) ||
            _smtp.Port <= 0)
        {
            _logger.LogWarning("Configuración SMTP incompleta; no se enviará el correo.");
            return false;
        }

        try
        {
            using var mensaje = new MailMessage
            {
                From = new MailAddress(_smtp.From.Trim(), _smtp.FromName),
                Subject = $"Alerta: {productos.Count} productos/lotes sin venta",
                Body = ConstruirHtml(productos, fechaReferencia),
                IsBodyHtml = true,
                SubjectEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8
            };

            foreach (var suscriptor in destinatarios)
            {
                if (string.IsNullOrWhiteSpace(suscriptor.Correo))
                    continue;
                try
                {
                    mensaje.To.Add(new MailAddress(suscriptor.Correo.Trim()));
                }
                catch (FormatException)
                {
                    _logger.LogWarning("Se omitió un destinatario con formato de correo inválido.");
                }
            }

            if (mensaje.To.Count == 0)
            {
                _logger.LogWarning("La rutina no tiene destinatarios de correo válidos.");
                return false;
            }

            using var cliente = new SmtpClient(_smtp.Host.Trim(), _smtp.Port)
            {
                EnableSsl = _smtp.EnableSsl,
                UseDefaultCredentials = false,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 20000
            };

            if (!string.IsNullOrWhiteSpace(_smtp.User))
                cliente.Credentials = new NetworkCredential(_smtp.User.Trim(), _smtp.Password);

            await cliente.SendMailAsync(mensaje, cancellationToken);
            _logger.LogInformation("Correo de productos sin venta enviado a {Cantidad} destinatarios.", mensaje.To.Count);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "No se pudo enviar el correo de productos sin venta.");
            return false;
        }
    }



    private string ConstruirHtml(IReadOnlyList<ProductoLoteSinVenta> productos, DateTime fechaReferencia)
    {
        var sb = new StringBuilder();

        sb.Append("<!DOCTYPE html><html lang='es'><head><meta charset='utf-8'>");

        sb.Append("<style>");
        sb.Append(@"
        body {
            font-family: 'Segoe UI', Arial, sans-serif;
            color: #34495e;
            background-color: #f4f6f8;
            margin: 0;
            padding: 25px;
        }

        .container {
            max-width: 1100px;
            margin: 0 auto;
            background: #ffffff;
            border-radius: 8px;
            overflow: hidden;
            border: 1px solid #e5eaea;
        }

        .header {
            background: #1ab394;
            color: white;
            padding: 22px 25px;
        }

        .header h2 {
            margin: 0;
            font-size: 22px;
            font-weight: 600;
            color: white;
        }

        .header p {
            margin: 7px 0 0;
            color: #eafff9;
            font-size: 13px;
        }

        .content {
            padding: 25px;
        }

        .description {
            font-size: 14px;
            color: #667781;
            margin-bottom: 20px;
        }

        table {
            width: 100%;
            border-collapse: collapse;
            font-size: 13px;
        }

        thead th {
            background: #1ab394;
            color: white;
            padding: 13px 10px;
            text-align: left;
            font-weight: 600;
            border: 1px solid #18a689;
        }

        tbody td {
            padding: 11px 10px;
            border-bottom: 1px solid #e9eeee;
            color: #34495e;
        }

        tbody tr:nth-child(even) {
            background-color: #f5f9f8;
        }

        .codigo {
            font-weight: 600;
            color: #263746;
        }

        .dias {
            font-weight: 600;
            color: #d17a22;
        }

        .stock {
            font-weight: 600;
            color: #1ab394;
        }

        .footer {
            background: #f4f7f6;
            border-top: 1px solid #e5eaea;
            padding: 18px 25px;
            text-align: center;
            color: #84929a;
            font-size: 12px;
        }

        .footer p {
            margin: 5px 0;
        }
    ");
        sb.Append("</style></head><body>");

        sb.Append("<div class='container'>");

        // ENCABEZADO
        sb.Append("<div class='header'>");
        sb.Append("<h2>Productos y lotes sin venta</h2>");
        sb.Append("<p>Reporte de control y seguimiento de inventario</p>");
        sb.Append("</div>");

        // CONTENIDO
        sb.Append("<div class='content'>");

        sb.Append("<p class='description'>");
        sb.Append("A continuación, se presenta el listado de productos y lotes ");
        sb.Append("que no registran ventas durante el período de evaluación.");
        sb.Append("</p>");

        // TABLA
        sb.Append("<table>");
        sb.Append("<thead><tr>");
        sb.Append("<th>Código</th>");
        sb.Append("<th>Producto</th>");
        sb.Append("<th>Lote</th>");
        sb.Append("<th>Fecha de admisión</th>");
        sb.Append("<th>Días sin venta</th>");
        sb.Append("<th>Stock</th>");
        sb.Append("</tr></thead><tbody>");

        foreach (var item in productos)
        {
            sb.Append("<tr>");

            sb.Append("<td class='codigo'>")
                .Append(WebUtility.HtmlEncode(item.Codigo))
                .Append("</td>");

            sb.Append("<td>")
                .Append(WebUtility.HtmlEncode(item.Producto))
                .Append("</td>");

            sb.Append("<td>")
                .Append(WebUtility.HtmlEncode(item.Lote))
                .Append("</td>");

            sb.Append("<td>")
                .Append(item.FechaIngreso.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Append("</td>");

            sb.Append("<td class='dias'>")
                .Append(item.DiasSinVenta.ToString(CultureInfo.InvariantCulture))
                .Append("</td>");

            sb.Append("<td class='stock'>")
                .Append(item.Stock.ToString("0.####", CultureInfo.InvariantCulture))
                .Append("</td>");

            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        sb.Append("</div>");

        // FOOTER
        sb.Append("<div class='footer'>");
        sb.Append($"<p>Este es un reporte automático generado por el servicio PRODUCTOS_SIN_VENTA a las {DateTime.Now:yyyy-MM-dd HH:mm:ss}.</p>");
        sb.Append("<p>Por favor, no responda a este correo electrónico.</p>");
        sb.Append("</div>");

        sb.Append("</div></body></html>");

        return sb.ToString();
    }


}