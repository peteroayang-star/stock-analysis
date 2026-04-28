"""
AKShare 行情数据服务
运行：pip install akshare flask && python akshare_server.py
接口：
  GET /stock/<code>       返回 CSV 格式行情数据
  GET /search/<name>      按名称查询股票代码，返回 JSON
"""
from flask import Flask, Response, jsonify
import akshare as ak
import pandas as pd

app = Flask(__name__)

# 启动时加载股票列表缓存
_stock_list = None

def get_stock_list():
    global _stock_list
    if _stock_list is None:
        df = ak.stock_info_a_code_name()
        _stock_list = df.set_index("name")["code"].to_dict()
    return _stock_list

@app.route("/stock/<code>")
def get_stock(code):
    try:
        prefix = "sh" if code.startswith("6") else "sz"
        df = ak.stock_zh_a_daily(symbol=f"{prefix}{code}", adjust="qfq")
        df = df.rename(columns={"date": "日期", "open": "开盘", "high": "最高", "low": "最低", "close": "收盘", "volume": "成交量", "amount": "成交额"})
        df = df[["日期", "开盘", "最高", "最低", "收盘", "成交量", "成交额"]]
        return Response(df.to_csv(index=False), mimetype="text/csv")
    except Exception as e:
        return {"error": str(e)}, 500

@app.route("/search/<name>")
def search_stock(name):
    try:
        stock_list = get_stock_list()
        code = stock_list.get(name)
        if code:
            return jsonify({"code": code, "name": name})
        return {"error": "not found"}, 404
    except Exception as e:
        return {"error": str(e)}, 500

@app.route("/realtime/<code>")
def get_realtime(code):
    try:
        prefix = "sh" if code.startswith("6") else "sz"
        df = ak.stock_zh_a_spot()
        row = df[df["code"] == f"{prefix}{code}"]
        if row.empty:
            return {"error": "not found"}, 404
        r = row.iloc[0]
        return jsonify({
            "price": float(r["trade"]),
            "open": float(r["open"]),
            "high": float(r["high"]),
            "low": float(r["low"]),
            "volume": float(r["volume"]),
            "amount": float(r["amount"]),
            "change_pct": float(r["percent"])
        })
    except Exception as e:
        return {"error": str(e)}, 500

@app.route("/minute/<code>")
def get_minute(code):
    try:
        df = ak.stock_zh_a_minute(symbol=code, period="1", adjust="qfq")
        df = df[["day", "open", "high", "low", "close", "volume"]]
        return Response(df.to_csv(index=False), mimetype="text/csv")
    except Exception as e:
        return {"error": str(e)}, 500

@app.route("/name/<code>")
def get_name(code):
    try:
        stock_list = get_stock_list()
        name = next((n for n, c in stock_list.items() if c == code), None)
        if name:
            return jsonify({"code": code, "name": name})
        return {"error": "not found"}, 404
    except Exception as e:
        return {"error": str(e)}, 500

@app.route("/finance/<code>")
def get_finance(code):
    try:
        df = ak.stock_financial_abstract(symbol=code)
        # 取最近4期日期列
        date_cols = [c for c in df.columns if str(c).isdigit()][:4]
        # 提取关键行：营业收入(1)、净利润(3)、净利率(14)、营收增长率(51)、净利润增长率(52)
        rows = {
            "revenue":      list(df.iloc[1][date_cols].astype(float)),
            "net_profit":   list(df.iloc[3][date_cols].astype(float)),
            "net_margin":   list(df.iloc[14][date_cols].astype(float)),
            "revenue_yoy":  list(df.iloc[51][date_cols].astype(float)),
            "profit_yoy":   list(df.iloc[52][date_cols].astype(float)),
            "periods":      list(date_cols)
        }
        return jsonify(rows)
    except Exception as e:
        return {"error": str(e)}, 500

if __name__ == "__main__":
    app.run(host="127.0.0.1", port=5100)

