using PRODUCTOS_SINVENTA.DTOs;

namespace PRODUCTOS_SINVENTA.Services;

public interface IEmailSenderProductos
{
    Task<bool> EnviarAsync(
        IReadOnlyList<ProductoLoteSinVenta> productos,
        IReadOnlyList<SuscriptorCorreoDTO> destinatarios,
        DateTime fechaReferencia,
        CancellationToken cancellationToken);
}