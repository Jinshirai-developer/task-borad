using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TaskApi.Configuration;

namespace TaskApi.Authentication;

public static class PublicProxyMiddleware
{
    public static void UseTaskBoardPublicProxy(this WebApplication app)
    {
        var secret = app.Services.GetRequiredService<IOptions<PublicProxyOptions>>().Value.Secret;
        if (string.IsNullOrEmpty(secret)) return;
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        var origin = new Uri(app.Services.GetRequiredService<IOptions<Configuration.AuthenticationOptions>>().Value.PublicBaseUrl);
        app.Use(async (context, next) =>
        {
            var supplied = context.Request.Headers["X-TaskBoard-Proxy-Key"].ToString();
            context.Request.Headers.Remove("X-TaskBoard-Proxy-Key");
            if (supplied.Length > 128 || !CryptographicOperations.FixedTimeEquals(expected,
                    SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                context.Response.Headers.CacheControl = "no-store";
                return;
            }
            // The authenticated gateway overwrites this header. Never trust client-supplied Forwarded headers.
            var clientIp = context.Request.Headers["X-TaskBoard-Client-IP"].ToString();
            context.Request.Headers.Remove("X-TaskBoard-Client-IP");
            if (IPAddress.TryParse(clientIp, out var address)) context.Connection.RemoteIpAddress = address;
            context.Request.Scheme = origin.Scheme;
            context.Request.Host = HostString.FromUriComponent(origin);
            await next(context);
        });
    }
}
