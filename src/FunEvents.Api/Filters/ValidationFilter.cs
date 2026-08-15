using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FunEvents.Api.Filters;

/// <summary>
/// Ejecuta el validador FluentValidation correspondiente a cada argumento de
/// accion antes de que el controlador se ejecute.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que existe.</b> La solucion ya registraba los validadores con
/// <c>AddValidatorsFromAssemblyContaining&lt;...&gt;()</c> pero nadie los
/// invocaba nunca. <c>CreateReservationRequestValidator</c> estaba escrito,
/// registrado en el contenedor y era codigo muerto: la regla "maximo N entradas
/// por peticion" no se aplicaba, y una cantidad negativa llegaba hasta el
/// constructor del dominio.
/// </para>
/// <para>
/// <b>Por que un filtro propio y no un paquete.</b> El paquete oficial
/// <c>FluentValidation.AspNetCore</c> esta deprecado desde la version 11 y su
/// autor recomienda invocar la validacion explicitamente. Treinta lineas sin
/// dependencias adicionales resuelven el caso y dejan claro donde ocurre.
/// </para>
/// </remarks>
public sealed class ValidationFilter : IAsyncActionFilter
{
    private readonly IServiceProvider _services;

    public ValidationFilter(IServiceProvider services) => _services = services;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (_services.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(
                new ValidationContext<object>(argument), context.HttpContext.RequestAborted);

            if (result.IsValid) continue;

            var errors = result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(errors)
            {
                Type = "https://api.funevents.com/errors/validation-failed",
                Title = "One or more validation errors occurred",
                Status = StatusCodes.Status400BadRequest,
                Instance = context.HttpContext.Request.Path
            });

            return; // Corta la ejecucion: el controlador no llega a ejecutarse.
        }

        await next();
    }
}
