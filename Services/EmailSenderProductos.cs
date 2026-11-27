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
        IReadOnlyList<ProductoSinVenta> productos,
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
                Subject = $"Alerta: {productos.Count} productos sin venta",
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




    private string ConstruirHtml(
        IReadOnlyList<ProductoSinVenta> productos,
        DateTime fechaReferencia)
    {
        var sb = new StringBuilder();

        sb.Append("<!DOCTYPE html><html lang='es'><head><meta charset='utf-8'>");

        sb.Append("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");

        sb.Append("<style>");
        sb.Append(@"
        body {
            font-family: 'Segoe UI', Arial, sans-serif;
            color: #34495e;
            background-color: #f4f6f8;
            margin: 0;
            padding: 15px;
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
            padding: 20px;
        }

        .description {
            font-size: 14px;
            color: #667781;
            margin-bottom: 20px;
            line-height: 1.6;
        }

        /* CONTENEDOR SCROLL */
        .tabla-scroll {
            width: 100%;
            max-width: 100%;
            overflow-x: auto;
            overflow-y: hidden;
            -webkit-overflow-scrolling: touch;
            border: 1px solid #e5eaea;
            border-radius: 6px;
            background-color: #ffffff;
        }

        /* TABLA CON ANCHO FIJO */
        .tabla-productos {
            width: 100%;
            min-width: 0;
            table-layout: auto;
            border-collapse: collapse;
            font-size: 13px;
            background-color: #ffffff;
        }

        .tabla-productos thead th {
            background: #1ab394;
            color: white;
            padding: 13px 10px;
            text-align: left;
            font-weight: 600;
            border: 1px solid #18a689;
            white-space: nowrap;
        }

        .tabla-productos tbody td {
            padding: 11px 10px;
            border-bottom: 1px solid #e9eeee;
            color: #34495e;
            vertical-align: middle;
        }

        .tabla-productos tbody tr:nth-child(even) {
            background-color: #f5f9f8;
        }

        .codigo {
            font-weight: 600;
            color: #263746;
            white-space: nowrap;
        }

        .producto {
            white-space: normal;
            overflow-wrap: break-word;
            word-break: normal;
            line-height: 1.5;
        }

        .dias {
            font-weight: 600;
            color: #d17a22;
            text-align: center;
            white-space: nowrap;
        }

        .unidad {
            white-space: nowrap;
        }

        .stock {
            font-weight: 600;
            color: #1ab394;
            text-align: right;
            white-space: nowrap;
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

        /* AJUSTES PARA CELULARES */
        @media only screen and (max-width: 600px) {
            body {
                padding: 5px;
            }

            .content {
                padding: 12px;
            }

            .header {
                padding: 18px 15px;
            }

            .header h2 {
                font-size: 20px;
            }

            .description {
                font-size: 13px;
            }

            .tabla-productos {
                width: 1150px;
                min-width: 1150px;
            }
        }
    ");
        sb.Append("</style></head><body>");

        sb.Append("<div class='container'>");

        // ENCABEZADO
        sb.Append("<div class='header'>");
        sb.Append("<h2>Productos sin venta</h2>");
        sb.Append("<p>Reporte de control y seguimiento de inventario</p>");
        sb.Append("</div>");

        // CONTENIDO
        sb.Append("<div class='content'>");

        sb.Append("<p class='description'>");
        sb.Append("A continuación, se presenta el listado de productos ");
        sb.Append("que no registran ventas durante el período de evaluación.");
        sb.Append("</p>");

        // TABLA CON SCROLL HORIZONTAL
        sb.Append("<div class='tabla-scroll' ");
        sb.Append("style='width:100%; max-width:100%; overflow-x:auto; overflow-y:hidden; -webkit-overflow-scrolling:touch;'>");

        sb.Append("<table class='tabla-productos' ");
        sb.Append("style='width:1150px; min-width:1150px; table-layout:fixed; border-collapse:collapse;'>");

        sb.Append("<thead><tr>");

        sb.Append("<th style='width:120px;'>CÓDIGO</th>");
        sb.Append("<th style='width:300px;'>PRODUCTO</th>");
        sb.Append("<th style='width:120px; text-align:center;'>DÍAS SIN VENTA</th>");
        sb.Append("<th style='width:150px; text-align:center; vertical-align:middle;'>U.M.</th>");
        sb.Append("<th style='width:1%; text-align:center; vertical-align:middle; white-space:nowrap;'>STOCK CAJAS</th>");
        sb.Append("<th style='width:1%; text-align:center; vertical-align:middle; white-space:nowrap;'>COSTO CAJAS</th>");
        sb.Append("<th style='width:1%; text-align:center; vertical-align:middle; white-space:nowrap;'>VALORIZACIÓN</th>");
        sb.Append("</tr></thead><tbody>");

        foreach (var item in productos)
        {
            sb.Append("<tr>");

            sb.Append("<td class='codigo' style='width:120px; white-space:nowrap;'>")
                .Append(WebUtility.HtmlEncode(item.Codigo))
                .Append("</td>");

            sb.Append("<td class='producto' style='width:300px; overflow-wrap:break-word; word-break:normal;'>")
                .Append(WebUtility.HtmlEncode(item.Producto))
                .Append("</td>");

            sb.Append("<td class='dias' style='width:120px; text-align:center; white-space:nowrap;'>")
                .Append(item.DiasSinVenta.ToString(CultureInfo.InvariantCulture))
                .Append("</td>");

            sb.Append("<td class='unidad' style='width:150px; text-align:center; vertical-align:middle; white-space:nowrap;'>")
                .Append(WebUtility.HtmlEncode(item.UnidadMedida))
                .Append("</td>");

            sb.Append("<td class='stock' style='width:1%; text-align:center; vertical-align:middle; white-space:nowrap;'>")
                .Append(FormatearOpcional(item.StockCajas, "0.####"))
                .Append("</td>");

            sb.Append("<td class='stock' style='width:1%; text-align:center; vertical-align:middle; white-space:nowrap;'>")
                .Append(FormatearOpcional(item.CostoPorCaja, "0.00"))
                .Append("</td>");

            sb.Append("<td class='stock' style='width:1%; text-align:center; vertical-align:middle; white-space:nowrap;'>")
                .Append(FormatearOpcional(item.Valorizacion, "0.00"))
                .Append("</td>");

            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        sb.Append("</div>");

        sb.Append("</div>");

        // FOOTER
        sb.Append("<div class='footer'>");
        sb.Append($"<p>Este es un reporte automático generado por el servicio PRODUCTOS_SIN_VENTA a las {DateTime.Now:yyyy-MM-dd HH:mm:ss}.</p>");
        sb.Append("<p>Por favor, no responda a este correo electrónico.</p>");
        sb.Append("</div>");

        sb.Append("</div></body></html>");

        return sb.ToString();
    }

    private static string FormatearOpcional(decimal? valor, string formato) =>
        valor?.ToString(formato, CultureInfo.InvariantCulture) ?? "N/D";

}
