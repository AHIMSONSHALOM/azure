using ProductHub_MVC.Data;
using ProductHub_MVC.Middlewares;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Add services to the MVC container
builder.Services.AddControllersWithViews();

// Register your custom MS-SQL Database Context Utility
builder.Services.AddScoped<SqlDbContext>();
builder.Services.AddScoped<ProductHub_MVC.Services.InternetDiscoveryService>();
builder.Services.AddMemoryCache();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("ProductHubSqlConnection")));
builder.Services.AddHostedService<ProductHub_MVC.Services.DatabaseSyncService>();
builder.Services.AddHostedService<ProductHub_MVC.Services.ProductSyncService>();
builder.Services.AddHostedService<ProductHub_MVC.Services.ProductEnrichmentService>();
builder.Services.AddHostedService<ProductHub_MVC.Services.ImageCollectionService>();
builder.Services.AddHostedService<ProductHub_MVC.Services.ProductRefreshService>();

// Register Cookie and Google Authentication services
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = "Google";
})
.AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(20);
    options.SlidingExpiration = true;
})
.AddOpenIdConnect("Google", options =>
{
    options.Authority = "https://accounts.google.com";
    options.ClientId = builder.Configuration["Authentication:Google:ClientId"] ?? string.Empty;
    options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? string.Empty;
    options.CallbackPath = "/signin-google";
    options.ResponseType = OpenIdConnectResponseType.Code;
    options.SaveTokens = false;
    options.GetClaimsFromUserInfoEndpoint = true;
    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");
    options.TokenValidationParameters.NameClaimType = "name";
    options.TokenValidationParameters.RoleClaimType = "role";
    options.Events = new OpenIdConnectEvents
    {
        OnRedirectToIdentityProvider = context =>
        {
            context.ProtocolMessage.Prompt = "select_account";
            return Task.CompletedTask;
        },
        OnUserInformationReceived = context =>
        {
            if (context.Principal?.Identity is ClaimsIdentity identity)
            {
                AddGoogleClaimIfMissing(identity, "sub", context.User.RootElement);
                AddGoogleClaimIfMissing(identity, "email", context.User.RootElement);
                AddGoogleClaimIfMissing(identity, "name", context.User.RootElement);
                AddGoogleClaimIfMissing(identity, "picture", context.User.RootElement);
            }

            return Task.CompletedTask;
        }
    };
});

// =========================================================================
// REGISTER SESSIONS AND COOKIE CONTEXT PROPERTIES FOR AUTHENTICATION
// =========================================================================
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20); // Session auto expires after 20 minutes of inactivity
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Enable the Session infrastructure state mechanism
app.UseSession();

// Register the custom Session Validation Middleware
app.UseMiddleware<SessionValidationMiddleware>();

// Enable authentication and authorization middleware in correct sequence
app.UseAuthentication();
app.UseAuthorization();

// =========================================================================
// ✅ DEFAULT ROUTE MODIFIED: Route directly to Account/Login gatekeeper on load
// =========================================================================
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();

static void AddGoogleClaimIfMissing(ClaimsIdentity identity, string claimType, System.Text.Json.JsonElement userInfo)
{
    if (identity.HasClaim(claim => claim.Type == claimType)) return;

    if (userInfo.TryGetProperty(claimType, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String)
    {
        string? claimValue = value.GetString();
        if (!string.IsNullOrWhiteSpace(claimValue))
        {
            identity.AddClaim(new Claim(claimType, claimValue));
        }
    }
}
