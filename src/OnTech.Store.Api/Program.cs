using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using OnTech.Store.Api.Auth;
using OnTech.Store.Api.Data;
using OnTech.Store.Api.Infrastructure;
using OnTech.Store.Api.Services;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // When running as a Windows service the working directory is System32, so pin it to the app folder.
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Host.UseWindowsService(o => o.ServiceName = "OnTech Store Server");

builder.Services.AddStoreDatabase(builder.Configuration);
builder.Services.AddStoreAuth(builder.Configuration, builder.Environment);
builder.Services.AddScoped<StockService>();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddControllers(o => o.Filters.Add<ApiExceptionFilter>())
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "OnTech Store API",
        Version = "v1",
        Description = "Stock, customers and orders for the OnTech store. Call POST /api/auth/login, then click Authorize and paste the token.",
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header,
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>(),
    });
    foreach (var xml in new[] { "OnTech.Store.Api.xml", "OnTech.Store.Contracts.xml" })
    {
        var path = Path.Combine(AppContext.BaseDirectory, xml);
        if (File.Exists(path)) c.IncludeXmlComments(path);
    }
});

var app = builder.Build();

if (app.Services.GetRequiredService<JwtSigningKey>().Generated)
    app.Logger.LogWarning("Jwt:SigningKey is not set, so a random key was made for this run (Development only). " +
        "Sign-ins stop working when the server restarts. Set it with: dotnet user-secrets set Jwt:SigningKey <random secret>");

app.UseExceptionHandler();
app.UseStatusCodePages();

// Swagger only in Development. Swagger:Enabled (env Swagger__Enabled) can force it on or off in any mode.
if (app.Configuration.GetValue<bool?>("Swagger:Enabled") ?? app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.DocumentTitle = "OnTech Store API");
}

// No endpoint (an unknown URL, or /swagger while it is off): 404, not the fallback policy's 401.
app.Use(async (ctx, next) =>
{
    if (ctx.GetEndpoint() is null) { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    await next(ctx);
});
app.UseAuthentication();
app.UseMiddleware<MustChangePasswordMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/api/health", async (StoreDbContext db) =>
        await db.Database.CanConnectAsync()
            ? Results.Ok(new { status = "ok", version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(), time = DateTimeOffset.UtcNow })
            : Results.Problem("Database is not reachable.", statusCode: 503))
    .AllowAnonymous()
    .WithTags("Health");

await app.Services.InitializeDatabaseAsync();
app.Run();

public partial class Program;
