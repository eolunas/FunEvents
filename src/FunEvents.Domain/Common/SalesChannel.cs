namespace FunEvents.Domain.Common;

/// <summary>
/// Canal por el que entra una venta. Es el concepto central del enunciado:
/// la misma logica de reserva se expone a tres canales distintos.
/// </summary>
/// <remarks>
/// Antes esto era un string libre ("user" / "office" / "partner") validado
/// con un switch dentro del constructor de Reservation. Un enum lo hace
/// verificable en tiempo de compilacion y elimina la clase de bug en la que
/// un canal mal escrito solo falla en runtime.
/// </remarks>
public enum SalesChannel
{
    /// <summary>Portal web propio de FunEvents (usuario final autenticado).</summary>
    Online = 0,

    /// <summary>Oficina de atencion al cliente (operador actuando por un usuario).</summary>
    Office = 1,

    /// <summary>Portal o POS de un colaborador integrado via API.</summary>
    Partner = 2
}
