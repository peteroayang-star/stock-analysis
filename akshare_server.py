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

import requests
# 禁止 requests 读取系统代理（Windows WinInet 代理）
_orig_session_init = requests.Session.__init__
def _no_proxy_session_init(self, *args, **kwargs):
    _orig_session_init(self, *args, **kwargs)
    self.trust_env = False
requests.Session.__init__ = _no_proxy_session_init


from flask import Flask, Response, jsonify
import akshare as ak
import pandas as pd

app = Flask(__name__)
app.json.ensure_ascii = False

# 启动时加载股票列表缓存
_stock_list = None
_hk_stock_list = None

def _fix_gbk(s):
    """修复 akshare 深交所接口返回的 GBK 乱码名称（仅当原始字符串不含中文时才尝试修复）"""
    if any('\u4e00' <= c <= '\u9fff' for c in s):
        return s  # 已经是正确中文，不需要修复
    try:
        fixed = s.encode('raw_unicode_escape').decode('gbk')
        if any('\u4e00' <= c <= '\u9fff' for c in fixed):
            return fixed
    except Exception:
        pass
    return s

def get_stock_list():
    global _stock_list
    if _stock_list is None:
        try:
            df = ak.stock_info_a_code_name()
        except Exception:
            # 北交所 SSL 偶发失败时，降级为只加载沪深
            sh = ak.stock_info_sh_name_code(symbol="主板A股")[["证券代码", "证券简称"]].rename(columns={"证券代码": "code", "证券简称": "name"})
            kcb = ak.stock_info_sh_name_code(symbol="科创板")[["证券代码", "证券简称"]].rename(columns={"证券代码": "code", "证券简称": "name"})
            sz = ak.stock_info_sz_name_code(symbol="A股列表")[["A股代码", "A股简称"]].rename(columns={"A股代码": "code", "A股简称": "name"})
            sz["code"] = sz["code"].astype(str).str.zfill(6)
            df = pd.concat([sh, kcb, sz], ignore_index=True)
            df["name"] = df["name"].apply(_fix_gbk)
        _stock_list = df.set_index("name")["code"].to_dict()
    return _stock_list

def get_hk_stock_list():
    global _hk_stock_list
    if _hk_stock_list is None:
        df = ak.stock_hk_spot_em()[["代码", "名称"]]
        df["名称"] = df["名称"].apply(_fix_gbk)
        _hk_stock_list = df.set_index("名称")["代码"].to_dict()
    return _hk_stock_list

def is_hk_code(code):
    return len(code) == 5 and code.isdigit()

def _fetch_kline_tencent(code):
    """用腾讯 K 线接口获取前复权日 K（约320条），返回 DataFrame"""
    import re as _re, json as _json
    prefix = "sh" if code.startswith("6") else "sz"
    symbol = f"{prefix}{code}"
    url = (f"http://web.ifzq.gtimg.cn/appstock/app/fqkline/get"
           f"?_var=kline_dayqfq&param={symbol},day,,,640,qfq")
    s = requests.Session(); s.trust_env = False
    raw = s.get(url, timeout=8).content.decode("utf-8", errors="replace")
    m = _re.match(r'\w+=(.+)', raw.strip())
    if not m:
        raise ValueError("parse error")
    obj = _json.loads(m.group(1))
    bars = obj["data"][symbol].get("qfqday") or obj["data"][symbol].get("day")
    rows = []
    for b in bars:
        rows.append({"日期": b[0], "开盘": float(b[1]), "最高": float(b[3]),
                     "最低": float(b[4]), "收盘": float(b[2]), "成交量": int(float(b[5])), "成交额": 0})
    return pd.DataFrame(rows)

@app.route("/stock/<code>")
def get_stock(code):
    try:
        if is_hk_code(code):
            df = ak.stock_hk_hist(symbol=code, period="daily", adjust="qfq")
            cols = [c for c in df.columns if c in ["日期", "开盘", "最高", "最低", "收盘", "成交量", "成交额"]]
            df = df[cols]
            for c in ["成交量", "成交额"]:
                if c in df.columns:
                    df[c] = df[c].fillna(0).astype("int64")
            return Response(df.to_csv(index=False), mimetype="text/csv")
        df = _fetch_kline_tencent(code)
        return Response(df.to_csv(index=False), mimetype="text/csv")
    except Exception as e:
        return {"error": str(e)}, 500

