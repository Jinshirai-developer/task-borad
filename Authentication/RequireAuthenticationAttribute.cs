using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TaskApi.Models;
using TaskApi.Services;

namespace TaskApi.Authentication;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class RequireAuthenticationAttribute : Attribute, IAsyncAuthorizationFilter
{
    private static readonly object AuthenticatedUserKey = new();

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var metadata = context.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any()) return;

        var manager = context.HttpContext.RequestServices.GetRequiredService<UserManager<UserProfile>>();
        var user = context.HttpContext.User.Identity?.IsAuthenticated == true
            ? await manager.GetUserAsync(context.HttpContext.User) : null;
        if (user == null)
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                message = "ログインの有効期限が切れています。再度ログインしてください。"
            });
            return;
        }

        if (!metadata.OfType<AllowAccountSetupAttribute>().Any()
            && (!user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email)
                || AuthenticationService.RequiresTerms(user)))
        {
            context.Result = new ObjectResult(new
            {
                code = "account_setup_required",
                message = "メール確認と利用規約の確認を完了してください。"
            }) { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }
        context.HttpContext.Items[AuthenticatedUserKey] = user;
    }

    public static UserProfile GetAuthenticatedUser(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(AuthenticatedUserKey, out var value) && value is UserProfile user
            ? user : throw new InvalidOperationException("認証済みユーザーが設定されていません。");
}
