using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();

var app = builder.Build();

app.MapGet("/api/health", () => "healthy");
app.MapPost("/api/ping", () => "pong");
app.MapControllers();

app.Run();
