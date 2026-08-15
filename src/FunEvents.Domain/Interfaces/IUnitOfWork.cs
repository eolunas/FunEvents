namespace FunEvents.Domain.Interfaces;

/// <summary>
/// Frontera transaccional del caso de uso.
/// </summary>
/// <remarks>
/// <para>
/// Existe por dos motivos concretos, ninguno decorativo:
/// </para>
/// <para>
/// 1. <b>Antes no habia transaccion.</b> El servicio de reservas incrementaba
/// el contador del evento con un UPDATE que se ejecuta de inmediato y despues
/// insertaba la reserva. Si el INSERT fallaba, el cupo quedaba consumido por
/// una reserva que no existe: aforo perdido de forma permanente.
/// </para>
/// <para>
/// 2. <b>Application no debe conocer EF Core.</b> Sin esta abstraccion, el
/// servicio tendria que hacer <c>DbContext.Database.BeginTransactionAsync()</c>,
/// lo que arrastra la dependencia de infraestructura hasta la capa de casos de
/// uso y rompe la regla de dependencia.
/// </para>
/// <para>
/// La implementacion envuelve la operacion en la execution strategy del
/// proveedor, que es el unico modo correcto de combinar transacciones
/// explicitas con reintentos de conexion.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Persiste los cambios pendientes del contexto actual.</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Ejecuta <paramref name="operation"/> dentro de una unica transaccion.
    /// Commit si termina bien, rollback ante cualquier excepcion.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken ct = default);

    /// <inheritdoc cref="ExecuteInTransactionAsync{TResult}"/>
    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation, CancellationToken ct = default);
}
