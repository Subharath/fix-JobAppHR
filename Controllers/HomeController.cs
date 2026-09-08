using DocumentFormat.OpenXml.Spreadsheet;
using JobAppHR.Models;
using JobAppHR.Repository;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Data;

namespace JobAppHR.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IDBOperations _DBOperations;
        private readonly IUtilityFn _UtilityFn;
        private readonly IConfiguration _configuration;

        public HomeController(ILogger<HomeController> logger, IDBOperations dbOperations, IUtilityFn utilityFn, IConfiguration configuration)
        {
            _logger = logger;
            _DBOperations = dbOperations;
            _UtilityFn = utilityFn;
            _configuration = configuration;
        }

        [Authorize(Policy = "NormalUserPolicy")]
        public IActionResult Index()
        {
            ViewBag.Message = StaticData.BaseUrl;
            return View("Home");
        }

        [Authorize(Policy = "NormalUserPolicy")]
        public IActionResult Settings()
        {
            ViewData["Title"] = "Appearance Settings";
            return View();
        }

        public RedirectResult AzureLogin()
        {
            // Security: Retrieve OAuth credentials from configuration instead of hard-coding in source (CWE-798)
            string tenantId = _configuration["Authentication:AzureAd:TenantId"] ?? "";
            string clientId = _configuration["Authentication:AzureAd:ClientId"] ?? "";
            string redirectUri = StaticData.BaseUrl + "/Home/UAzure";
            string scope = _configuration["Authentication:AzureAd:Scope"] ?? "openid profile offline_access user.read";
            string responseMode = "query";

            // OAuth Best Practice (RFC 6749 Section 10.12): Cryptographically secure random state to protect against CSRF
            byte[] stateBytes = new byte[32];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(stateBytes);
            }
            string state = Convert.ToBase64String(stateBytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            HttpContext.Session.SetString("OAuth_State", state);

            string redirectUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize?response_type=code&client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={Uri.EscapeDataString(scope)}&response_mode={responseMode}&state={Uri.EscapeDataString(state)}";
            return Redirect(redirectUrl);
        }

        // OAuth 2.0 / OpenID Connect callback endpoint
        public async Task<ActionResult> UAzure(string code, string? state = null)
        {
            try
            {
                // OAuth Best Practice: Validate state parameter to mitigate CSRF attacks (RFC 6749 Section 10.12)
                string? expectedState = HttpContext.Session.GetString("OAuth_State");
                HttpContext.Session.Remove("OAuth_State"); // Enforce single-use state token

                if (string.IsNullOrEmpty(state) || string.IsNullOrEmpty(expectedState) || state != expectedState)
                {
                    _logger.LogWarning("OAuth state validation failed in UAzure callback. Potential CSRF attempt.");
                    return BadRequest("OAuth state validation failed. Possible CSRF attack detected.");
                }

                if (string.IsNullOrWhiteSpace(code))
                {
                    return BadRequest("Authorization code is missing from response.");
                }

                // Security: Load OAuth client credentials from configuration
                string clientId = _configuration["Authentication:AzureAd:ClientId"] ?? "";
                string clientSecret = _configuration["Authentication:AzureAd:ClientSecret"] ?? "";
                string tenantId = _configuration["Authentication:AzureAd:TenantId"] ?? "";
                string redirectUri = StaticData.BaseUrl + "/Home/UAzure";
                string tokenEndpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";

                using (HttpClient httpClient = new HttpClient())
                {
                    // Construct the OAuth 2.0 authorization_code grant request
                    var postData = new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("client_id", clientId),
                        new KeyValuePair<string, string>("client_secret", clientSecret),
                        new KeyValuePair<string, string>("code", code),
                        new KeyValuePair<string, string>("redirect_uri", redirectUri),
                        new KeyValuePair<string, string>("grant_type", "authorization_code")
                    };

                    // Request access token
                    HttpResponseMessage tokenResponse = await httpClient.PostAsync(tokenEndpoint, new FormUrlEncodedContent(postData));

                    if (tokenResponse.IsSuccessStatusCode)
                    {
                        // Read and parse token response
                        string tokenResponseContent = await tokenResponse.Content.ReadAsStringAsync();
                        dynamic tokenJson = JObject.Parse(tokenResponseContent);
                        string accessToken = tokenJson.access_token;

                        // Use access token to retrieve user identity details via Microsoft Graph API (OIDC UserInfo)
                        string graphApiEndpoint = "https://graph.microsoft.com/v1.0/me";
                        using (HttpClient graphClient = new HttpClient())
                        {
                            graphClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                            HttpResponseMessage userResponse = await graphClient.GetAsync(graphApiEndpoint);

                            if (userResponse.IsSuccessStatusCode)
                            {
                                // Read and parse user details
                                string userResponseContent = await userResponse.Content.ReadAsStringAsync();
                                dynamic userJson = JObject.Parse(userResponseContent);

                                string userEmail = (string)(userJson.mail ?? userJson.userPrincipalName);
                                string userName = (string)userJson.displayName;
                                string userId = (string)userJson.userPrincipalName;
                                string userGroup = "";
                                string userRole = "Normal";

                                if (!string.IsNullOrEmpty(userId) && userId.Length >= 6)
                                {
                                    userId = userId.Substring(0, 6);
                                }

                                HttpContext.Session.SetString("UserName", userName ?? "User");
                                HttpContext.Session.SetString("UserId", userId ?? "");

                                // get the user group (SQL injection safe lookup)
                                string safeUserId = (userId ?? "").Replace("'", "''");
                                string sql = "SELECT UserGroup, UserEmail FROM Users WHERE UserId = '" + safeUserId + "' AND ActiveStatus = 'ACTIVE'";
                                DataTable dataTable = _DBOperations.SelectRows(sql);

                                if (dataTable.Rows.Count > 0)
                                {
                                    userGroup = dataTable.Rows[0]["UserGroup"].ToString() ?? "";
                                    if (userGroup == "0" || userGroup == "1")
                                    {
                                        userRole = "Admin";
                                    }
                                }

                                var claims = new List<Claim>
                                {
                                    new Claim("UserRole", userRole),
                                    new Claim("UserId", userId ?? ""),
                                    new Claim("UserName", userName ?? "User"),
                                    new Claim("UserEmail", userEmail ?? ""),
                                    new Claim("UserGroup", userGroup),
                                };

                                var claimsIdentity = new ClaimsIdentity(
                                    claims, CookieAuthenticationDefaults.AuthenticationScheme);

                                await HttpContext.SignInAsync(
                                    CookieAuthenticationDefaults.AuthenticationScheme,
                                    new ClaimsPrincipal(claimsIdentity));

                                return View("Home");
                            }
                            else
                            {
                                return StatusCode((int)userResponse.StatusCode);
                            }
                        }
                    }
                    else
                    {
                        return StatusCode((int)tokenResponse.StatusCode);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during UAzure OAuth callback");
                return StatusCode(500);
            }
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult AccessDenied()
        {
            return View();
        }

        /// <summary>
        /// Dev login page — only available when EnableDevUserFallback is true.
        /// Allows testers to login as different users to test multi-user collaborative screening.
        /// </summary>
        [AllowAnonymous]
        public IActionResult DevLogin(string? returnUrl)
        {
            // Only allow dev login when fallback is enabled
            var enableDevFallback = _configuration.GetValue<bool>("Authentication:EnableDevUserFallback");

            if (!enableDevFallback)
                return RedirectToAction("AzureLogin");

            // Get list of active users from DB
            string sql = "SELECT UserId, UserGroup FROM Users WHERE ActiveStatus = 'ACTIVE' ORDER BY UserId";
            DataTable userTable = _DBOperations.SelectRows(sql);
            ViewBag.UserList = userTable;
            ViewBag.ReturnUrl = returnUrl ?? "/";
            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> DevLogin(string userId, string? returnUrl)
        {
            var enableDevFallback = _configuration.GetValue<bool>("Authentication:EnableDevUserFallback");

            if (!enableDevFallback)
                return RedirectToAction("AzureLogin");

            // Look up user in DB
            string sql = "SELECT UserId, UserGroup FROM Users WHERE UserId = '" + userId + "' AND ActiveStatus = 'ACTIVE'";
            DataTable dataTable = _DBOperations.SelectRows(sql);

            if (dataTable.Rows.Count == 0)
            {
                ViewBag.Error = "User not found or inactive.";
                return DevLogin(returnUrl);
            }

            string userGroup = dataTable.Rows[0]["UserGroup"].ToString() ?? "";
            string userRole = (userGroup == "0" || userGroup == "1") ? "Admin" : "Normal";
            string userName = "Dev-" + userId; // Display name for dev

            var claims = new List<Claim>
            {
                new Claim("UserRole", userRole),
                new Claim("UserId", userId),
                new Claim("UserName", userName),
                new Claim("UserEmail", userId + "@dev.local"),
                new Claim("UserGroup", userGroup),
            };

            var claimsIdentity = new ClaimsIdentity(
                claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity));

            // Store in session too
            HttpContext.Session.SetString("UserId", userId);
            HttpContext.Session.SetString("UserName", userName);
            HttpContext.Session.SetString("UserEmail", userId + "@dev.local");
            HttpContext.Session.SetString("UserGroup", userGroup);
            HttpContext.Session.SetString("UserRole", userRole);

            return Redirect(returnUrl ?? "/");
        }

        [AllowAnonymous]
        public async Task<IActionResult> Logout()
        {
            // Clear the authentication cookie
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // Clear the session
            HttpContext.Session.Clear();

            // Redirect back to home (which will force a login)
            return RedirectToAction("Index");
        }
    }
}