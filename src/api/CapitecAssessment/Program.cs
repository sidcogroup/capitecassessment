using CapitecAssessment.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Capitec Assessment API",
        Version = "v1",
        Description = "API for the Capitec assessment"
    });
});

builder.Services.AddApiVersioningConfiguration();
builder.Services.AddApiMiddlewares();
builder.Services.AddDataAccess(builder.Configuration);
builder.Services.AddApiServices();
builder.Services.AddOtp();
builder.Services.AddMessaging();
builder.Services.AddSessionAuthentication();

var app = builder.Build();

// Exception handling wraps request logging so failures are logged before being converted to a response.
app.UseExceptionHandling();
app.UseRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// In Development, Swagger UI may be served over plain http; redirecting its XHR calls to https
// turns them into cross-origin requests (307 + failed CORS preflight), so only redirect elsewhere.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();

// Exposed so the test project can host the API with WebApplicationFactory<Program>.
public partial class Program { }
