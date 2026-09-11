using JobAppHR.Models;
using JobAppHR.Repository;
using JobAppHR.Services;
using JobAppHR.Hubs;
using JobAppHR.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

var enableDevUserFallback = builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Authentication:EnableDevUserFallback");

// Add services to the container.
builder.Services.AddControllersWithViews();

// Add SignalR for real-time collaborative screening
builder.Services.AddSignalR();

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromMinutes(10);
});

builder.Services.Configure<FormOptions>(x => x.ValueCountLimit = 10000);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        // When dev fallback is enabled, redirect to DevLogin instead of AzureLogin
        options.LoginPath = enableDevUserFallback ? "/Home/DevLogin" : "/Home/AzureLogin";
        options.Cookie.Name = "JobAppHRWebCookie";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(20);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(options =>
{
    if (enableDevUserFallback)
    {
        // DEV/TEST: Require authentication (must log in via DevLogin page)
        // but don't enforce specific claims — any authenticated user is allowed
        options.AddPolicy("NormalUserPolicy",
            policy => policy.RequireAuthenticatedUser());

        options.AddPolicy("AdminUserPolicy",
            policy => policy.RequireAuthenticatedUser());
    }
    else
    {
        // PRODUCTION: Proper claim-based policies (Azure AD login)
        options.AddPolicy("NormalUserPolicy",
            policy => policy.RequireClaim("UserRole", "Normal", "Admin"));

        options.AddPolicy("AdminUserPolicy",
            policy => policy.RequireClaim("UserRole", "Admin"));
    }
});

builder.Services.AddScoped<IDBOperations, DBOperations>();
builder.Services.AddScoped<IUtilityFn, UtilityFn>();
builder.Services.AddScoped<IFilterProcess, FilterProcess>();
builder.Services.AddScoped<IManualProcess, ManualProcess>();
builder.Services.AddScoped<IFastAPIProcess, FastAPIProcess>();
builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

var app = builder.Build();

// Security Headers Middleware: Positioned at the beginning of the pipeline to guarantee headers on all responses (redirects, static files, error pages).
// Emits a strict nonce-based Content-Security-Policy (no 'unsafe-inline' / 'unsafe-eval' / wildcard sources).
app.UseMiddleware<CspHeaderMiddleware>();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

if (app.Environment.IsDevelopment())
    StaticData.BaseUrl = app.Configuration["URL:BaseUrl-Local"];
else 
    StaticData.BaseUrl = app.Configuration["URL:BaseUrl-Production"];

StaticData.DefaultConnection = app.Configuration["ConnectionStrings:DefaultConnection"];
StaticData.UploadPath = app.Configuration["Application:UploadPath"];
StaticData.FastAPIUrl = app.Configuration["URL:FastAPIUrl"];

app.UseHttpsRedirection();

app.UseStatusCodePages(async context => {
    if (context.HttpContext.Response.StatusCode == 404)
    {
        context.HttpContext.Response.Redirect("/Home/AccessDenied");
    }
});

app.UseStaticFiles();

// Moves inline style="..." attributes into a nonce-protected <style> block (after static files so assets are never buffered)
app.UseMiddleware<CspStyleRewriteMiddleware>();

app.UseRouting();

// Middleware order matches production: Authentication -> Authorization -> Session
app.UseAuthentication();
app.UseAuthorization();

app.UseSession();

app.UseCookiePolicy(
new CookiePolicyOptions
{
    Secure = app.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Map the SignalR hub for real-time screening
app.MapHub<ScreeningHub>("/hubs/screening");

app.Run();
