using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PartyTown.Controllers;
using PartyTown.Grains;

namespace PartyTown.Services.Auth;

/// <summary>Lets a write through to a stopped World: starting it again, or moving out of it.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowWhenStoppedAttribute : Attribute;

/// <summary>A stopped World keeps everything and can be looked around in, but takes no writes.</summary>
public sealed class StoppedWorldFilter(IGrainFactory grains) : IAsyncActionFilter
{
    public const string Title = "This World is stopped.";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (IsWrite(context.HttpContext.Request.Method)
            && !context.ActionDescriptor.EndpointMetadata.OfType<AllowWhenStoppedAttribute>().Any()
            && WorldOf(context) is { } worldId
            && await grains.GetGrain<IPartyRootGrain>(Guid.Empty).IsStoppedAsync(worldId))
        {
            context.Result = new ObjectResult(new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = Title })
            {
                StatusCode = StatusCodes.Status409Conflict,
            };
            return;
        }
        await next();
    }

    private static bool IsWrite(string method)
        => !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method);

    /// <summary>World routes name it partyId; PartyController's own routes call it id.</summary>
    private static Guid? WorldOf(ActionExecutingContext context)
    {
        var key = context.Controller is PartyController ? "id" : "partyId";
        return context.RouteData.Values.TryGetValue(key, out var value) && Guid.TryParse(value?.ToString(), out var id) ? id : null;
    }
}
