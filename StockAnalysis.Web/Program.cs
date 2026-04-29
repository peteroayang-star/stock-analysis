using Microsoft.Extensions.Options;
using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;
using StockAnalysis.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

var cfg = new AppConfig();
builder.Configuration.GetSection("Risk").Bind(cfg.Risk);
builder.Configuration.GetSection("Signal").Bind(cfg.Signal);
builder.Configuration.GetSection("Filter").Bind(cfg.Filter);
builder.Services.AddSingleton(cfg);
builder.Services.AddScoped<StockAnalyzer>(sp => new StockAnalyzer(sp.GetRequiredService<AppConfig>()));
builder.Services.AddScoped<DataImporter>();
builder.Services.AddScoped<Backtester>(sp => new Backtester(sp.GetRequiredService<AppConfig>().Signal));
builder.Services.AddScoped<LocalStockDataService>();
builder.Services.AddSingleton<StockDictionaryService>();
builder.Services.AddHttpClient<AkShareDataService>();
builder.Services.AddScoped<AkShareDataService>();
builder.Services.AddHttpClient<TencentRealTimeService>();
builder.Services.AddScoped<TencentRealTimeService>();
builder.Services.AddHttpClient<FinanceDataService>();
builder.Services.AddScoped<FinanceDataService>();
builder.Services.AddScoped<MarketDataService>();
builder.Services.AddSingleton<SignalLogService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
