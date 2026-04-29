"""
AKShare 行情数据服务
运行：pip install akshare flask && python akshare_server.py
接口：
  GET /stock/<code>       返回 CSV 格式行情数据
  GET /search/<name>      按名称查询股票代码，返回 JSON
"""
import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')

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

def normalize(s):
    return ''.join(chr(ord(c) - 0xFEE0) if 0xFF01 <= ord(c) <= 0xFF5E else c for c in s)

@app.route("/search/<name>")
def search_stock(name):
    try:
        stock_list = get_stock_list()
        name_norm = normalize(name)
        for k, v in stock_list.items():
            if normalize(k) == name_norm:
                return jsonify({"code": v, "name": k})
        # 模糊匹配
        for k, v in stock_list.items():
            if name_norm in normalize(k):
                return jsonify({"code": v, "name": k})
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

def format_period(s):
    s = str(s)
    if len(s) == 8:
        y, m = s[:4], s[4:6]
        q = {"03": "第一季度", "06": "第二季度", "09": "第三季度", "12": "第四季度"}.get(m)
        return f"{y}年{q}" if q else f"{y}年{m}月"
    return s

@app.route("/finance/<code>")
def get_finance(code):
    try:
        df = ak.stock_financial_abstract(symbol=code)
        date_cols = [c for c in df.columns if str(c).isdigit() or (isinstance(c, int))][:4]

        def find_row(name):
            matches = df[df['指标'] == name]
            return matches.iloc[0] if not matches.empty else None

        def get_vals(name):
            row = find_row(name)
            if row is None:
                return []
            vals = []
            for c in date_cols:
                try:
                    v = float(row[c])
                    vals.append(round(v, 4))
                except:
                    vals.append(None)
            return vals

        rows = {
            "revenue":      get_vals("营业总收入"),
            "net_profit":   get_vals("归母净利润"),
            "net_margin":   get_vals("销售净利率"),
            "revenue_yoy":  get_vals("营业总收入增长率"),
            "profit_yoy":   get_vals("归属母公司净利润增长率"),
            "periods":      [format_period(c) for c in date_cols]
        }
        return jsonify(rows)
    except Exception as e:
        return {"error": str(e)}, 500

if __name__ == "__main__":
    app.run(host="127.0.0.1", port=5100)

