using System.Text;

namespace JobAppHR.Security
{
    /// <summary>
    /// Buffers HTML responses and passes them through <see cref="CspStyleRewriter"/> so no inline style attributes
    /// remain. Non-HTML responses, SignalR traffic and WebSockets are streamed through untouched.
    /// Place it after UseStaticFiles so static assets are never buffered.
    /// </summary>
    public sealed class CspStyleRewriteMiddleware
    {
        private readonly RequestDelegate _next;

        public CspStyleRewriteMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.WebSockets.IsWebSocketRequest || context.Request.Path.StartsWithSegments("/hubs"))
            {
                await _next(context);
                return;
            }

            Stream original = context.Response.Body;
            using var buffer = new MemoryStream();
            context.Response.Body = buffer;

            try
            {
                await _next(context);
            }
            finally
            {
                context.Response.Body = original;
            }

            buffer.Position = 0;

            string? contentType = context.Response.ContentType;
            bool isHtml = contentType != null && contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);

            if (isHtml && buffer.Length > 0 && context.Items[CspHeaderMiddleware.NonceItemKey] is string nonce)
            {
                string html = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
                byte[] bytes = Encoding.UTF8.GetBytes(CspStyleRewriter.Rewrite(html, nonce));

                context.Response.ContentLength = bytes.Length;
                await original.WriteAsync(bytes, context.RequestAborted);
            }
            else
            {
                if (buffer.Length > 0)
                    context.Response.ContentLength = buffer.Length;

                await buffer.CopyToAsync(original, context.RequestAborted);
            }
        }
    }
}
