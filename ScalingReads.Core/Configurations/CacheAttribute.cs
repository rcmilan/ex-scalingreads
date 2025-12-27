using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Distributed;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ScalingReads.Core.Configurations;

[AttributeUsage(AttributeTargets.Method)]
public class CacheAttribute(int ttlSeconds = 60) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var cache = context.HttpContext.RequestServices.GetRequiredService<IDistributedCache>();

        var cacheKey = BuildCacheKey(context);

        var cached = await cache.GetStringAsync(cacheKey);

        if (cached is not null)
        {
            var result = JsonSerializer.Deserialize<object>(cached);
            context.Result = new OkObjectResult(result);
            return;
        }

        var executed = await next();

        if (executed.Result is ObjectResult objectResult && objectResult.Value is not null)
        {
            var data = JsonSerializer.Serialize(objectResult.Value);

            await cache.SetStringAsync(
                cacheKey,
                data,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow =
                        TimeSpan.FromSeconds(ttlSeconds)
                });
        }
    }

    private static string BuildCacheKey(ActionExecutingContext context)
    {
        var route = context.HttpContext.Request.Path.Value ?? "";

        if (context.ActionDescriptor is not ControllerActionDescriptor controllerAction)
            return $"endpoint:{route}";

        var relevantArgs = new Dictionary<string, object?>();

        foreach (var param in controllerAction.MethodInfo.GetParameters())
        {
            // se for [FromServices], pula
            if (param.GetCustomAttributes(typeof(FromServicesAttribute), false).Length != 0)
                continue;

            if (context.ActionArguments.TryGetValue(param.Name!, out var value))
            {
                relevantArgs[param.Name!] = value;
            }
        }

        // Serializamos somente os parâmetros “relevantes”
        var argsJson = JsonSerializer.Serialize(relevantArgs);

        // Hash para manter a chave compacta
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(argsJson));
        var hash = Convert.ToHexString(hashBytes);

        return $"endpoint:{route}:{hash}";
    }
}
