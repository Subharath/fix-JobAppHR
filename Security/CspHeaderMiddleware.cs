using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace JobAppHR.Security
{
    /// <summary>
    /// Emits the security headers (including a strict, nonce-based Content-Security-Policy) on every response.
    /// Must be the first middleware in the pipeline so redirects, error pages and short-circuited responses are covered.
    /// </summary>
    public sealed class CspHeaderMiddleware
    {
        public const string NonceItemKey = "CspNonce";
        public const string NonceRequestHeader = "X-CSP-Nonce";

        private static readonly Regex NonceFormat = new("^[A-Za-z0-9+/=_-]{16,64}$", RegexOptions.Compiled);

        private readonly RequestDelegate _next;

        public CspHeaderMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            string nonce = ResolveNonce(context);
            context.Items[NonceItemKey] = nonce;

            string csp = BuildPolicy(nonce);
            ApplyHeaders(context.Response.Headers, csp);

            // Re-apply when the response starts so nothing downstream can drop the headers.
            context.Response.OnStarting(() =>
            {
                ApplyHeaders(context.Response.Headers, csp);
                return Task.CompletedTask;
            });

            await _next(context);
        }

        private static string BuildPolicy(string nonce) =>
            "default-src 'self'; " +
            $"script-src 'self' 'nonce-{nonce}'; " +
            $"style-src 'self' 'nonce-{nonce}'; " +
            "img-src 'self' data:; " +
            "font-src 'self' data:; " +
            "connect-src 'self'; " +
            "object-src 'none'; " +
            "frame-ancestors 'none'; " +
            "form-action 'self' https://login.microsoftonline.com; " +
            "base-uri 'self';";

        private static void ApplyHeaders(IHeaderDictionary headers, string csp)
        {
            headers["X-Frame-Options"] = "DENY";
            headers["X-XSS-Protection"] = "1; mode=block";
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Content-Security-Policy"] = csp;
        }

        /// <summary>
        /// A fresh random nonce per request. AJAX-loaded HTML fragments (jQuery .load) contain inline scripts that must
        /// carry the nonce of the page that hosts them, so same-origin XHR requests may echo the page nonce back via a
        /// custom header (cross-origin pages cannot set custom headers without CORS, which this app does not enable).
        /// </summary>
        private static string ResolveNonce(HttpContext context)
        {
            bool isXhr = string.Equals(context.Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
            if (isXhr)
            {
                string? supplied = context.Request.Headers[NonceRequestHeader].FirstOrDefault();
                if (!string.IsNullOrEmpty(supplied) && NonceFormat.IsMatch(supplied))
                    return supplied;
            }

            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        }
    }
}
