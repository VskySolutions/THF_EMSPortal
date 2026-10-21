using EmsPortal.Shared.Configuration;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace EmsPortal.Api.Filters;

/// <summary>Which ceiling under Limits:Uploads an upload runs against.</summary>
public enum UploadKind
{
    File,
    BrandingImage,
    BrandingBackground,
}

/// <summary>
/// <c>[RequestSizeLimit]</c> with the number read from configuration at request time instead of compiled in, so
/// the ceiling can be raised on the server. Multipart framing gets a little room on top of the file itself.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UploadSizeLimitAttribute : Attribute, IFilterFactory
{
    private const long FramingAllowance = 64 * 1024;

    private readonly UploadKind _kind;

    public UploadSizeLimitAttribute(UploadKind kind)
    {
        _kind = kind;
    }

    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        var uploads = serviceProvider.GetRequiredService<IOptions<LimitsOptions>>().Value.Uploads;
        var bytes = _kind switch
        {
            UploadKind.BrandingImage => uploads.BrandingImageBytes,
            UploadKind.BrandingBackground => uploads.BrandingBackgroundBytes,
            _ => uploads.MaxFileBytes,
        };
        return new BodySizeFilter(bytes + FramingAllowance);
    }

    private sealed class BodySizeFilter : IAuthorizationFilter
    {
        private readonly long _maxBytes;

        public BodySizeFilter(long maxBytes)
        {
            _maxBytes = maxBytes;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var feature = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false })
            {
                feature.MaxRequestBodySize = _maxBytes;
            }
        }
    }
}
