using GridKit.Http;
using Microsoft.AspNetCore.Authentication;
using SampleApi.Auth;
using SampleApi.Data;

var builder = WebApplication.CreateBuilder(args);

// The demo authentication handler trusts request headers for tenant and role. That is only acceptable
// on a developer machine, so the sample refuses to start anywhere else. A real deployment replaces it
// with the identity provider's scheme and reads tenant and roles from the token.
if (!builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "SampleApi uses a demo authentication handler and only runs in the Development environment.");
}

builder.Services.AddControllers();
builder.Services.AddGridKit();
builder.Services
    .AddAuthentication(DemoAuthHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DemoAuthHandler>(DemoAuthHandler.SchemeName, configureOptions: null);
builder.Services.AddAuthorization();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:4200")
    .AllowAnyHeader()
    .AllowAnyMethod()));
builder.Services.AddSingleton(sp => new ShipmentConnectionFactory(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Shipments") ?? "Data Source=shipments.db"));

var app = builder.Build();

ShipmentDatabase.EnsureSeeded(
    app.Services.GetRequiredService<ShipmentConnectionFactory>().ConnectionString,
    app.Configuration.GetValue("Seed:Rows", 120_000),
    app.Logger);

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapControllers();

app.Run();

/// <summary>Entry point; public so integration tests can host the API in memory.</summary>
public partial class Program;
