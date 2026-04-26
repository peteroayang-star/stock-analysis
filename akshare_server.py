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
        df = ak.stock_zh_a_hist(symbol=code, period="daily", adjust="qfq")
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

if __name__ == "__main__":
    app.run(host="127.0.0.1", port=5100)

