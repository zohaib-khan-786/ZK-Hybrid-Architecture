using Microsoft.EntityFrameworkCore;
using ZMAMedium.Application.CatalogModule.Interfaces;
using ZMAMedium.Application.CatalogModule.Services;
using ZMAMedium.Application.OrdersModule.Interfaces;
using ZMAMedium.Application.OrdersModule.Services;
using ZMAMedium.Infrastructure.Persistence;
using ZMAMedium.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseInMemoryDatabase("ZMA-Catalog"));
builder.Services.AddDbContext<OrdersDbContext>(options =>
    options.UseInMemoryDatabase("ZMA-Orders"));

builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IOrderService, OrderService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();
app.Run();
