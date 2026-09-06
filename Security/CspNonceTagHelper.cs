using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace JobAppHR.Security
{
    /// <summary>
    /// Adds the per-request CSP nonce to developer-authored &lt;script&gt; and &lt;style&gt; elements in Razor views.
    /// Elements injected from data (not authored in a view) never receive a nonce, so they stay blocked by CSP.
    /// </summary>
    [HtmlTargetElement("script")]
    [HtmlTargetElement("style")]
    public class CspNonceTagHelper : TagHelper
    {
        [ViewContext]
        [HtmlAttributeNotBound]
        public ViewContext ViewContext { get; set; } = default!;

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            if (output.Attributes.ContainsName("nonce"))
                return;

            if (ViewContext.HttpContext.Items[CspHeaderMiddleware.NonceItemKey] is string nonce)
                output.Attributes.SetAttribute("nonce", nonce);
        }
    }
}
