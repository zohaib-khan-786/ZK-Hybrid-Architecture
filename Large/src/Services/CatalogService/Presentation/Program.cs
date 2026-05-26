using CatalogService.Application.Interfaces;
using CatalogService.Application.Services;
using CatalogService.Infrastructure.Persistence;
using CatalogService.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers();

builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseInMemoryDatabase("ZMA-Catalog"));

builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<ProductService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
