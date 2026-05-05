# 通达信 + 风险评分系统 3.0

A 股技术分析平台，集信号检测、风险评分、AI 辅助分析与持仓管理于一体。

## 架构概览

```
┌─────────────────────────────┐     HTTP      ┌──────────────────────┐
│  StockAnalysis.Web          │ ◄──────────── │  akshare_server.py   │
│  ASP.NET Core 8 MVC (C#)    │               │  Flask + AKShare     │
│  port 5000/5001             │               │  port 5100           │
└─────────────────────────────┘               └──────────────────────┘
         │
         │ 本地 CSV
         ▼
    /Data/  (通达信导出数据)
```

双栈设计：C# 负责计算密集型分析，Python 负责数据采集，通过 HTTP 解耦。

## 核心功能

| 功能 | 说明 |
|------|------|
| **风险评分** | 每只股票 0–100 分，驱动所有 UI 决策（颜色、排序、提醒） |
| **信号检测** | 突破、量能放大、均线回踩、洗盘四类买入信号 |
| **龙头筛选器** | 按流动性、上市天数、信号强度过滤机会 |
| **回测** | 在历史 K 线上验证策略表现 |
| **持仓管理** | 跟踪成本、浮盈浮亏 |
| **AI 分析** | 接入讯飞星火大模型，自然语言问答 |

## 快速启动

**第一步：启动 Python 数据服务**

```bash
pip install akshare flask pandas
python akshare_server.py
# 运行在 http://127.0.0.1:5100
```

**第二步：启动 Web 应用**

```bash
cd StockAnalysis.Web
dotnet run
# 运行在 http://localhost:5000
```

> 也可直接运行根目录的 `启动服务.bat`

## 配置说明

`StockAnalysis.Web/appsettings.json`：

```json
"Risk":   { "BuyMaxScore": 30, "WatchMaxScore": 50, "SellScore": 65 },
"Signal": { "BreakoutDays": 20, "VolumeMultiplier": 1.8, "PullbackNearMARatio": 0.02 },
"Filter": { "MinAmountMillionYuan": 5.0, "MinListedDays": 60 },
"Spark":  { "ApiKey": "YOUR_SPARK_API_KEY", "ApiSecret": "YOUR_SPARK_API_SECRET", "Model": "generalv3.5" }
```

风险评分阈值、信号参数、过滤条件均可在此调整，无需重新编译。

## 本地数据

将通达信导出的 CSV 文件放入 `/Data` 目录，应用启动时自动读取用于历史分析。

## 页面路由

| 路由 | 页面 |
|------|------|
| `/` | 仪表盘 |
| `/Stock` | 个股详情（信号 + K 线） |
| `/Screener` | 龙头筛选器 |
| `/Opportunity` | 机会排行榜 |
| `/Portfolio` | 持仓管理 |
| `/Backtest` | 策略回测 |
| `/Ai` | AI 股票问答 |

## AKShare 数据接口

| 接口 | 说明 |
|------|------|
| `GET /stock/<code>` | 日线 OHLCV（CSV） |
| `GET /realtime/<code>` | 实时行情 |
| `GET /finance/<code>` | 季度财务数据 |
| `GET /search/<name>` | 股票名称 → 代码 |
| `GET /sectors` | 概念板块列表 |
| `GET /sector/<name>` | 板块成分股 |

## 技术栈

- **后端**：ASP.NET Core 8 / C#
- **数据服务**：Python 3 / Flask / AKShare
- **前端**：Razor Views / Chart.js
- **AI**：讯飞星火 API (generalv3.5)