def normalize(s):
    return ''.join(chr(ord(c) - 0xFEE0) if 0xFF01 <= ord(c) <= 0xFF5E else c for c in s)

@app.route("/search/<path:name>")
def search_stock(name):
    try:
        # 仅当原始字符串不含中文（说明是 latin-1 误解码的乱码）时才尝试修复
        if not any('\u4e00' <= c <= '\u9fff' for c in name):
            try:
                fixed = name.encode('latin-1').decode('utf-8')
                if any('\u4e00' <= c <= '\u9fff' for c in fixed):
                    name = fixed
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
        # 港股匹配（接口失败时跳过，不影响A股查询）
        try:
            hk_list = get_hk_stock_list()
            for k, v in hk_list.items():
                if normalize(k) == name_norm:
                    return jsonify({"code": v, "name": k, "market": "hk"})
            for k, v in hk_list.items():
                if name_norm in normalize(k):
                    return jsonify({"code": v, "name": k, "market": "hk"})
        except Exception:
            pass
        return {"error": "not found"}, 404
    except Exception as e:
        return {"error": str(e)}, 500

@app.route("/realtime/<code>")
def get_realtime(code):
    try:
        prefix = "sh" if code.startswith("6") else "sz"
        raw = requests.get(f"https://qt.gtimg.cn/q={prefix}{code}", timeout=5).content
        text = raw.decode("gbk", errors="replace")
        import re
        m = re.match(r'v_\w+="(.*)"', text.strip())
        if not m:
            return {"error": "not found"}, 404
        f = m.group(1).split("~")
        if len(f) < 39:
            return {"error": "parse error"}, 500
        pre_close = float(f[4]) if f[4] else 0
        price = float(f[3]) if f[3] else 0
        change_pct = round((price - pre_close) / pre_close * 100, 2) if pre_close else 0
        return jsonify({
            "price": price, "open": float(f[5] or 0),
            "high": float(f[33] or 0), "low": float(f[34] or 0),
            "volume": float(f[6] or 0), "amount": float(f[37] or 0) * 10000,
            "change_pct": change_pct
        })
    except Exception as e:
        return {"error": str(e)}, 500

@app.route("/minute/<code>")
def get_minute(code):
    try:
        import re as _re, json as _json
        prefix = "sh" if code.startswith("6") else "sz"
        symbol = f"{prefix}{code}"
        s = requests.Session(); s.trust_env = False
        url = f"http://web.ifzq.gtimg.cn/appstock/app/minute/query?_var=min_data_{symbol}&code={symbol}"
        raw = s.get(url, timeout=8).content.decode("utf-8", errors="replace")
        m = _re.match(r'\w+=(.+)', raw.strip())
        if not m:
            return {"error": "parse error"}, 500
        obj = _json.loads(m.group(1))
        bars = obj["data"][symbol]["data"]["data"]
        rows = []
        for b in bars:
            parts = b.split()
            if len(parts) >= 2:
                rows.append({"day": parts[0], "open": float(parts[1]), "high": float(parts[1]),
                             "low": float(parts[1]), "close": float(parts[1]),
                             "volume": int(parts[2]) if len(parts) > 2 else 0})
        df = pd.DataFrame(rows)
        return Response(df.to_csv(index=False), mimetype="text/csv")
    except Exception as e:
        return {"error": str(e)}, 500

# 名称查找缓存
_name_cache = {}

def _get_stock_name(code):
    if code in _name_cache:
        return _name_cache[code]
    stock_list = get_stock_list()
    name = next((n for n, c in stock_list.items() if c == code), None)
    _name_cache[code] = name
    # 最多缓存 5000 条
    if len(_name_cache) > 5000:
        _name_cache.clear()
    return name

@app.route("/name/<code>")
def get_name(code):
    try:
        name = _get_stock_name(code)
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

