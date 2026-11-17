using CapitecAssessment.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddApiVersioningConfiguration();
builder.Services.AddApiMiddlewares();
builder.Services.AddDataAccess(builder.Configuration);
builder.Services.AddApiServices();

var app = builder.Build();

// Exception handling wraps request logging so failures are logged before being converted to a response.
app.UseExceptionHandling();
app.UseRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposed so the test project can host the API with WebApplicationFactory<Program>.
public partial class Program { }
