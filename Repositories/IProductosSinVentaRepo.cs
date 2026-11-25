using PRODUCTOS_SINVENTA.DTOs;

namespace PRODUCTOS_SINVENTA.Repositories;

public interface IProductosSinVentaRepo
{
    Task<int?> ObtenerVersionConfiguracionAsync(CancellationToken cancellationToken);

    Task<ConfiguracionRutinaDTO> ObtenerConfiguracionRutinaAsync(
        string codigoRutina,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SuscriptorCorreoDTO>> ObtenerSuscriptoresCorreoAsync(
        string codigoRutina,
        CancellationToken cancellationToken);

    Task<ResultadoNotificacionIntranetDTO> RegistrarNotificacionIntranetAsync(
        string codigoRutina,
        DateTime fechaReferencia,
        DateTime fechaDeteccion,
        int cantidadProductos,
        string detalleJson,
        string titulo,
        string mensaje,
        CancellationToken cancellationToken);
}