def get_latest_quarter_date():
    """返回最近已结束季度的报告期，格式 20260331"""
    from datetime import date
    today = date.today()
    quarters = [(3,31),(6,30),(9,30),(12,31)]
    for month, day in reversed(quarters):
        if (today.month, today.day) >= (month, day):
            return f"{today.year}{month:02d}{day:02d}"
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

@app.route("/marketcap/<code>")
def get_market_cap(code):
    try:
        prefix = "sh" if code.startswith("6") else "sz"
        s = requests.Session(); s.trust_env = False
        raw = s.get(f"https://qt.gtimg.cn/q={prefix}{code}", timeout=5).content
        text = raw.decode("gbk", errors="replace")
        import re
        m = re.match(r'v_\w+="(.*)"', text.strip())
        if not m:
            return {"error": "not found"}, 404
        f = m.group(1).split("~")
        name = f[1] if len(f) > 1 else ""
        circ_mv  = float(f[44]) * 1e8 if len(f) > 44 and f[44] else 0
        total_mv = float(f[45]) * 1e8 if len(f) > 45 and f[45] else 0
        return jsonify({"total_mv": total_mv, "circ_mv": circ_mv, "name": name})
    except Exception as e:
        return {"error": str(e)}, 500

@app.route("/allstocks")
def get_all_stocks():
    try:
        stock_list = get_stock_list()
        stocks = [{"code": v, "name": k} for k, v in stock_list.items()]
        return jsonify({"stocks": stocks})
    except Exception as e:
        return {"error": str(e)}, 500

def _is_common_a_stock(code):
    """判断是否为普通A股（沪深主板），排除ETF/基金/债券/指数"""
    if len(code) != 6 or not code.isdigit():
        return False
    # 沪市主板
    if code.startswith(('600', '601', '603', '605')):
        return True
    # 深市主板
    if code.startswith(('000', '001', '002', '003')):
        return True
    # 排除创业板、科创板、北交所、ETF、债券、指数等
    return False

@app.route("/snapshot")
def get_snapshot():
    """用腾讯批量行情接口获取全市场快照，每批100只，并发请求"""
    import concurrent.futures, re
    from requests import Session

    stock_list = get_stock_list()
    # 只取沪深主板A股（排除创业板/科创板/北交所/ETF/基金/债券）
    codes = [c for c in stock_list.values()
             if _is_common_a_stock(c)]

    def prefix(code):
        return 'sh' if code.startswith('6') else 'sz'

    def fetch_batch(batch):
        symbols = ','.join(f"{prefix(c)}{c}" for c in batch)
        try:
            s = Session(); s.trust_env = False
            raw = s.get(f'https://qt.gtimg.cn/q={symbols}', timeout=8).content
            text = raw.decode('gbk', errors='replace')
            results = []
            for line in text.strip().split('\n'):
                m = re.match(r'v_\w+="(.*)"', line)
                if not m: continue
                f = m.group(1).split('~')
                if len(f) < 39 or not f[2]: continue
                try:
                    results.append({
                        'code': f[2], 'name': f[1],
                        'price': float(f[3]) if f[3] else 0,
                        'change_pct': float(f[32]) if f[32] else 0,
                        'amount': float(f[37]) * 10000 if f[37] else 0,
                        'total_mv': 0,
                        'turnover': float(f[38]) if f[38] else 0
                    })
                except: continue
            return results
        except: return []

    batches = [codes[i:i+100] for i in range(0, len(codes), 100)]
    all_stocks = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=10) as ex:
        for result in ex.map(fetch_batch, batches):
            all_stocks.extend(result)

    return jsonify({"stocks": all_stocks, "source": "tencent"})

@app.route("/sectors")
def get_sectors():
    try:
        df = ak.stock_board_concept_name_ths()
        names = df["name"].tolist()
        return jsonify({"sectors": names})
    except Exception as e:
        return {"error": str(e)}, 500

@app.route("/sector/<name>")
def get_sector_stocks(name):
    try:
        df = ak.stock_board_concept_cons_ths(symbol=name)
        stocks = df[["代码", "名称"]].rename(columns={"代码": "code", "名称": "name"}).to_dict(orient="records")
        return jsonify({"stocks": stocks})
    except Exception as e:
        return {"error": str(e)}, 500

if __name__ == "__main__":
    app.run(host="127.0.0.1", port=5100)

