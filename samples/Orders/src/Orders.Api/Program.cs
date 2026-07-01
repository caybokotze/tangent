using Orders.Api.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOrdersServices();

var app = builder.Build();

app.MapControllers();

app.Run();
