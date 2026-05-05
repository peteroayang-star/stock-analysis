"""
AKShare 行情数据服务
运行：pip install akshare flask && python akshare_server.py
接口：
  GET /stock/<code>       返回 CSV 格式行情数据
  GET /search/<name>      按名称查询股票代码，返回 JSON
"""
import sys, io, os
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')

# 清除代理设置，避免 akshare 请求被代理拦截
for _k in ("HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy", "ALL_PROXY", "all_proxy"):
    os.environ.pop(_k, None)

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
        # 兼容浏览器直接发送未编码中文 URL 时 Werkzeug 用 latin-1 错误解码的情况
        try:
            name = name.encode('latin-1').decode('utf-8')
        except (UnicodeDecodeError, UnicodeEncodeError):
            pass
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

@app.route("/finance_debug/<code>")
def get_finance_debug(code):
    try:
        df = ak.stock_financial_abstract(symbol=code)
        return jsonify({"columns": [str(c) for c in df.columns.tolist()]})
    except Exception as e:
        return {"error": str(e)}, 500

def get_latest_quarter_date():
    """返回最近已结束季度的报告期，格式 20260331"""
    from datetime import date
    today = date.today()
    quarters = [(3,31),(6,30),(9,30),(12,31)]
    for month, day in reversed(quarters):
        if (today.month, today.day) > (month, day) or (today.month == month and today.day == day):
            return f"{today.year}{month:02d}{day:02d}"
        if today.month <= month:
            continue
    # 上一年Q4
    return f"{today.year-1}1231"

@app.route("/finance/<code>")
def get_finance(code):
    try:
        df = ak.stock_financial_abstract(symbol=code)
        date_cols = sorted([c for c in df.columns if str(c).isdigit() or isinstance(c, int)], reverse=True)[:4]

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

        periods = [format_period(c) for c in date_cols]

        # 检查最新季度是否缺失，若缺失则用 stock_yjbb_em 补充
        latest_q = get_latest_quarter_date()
        latest_q_label = format_period(latest_q)
        if periods and periods[0] != latest_q_label:
            try:
                em = ak.stock_yjbb_em(date=latest_q)
                row = em[em['股票代码'] == code]
                if not row.empty:
                    r = row.iloc[0]
                    revenue     = r.get('营业总收入-营业总收入', None)
                    revenue_yoy = r.get('营业总收入-同比增长', None)
                    profit      = r.get('净利润-净利润', None)
                    profit_yoy  = r.get('净利润-同比增长', None)

                    def safe(v):
                        try: return round(float(v), 4)
                        except: return None

                    # 插入最新期到列表头部，移除最旧一期
                    rows = {
                        "revenue":     [safe(revenue)]     + get_vals("营业总收入")[:3],
                        "net_profit":  [safe(profit)]      + get_vals("归母净利润")[:3],
                        "net_margin":  [None]              + get_vals("销售净利率")[:3],
                        "revenue_yoy": [safe(revenue_yoy)] + get_vals("营业总收入增长率")[:3],
                        "profit_yoy":  [safe(profit_yoy)]  + get_vals("归属母公司净利润增长率")[:3],
                        "periods":     [latest_q_label]    + periods[:3]
                    }
                    return jsonify(rows)
            except:
                pass

        rows = {
            "revenue":      get_vals("营业总收入"),
            "net_profit":   get_vals("归母净利润"),
            "net_margin":   get_vals("销售净利率"),
            "revenue_yoy":  get_vals("营业总收入增长率"),
            "profit_yoy":   get_vals("归属母公司净利润增长率"),
            "periods":      periods
        }
        return jsonify(rows)
    except Exception as e:
        return {"error": str(e)}, 500

@app.route("/sectors")
def get_sectors():
    try:
        df = ak.stock_board_concept_name_em()
        names = df["板块名称"].tolist()
        return jsonify({"sectors": names})
    except Exception as e:
        return {"error": str(e)}, 500

@app.route("/sector/<name>")
def get_sector_stocks(name):
    try:
        import requests
        # 用同花顺概念板块接口
        df = ak.stock_board_concept_cons_ths(symbol=name)
        stocks = df[["代码", "名称"]].rename(columns={"代码": "code", "名称": "name"}).to_dict(orient="records")
        return jsonify({"stocks": stocks})
    except Exception as e:
        return {"error": str(e)}, 500

if __name__ == "__main__":
    app.run(host="127.0.0.1", port=5100)

