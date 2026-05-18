using Microsoft.Extensions.Options;
using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;
using StockAnalysis.Web.Services;
using StockAnalysis.Web.Services.Providers;

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
builder.Services.AddSingleton<StockDictionaryService>();
builder.Services.AddHttpClient<AkShareDataService>().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseProxy = false });
builder.Services.AddScoped<AkShareDataService>();
builder.Services.AddHttpClient<TencentRealTimeService>().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseProxy = false });
builder.Services.AddScoped<TencentRealTimeService>();
builder.Services.AddHttpClient<FinanceDataService>().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseProxy = false });
builder.Services.AddScoped<FinanceDataService>();

// 统一数据源抽象层 (优先级: LocalCSV > TencentKline > AKShare > Tencent实时 > Sina兜底)
builder.Services.AddScoped<IMarketDataProvider, LocalCsvProvider>();
builder.Services.AddHttpClient<TencentKlineProvider>().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseProxy = false });
builder.Services.AddScoped<IMarketDataProvider, TencentKlineProvider>();
builder.Services.AddScoped<IMarketDataProvider, AkShareProvider>();
builder.Services.AddScoped<IMarketDataProvider, TencentRealtimeProvider>();
builder.Services.AddHttpClient<SinaFallbackProvider>().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseProxy = false });
builder.Services.AddScoped<IMarketDataProvider, SinaFallbackProvider>();
builder.Services.AddScoped<ProviderRouter>();

builder.Services.AddScoped<RiskStockTagEngine>();
builder.Services.AddScoped<MarketDataService>();
builder.Services.AddScoped<MarketIndexService>();
builder.Services.AddSingleton<SignalLogService>();
builder.Services.AddScoped<DataSourceFallbackService>();
builder.Services.AddScoped<DailyWatchPoolService>();
builder.Services.AddSingleton<AiAnalysisCacheService>();
builder.Services.AddHttpClient<SparkAiService>().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseProxy = false });

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
