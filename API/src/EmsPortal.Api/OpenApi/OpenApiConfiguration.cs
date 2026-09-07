using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace EmsPortal.Api.OpenApi;

/// <summary>Native OpenAPI document configuration (WO-31).</summary>
public static class OpenApiConfiguration
{
    public static IServiceCollection AddEmsPortalOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer<SecuritySchemeDocumentTransformer>();
        });

        return services;
    }
}

/// <summary>Adds platform metadata and the two authentication schemes to the OpenAPI document.</summary>
internal sealed class SecuritySchemeDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "EMS Portal API",
            Version = "v1",
            Description = "Administration platform API: tenants, users, and auth.",
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes[AuthenticationSchemes.Jwt] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Name = "Authorization",
            Description = "Platform-issued JWT bearer token.",
        };
        document.Components.SecuritySchemes[AuthenticationSchemes.ApiKey] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-Api-Key",
            Description = "Machine-to-machine API key.",
        };

        document.SecurityRequirements.Add(new OpenApiSecurityRequirement
        {
            [Reference(AuthenticationSchemes.Jwt)] = Array.Empty<string>(),
            [Reference(AuthenticationSchemes.ApiKey)] = Array.Empty<string>(),
        });

        return Task.CompletedTask;
    }

    private static OpenApiSecurityScheme Reference(string id) => new()
    {
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = id },
    };
}
