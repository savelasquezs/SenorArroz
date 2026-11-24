using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;

namespace SenorArroz.API.Hosting;

public static class PublicCatalogCompression
{
    public static IServiceCollection AddPublicCatalogCompression(this IServiceCollection services)
        => services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.MimeTypes = ["application/json"];
            options.Providers.Add<GzipCompressionProvider>();
        });

    // Only the public, non-personalized catalog, after authentication/authorization.
    // Never apply HTTPS compression to OTP, checkout, payments, private APIs or hubs.
    public static IApplicationBuilder UsePublicCatalogCompression(this IApplicationBuilder app)
        => app.UseWhen(context =>
            HttpMethods.IsGet(context.Request.Method)
            && context.Request.Path.Equals(new PathString("/api/public/storefront/catalog"))
            && !context.Request.QueryString.HasValue,
            branch => branch.UseResponseCompression());
}
