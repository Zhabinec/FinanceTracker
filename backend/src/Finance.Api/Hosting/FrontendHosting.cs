using Microsoft.AspNetCore.StaticFiles;

namespace Finance.Api.Hosting;

public static class FrontendHosting
{
    public static void UseHostedFrontend(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Hosting:ServeFrontend")) return;
        var index = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html");
        if (!File.Exists(index)) throw new InvalidOperationException("Frontend build is missing. Use deploy/render.Dockerfile to build the combined application.");
        app.Use(async (context, next) =>
        {
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self' https://telegram.org; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'self' https://web.telegram.org https://*.telegram.org https://t.me";
            await next(context);
        });
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context => context.Context.Response.Headers.CacheControl =
                context.Context.Request.Path.StartsWithSegments("/assets") ? "public,max-age=31536000,immutable" : "no-cache"
        });
        app.MapFallback(async context =>
        {
            var path = context.Request.Path;
            // Unknown API routes and missing assets must stay 404, never return an HTML login page.
            if ((!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)) ||
                path.StartsWithSegments("/api") || path.StartsWithSegments("/health") || Path.HasExtension(path.Value))
            { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-cache";
            await context.Response.SendFileAsync(index, context.RequestAborted);
        });
    }
}
