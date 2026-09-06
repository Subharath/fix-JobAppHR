using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace JobAppHR.Security
{
    /// <summary>
    /// Moves inline <c>style="..."</c> attributes (which a CSP without 'unsafe-inline' blocks) into a single
    /// nonce-protected &lt;style&gt; block, replacing each attribute with a generated CSS class.
    /// Script/style element bodies and HTML comments are left untouched.
    /// </summary>
    public static class CspStyleRewriter
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        private static readonly Regex TokenRegex = new(
            @"<!--.*?-->|<script\b.*?</script\s*>|<style\b.*?</style\s*>|<(?<tag>[a-zA-Z][a-zA-Z0-9:-]*)(?<attrs>(?:\s+[^\s""'<>/=]+(?:\s*=\s*(?:""[^""]*""|'[^']*'|[^\s""'=<>`]+))?)*)\s*/?>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex AttrRegex = new(
            @"\s+(?<name>[^\s""'<>/=]+)(?:\s*=\s*(?:""(?<dq>[^""]*)""|'(?<sq>[^']*)'|(?<uq>[^\s""'=<>`]+)))?",
            RegexOptions.Singleline | RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex BodyCloseRegex = new(
            @"</body\s*>", RegexOptions.IgnoreCase | RegexOptions.RightToLeft | RegexOptions.Compiled, RegexTimeout);

        public static string Rewrite(string html, string nonce)
        {
            if (string.IsNullOrEmpty(html) || html.IndexOf("style", StringComparison.OrdinalIgnoreCase) < 0)
                return html;

            try
            {
                var rules = new Dictionary<string, string>(StringComparer.Ordinal);

                string rewritten = TokenRegex.Replace(html, m => RewriteTag(m, rules));
                if (rules.Count == 0)
                    return html;

                var css = new StringBuilder();
                foreach (var rule in rules)
                    css.Append('.').Append(rule.Key).Append(":not(#_):not(#_){").Append(rule.Value).Append("}\n");

                string block = $"<style nonce=\"{nonce}\">\n{css}</style>";

                Match close = BodyCloseRegex.Match(rewritten);
                return close.Success
                    ? rewritten.Insert(close.Index, block + "\n")
                    : rewritten + "\n" + block;
            }
            catch (RegexMatchTimeoutException)
            {
                return html;
            }
        }

        private static string RewriteTag(Match m, Dictionary<string, string> rules)
        {
            if (!m.Groups["tag"].Success)
                return m.Value;

            string attrs = m.Groups["attrs"].Value;
            if (attrs.IndexOf("style", StringComparison.OrdinalIgnoreCase) < 0)
                return m.Value;

            string? css = null;
            string? existingClass = null;
            var kept = new StringBuilder();

            foreach (Match a in AttrRegex.Matches(attrs))
            {
                string name = a.Groups["name"].Value;
                string value = a.Groups["dq"].Success ? a.Groups["dq"].Value
                             : a.Groups["sq"].Success ? a.Groups["sq"].Value
                             : a.Groups["uq"].Value;

                if (name.Equals("style", StringComparison.OrdinalIgnoreCase))
                    css = value;
                else if (name.Equals("class", StringComparison.OrdinalIgnoreCase))
                    existingClass = value;
                else
                    kept.Append(a.Value);
            }

            if (css == null)
                return m.Value;

            string? className = RegisterRule(css, rules);

            var tag = new StringBuilder("<").Append(m.Groups["tag"].Value).Append(kept);

            string classValue = (existingClass ?? string.Empty).Trim();
            if (className != null)
                classValue = (classValue + " " + className).Trim();
            if (classValue.Length > 0)
                tag.Append(" class=\"").Append(classValue.Replace("\"", "&quot;")).Append('"');

            tag.Append(m.Value.EndsWith("/>", StringComparison.Ordinal) ? " />" : ">");
            return tag.ToString();
        }

        private static string? RegisterRule(string rawCss, Dictionary<string, string> rules)
        {
            string css = WebUtility.HtmlDecode(rawCss).Trim();

            // Refuse anything that could break out of the declaration block or the <style> element.
            if (css.Length == 0 || css.IndexOfAny(new[] { '{', '}', '<', '>', '\\' }) >= 0)
                return null;

            string name = "csp-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(css)), 0, 6).ToLowerInvariant();
            rules.TryAdd(name, css);
            return name;
        }
    }
}
