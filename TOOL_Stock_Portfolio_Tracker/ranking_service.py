import os
import sys
import json
import time
import datetime
import ssl
import urllib.request
from typing import Dict, List, Any, Optional, Tuple

def get_cache_file_path() -> str:
    """精確解析 market_ranking_cache.json 的持久化儲存路徑 (永久對齊應用程式根目錄)"""
    if getattr(sys, 'frozen', False):
        base_dir = os.path.dirname(os.path.abspath(sys.executable))
    else:
        base_dir = os.path.dirname(os.path.abspath(__file__))
        if os.path.basename(base_dir) == "modules":
            base_dir = os.path.dirname(base_dir)
    return os.path.join(base_dir, "market_ranking_cache.json")

CACHE_FILE = get_cache_file_path()
CACHE_EXPIRY_HOURS = 12

# 主流熱門台股 ETF 清單（基準年化殖利率、分類與配息特性）
POPULAR_ETFS = [
    {"symbol": "0050", "name": "元大台灣50", "category": "市值型", "yield_pct": 3.25, "freq": "半年配"},
    {"symbol": "0056", "name": "元大高股息", "category": "高股息", "yield_pct": 8.85, "freq": "季配"},
    {"symbol": "00878", "name": "國泰永續高股息", "category": "高股息", "yield_pct": 8.20, "freq": "季配"},
    {"symbol": "00919", "name": "群益台灣精選高息", "category": "高股息", "yield_pct": 10.55, "freq": "季配"},
    {"symbol": "00929", "name": "復華台灣科技優息", "category": "高股息", "yield_pct": 9.20, "freq": "月配"},
    {"symbol": "00940", "name": "元大台灣價值高息", "category": "高股息", "yield_pct": 6.80, "freq": "月配"},
    {"symbol": "00713", "name": "元大台灣高息低波", "category": "高股息", "yield_pct": 7.50, "freq": "季配"},
    {"symbol": "006208", "name": "富邦台50", "category": "市值型", "yield_pct": 3.15, "freq": "半年配"},
    {"symbol": "00918", "name": "大華優利高填息30", "category": "高股息", "yield_pct": 10.10, "freq": "季配"},
    {"symbol": "00915", "name": "凱基優選高股息30", "category": "高股息", "yield_pct": 9.80, "freq": "季配"},
    {"symbol": "00934", "name": "中信成長高股息", "category": "高股息", "yield_pct": 7.80, "freq": "月配"},
    {"symbol": "00936", "name": "台新永續高息中小", "category": "高股息", "yield_pct": 7.20, "freq": "月配"},
    {"symbol": "00881", "name": "國泰台灣5G+", "category": "科技型", "yield_pct": 5.80, "freq": "半年配"},
    {"symbol": "00757", "name": "統一FANG+", "category": "海外科技", "yield_pct": 0.00, "freq": "不配息"},
    {"symbol": "00692", "name": "富邦公司治理", "category": "ESG型", "yield_pct": 4.50, "freq": "半年配"},
    {"symbol": "00850", "name": "元大臺灣ESG永續", "category": "ESG型", "yield_pct": 4.30, "freq": "季配"},
    {"symbol": "00939", "name": "統一台灣高息動能", "category": "高股息", "yield_pct": 6.50, "freq": "月配"},
    {"symbol": "00900", "name": "富邦特選高股息30", "category": "高股息", "yield_pct": 7.90, "freq": "季配"},
    {"symbol": "00891", "name": "中信關鍵半導體", "category": "科技型", "yield_pct": 4.80, "freq": "季配"},
    {"symbol": "00892", "name": "富邦台灣半導體", "category": "科技型", "yield_pct": 4.20, "freq": "半年配"}
]

class MarketRankingService:
    _cached_data: Optional[Dict[str, Any]] = None
    _last_load_time: float = 0.0

    @classmethod
    def get_cache_file_path(cls) -> str:
        return get_cache_file_path()

    @classmethod
    def is_cache_stale(cls) -> bool:
        cache_path = cls.get_cache_file_path()
        if not os.path.exists(cache_path):
            alt_path = os.path.join(os.path.dirname(os.path.dirname(cache_path)), "market_ranking_cache.json")
            if os.path.exists(alt_path):
                cache_path = alt_path
            else:
                return True
        if cls._cached_data:
            cached_time = cls._cached_data.get("cached_at", 0)
        else:
            try:
                with open(cache_path, "r", encoding="utf-8") as f:
                    d = json.load(f)
                    cached_time = d.get("cached_at", 0)
            except Exception:
                return True
        return (time.time() - cached_time) > (CACHE_EXPIRY_HOURS * 3600)

    @classmethod
    def _get_ssl_context(cls):
        ctx = ssl.create_default_context()
        ctx.check_hostname = False
        ctx.verify_mode = ssl.CERT_NONE
        return ctx

    @classmethod
    def load_cache(cls, allow_stale: bool = True) -> Optional[Dict[str, Any]]:
        """從本地檔案載入快取 (支援 Stale-While-Revalidate 秒開優先原則)"""
        if cls._cached_data:
            return cls._cached_data

        cache_path = cls.get_cache_file_path()
        if not os.path.exists(cache_path):
            alt_path = os.path.join(os.path.dirname(os.path.dirname(cache_path)), "market_ranking_cache.json")
            if os.path.exists(alt_path):
                cache_path = alt_path
            else:
                return None

        try:
            with open(cache_path, "r", encoding="utf-8") as f:
                data = json.load(f)
                cached_time = data.get("cached_at", 0)
                # 只要檔案含有資料，在 allow_stale=True 時一律秒顯，不因過期拒絕回傳！
                if allow_stale or (time.time() - cached_time < CACHE_EXPIRY_HOURS * 3600):
                    cls._cached_data = data
                    cls._last_load_time = cached_time
                    return data
        except Exception as e:
            print(f"[MarketRankingService] 讀取快取失敗: {e}")
        return None

    @classmethod
    def save_cache(cls, data: Dict[str, Any]):
        """將市場數據持久化寫入本地快取"""
        cache_path = cls.get_cache_file_path()
        try:
            data["cached_at"] = time.time()
            data["cached_time_str"] = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
            with open(cache_path, "w", encoding="utf-8") as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
            cls._cached_data = data
            cls._last_load_time = data["cached_at"]
        except Exception as e:
            print(f"[MarketRankingService] 儲存快取失敗: {e}")

    @classmethod
    def fetch_twse_bwibbu(cls) -> List[Dict[str, Any]]:
        """抓取台灣證交所上市公司每日本益比、殖利率、股價淨值比與推導 ROE"""
        stocks = []
        try:
            ctx = cls._get_ssl_context()
            req = urllib.request.Request(
                'https://www.twse.com.tw/rwd/zh/afterTrading/BWIBBU_d?response=json',
                headers={'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'}
            )
            with urllib.request.urlopen(req, context=ctx, timeout=12) as resp:
                raw_json = json.loads(resp.read().decode('utf-8'))
                raw_data = raw_json.get('data', [])

                for r in raw_data:
                    if len(r) < 8:
                        continue
                    sym = str(r[0]).strip()
                    name = str(r[1]).strip()

                    try:
                        close_p = float(str(r[2]).replace(',', '').strip())
                    except Exception:
                        close_p = 0.0

                    try:
                        yield_pct = float(str(r[3]).replace(',', '').strip())
                    except Exception:
                        yield_pct = 0.0

                    pe_str = str(r[5]).replace(',', '').strip()
                    try:
                        pe = float(pe_str)
                    except Exception:
                        pe = None

                    pb_str = str(r[6]).replace(',', '').strip()
                    try:
                        pb = float(pb_str)
                    except Exception:
                        pb = None

                    # 金融學杜邦公式推導：ROE = (PB / PE) * 100%
                    roe = None
                    if pe is not None and pb is not None and pe > 0:
                        roe = round((pb / pe) * 100.0, 2)

                    div_year = str(r[4]).strip()
                    season = str(r[7]).strip()

                    stocks.append({
                        "symbol": sym,
                        "name": name,
                        "price": close_p,
                        "yield_pct": yield_pct,
                        "pe": pe,
                        "pb": pb,
                        "roe": roe,
                        "div_year": div_year,
                        "season": season,
                        "is_etf": False,
                        "type": "個股"
                    })
        except Exception as e:
            print(f"[MarketRankingService] 抓取 BWIBBU_d 失敗: {e}")
        return stocks

    @classmethod
    def fetch_twse_revenue_yoy(cls) -> List[Dict[str, Any]]:
        """抓取台灣證交所上市公司最新月份營業收入與年增率 (YoY)"""
        rev_list = []
        try:
            ctx = cls._get_ssl_context()
            req = urllib.request.Request(
                'https://openapi.twse.com.tw/v1/opendata/t187ap05_L',
                headers={'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'}
            )
            with urllib.request.urlopen(req, context=ctx, timeout=12) as resp:
                data = json.loads(resp.read().decode('utf-8'))
                for r in data:
                    sym = str(r.get('公司代號', '')).strip()
                    name = str(r.get('公司名稱', '')).strip()
                    industry = str(r.get('產業別', '')).strip()
                    month_str = str(r.get('資料年月', '')).strip()
                    yoy_str = str(r.get('營業收入-去年同月增減(%)', '')).strip()
                    cum_yoy_str = str(r.get('累計營業收入-前期比較增減(%)', '')).strip()

                    try:
                        yoy = float(yoy_str)
                    except Exception:
                        continue

                    try:
                        cum_yoy = float(cum_yoy_str)
                    except Exception:
                        cum_yoy = None

                    try:
                        # 原始為千元，轉為億元呈現較精簡好讀
                        rev_cur_e = float(str(r.get('營業收入-當月營收', 0)).replace(',', '')) / 100000.0
                    except Exception:
                        rev_cur_e = 0.0

                    try:
                        rev_last_e = float(str(r.get('營業收入-去年當月營收', 0)).replace(',', '')) / 100000.0
                    except Exception:
                        rev_last_e = 0.0

                    rev_list.append({
                        "symbol": sym,
                        "name": name,
                        "industry": industry,
                        "month": month_str,
                        "yoy": round(yoy, 2),
                        "cum_yoy": round(cum_yoy, 2) if cum_yoy is not None else None,
                        "rev_cur_e": round(rev_cur_e, 2),
                        "rev_last_e": round(rev_last_e, 2),
                        "is_etf": False,
                        "type": "個股"
                    })
        except Exception as e:
            print(f"[MarketRankingService] 抓取 t187ap05_L 營收年增率失敗: {e}")
        return rev_list

    @classmethod
    def get_market_data(cls, force_refresh: bool = False) -> Dict[str, Any]:
        """取得完整市場資料（支援記憶體快取、本地磁碟快取與遠端同步）"""
        if not force_refresh:
            cached = cls.load_cache()
            if cached and cached.get("stocks") and cached.get("revenues"):
                return cached

        # 抓取證交所兩大權威端點
        stocks = cls.fetch_twse_bwibbu()
        revenues = cls.fetch_twse_revenue_yoy()

        # 若證交所連線失敗但本地曾有舊快取，則優雅降級使用舊快取
        if not stocks and not revenues:
            fallback = cls.load_cache()
            if fallback:
                return fallback

        # 整合熱門 ETF 資訊
        etfs = []
        for e in POPULAR_ETFS:
            etfs.append({
                "symbol": e["symbol"],
                "name": e["name"],
                "price": 0.0,
                "yield_pct": e["yield_pct"],
                "pe": None,
                "pb": None,
                "roe": None,
                "category": e["category"],
                "freq": e["freq"],
                "is_etf": True,
                "type": f"ETF ({e['category']})"
            })

        payload = {
            "stocks": stocks,
            "revenues": revenues,
            "etfs": etfs,
            "total_stocks_count": len(stocks),
            "total_revenues_count": len(revenues),
            "total_etfs_count": len(etfs)
        }

        cls.save_cache(payload)
        return payload

    @classmethod
    def get_dividend_ranking(cls, filter_type: str = "all", limit: int = 50) -> List[Dict[str, Any]]:
        """
        1. 殖利率排名 (Yield Ranking)
        - filter_type: 'all' (個股+ETF), 'stock' (僅個股), 'etf' (僅ETF)
        """
        data = cls.get_market_data()
        pool = []

        if filter_type in ("all", "stock"):
            for s in data.get("stocks", []):
                if s.get("yield_pct", 0) > 0:
                    item = dict(s)
                    item["highlight_label"] = f"{item['yield_pct']:.2f}%"
                    item["sub_info"] = f"PE: {item['pe'] if item['pe'] else '-'} | PB: {item['pb'] if item['pb'] else '-'}"
                    pool.append(item)

        if filter_type in ("all", "etf"):
            for e in data.get("etfs", []):
                if e.get("yield_pct", 0) > 0:
                    item = dict(e)
                    item["highlight_label"] = f"{item['yield_pct']:.2f}%"
                    item["sub_info"] = f"{item['freq']} | 類別: {item['category']}"
                    pool.append(item)

        # 依照殖利率降序排序
        ranked = sorted(pool, key=lambda x: x.get("yield_pct", 0), reverse=True)
        return ranked[:limit]

    @classmethod
    def get_pe_ranking(cls, min_pe: float = 4.0, max_pe: float = 30.0, min_price: float = 10.0, limit: int = 50) -> List[Dict[str, Any]]:
        """
        2. PE (本益比) 排名 - 價值投資低本益比優選
        - 排除虧損 (PE <= 0) 與異常暴賺 (PE < 4.0，常為處分土地或一次性收益)
        - 篩選股價 >= 10.0 元之優質個股，由低到高排序
        """
        data = cls.get_market_data()
        pool = []

        for s in data.get("stocks", []):
            pe = s.get("pe")
            price = s.get("price", 0.0)
            if pe is not None and min_pe <= pe <= max_pe and price >= min_price:
                item = dict(s)
                item["highlight_label"] = f"{pe:.2f} 倍"
                roe_str = f"{item['roe']:.1f}%" if item.get('roe') is not None else "-"
                yield_str = f"{item['yield_pct']:.2f}%" if item.get('yield_pct', 0) > 0 else "-"
                item["sub_info"] = f"現價: {price} | 殖利率: {yield_str} | ROE: {roe_str}"
                pool.append(item)

        ranked = sorted(pool, key=lambda x: x["pe"])
        return ranked[:limit]

    @classmethod
    def get_revenue_growth_ranking(cls, limit: int = 50) -> List[Dict[str, Any]]:
        """
        3. 營收成長排名 (Revenue YoY Ranking)
        - 依上市公司營業收入去年同月增減% (YoY) 降序排列
        """
        data = cls.get_market_data()
        pool = []

        for r in data.get("revenues", []):
            yoy = r.get("yoy")
            if yoy is not None and yoy > 0:
                item = dict(r)
                item["highlight_label"] = f"+{yoy:.2f}%"
                month_display = f"民國{item['month'][:3]}年{item['month'][3:]}月" if len(item.get('month', '')) == 5 else item.get('month', '')
                cum_str = f"累計年增: +{item['cum_yoy']:.1f}%" if item.get('cum_yoy') is not None else ""
                item["sub_info"] = f"{month_display}營收: {item['rev_cur_e']}億 | {item['industry']} | {cum_str}"
                pool.append(item)

        ranked = sorted(pool, key=lambda x: x["yoy"], reverse=True)
        return ranked[:limit]

    @classmethod
    def get_roe_ranking(cls, min_pe: float = 4.0, min_roe: float = 8.0, max_roe: float = 75.0, limit: int = 50) -> List[Dict[str, Any]]:
        """
        4. ROE (股東權益報酬率) 排名 - 高資本報酬率績優股
        - 杜邦恆等式 ROE = (PB / PE) * 100%
        - 排除 PE < 4.0 與極端 ROE > 75% 之資產處分膨脹值
        - 依照 ROE 由高到低降序排列
        """
        data = cls.get_market_data()
        pool = []

        for s in data.get("stocks", []):
            roe = s.get("roe")
            pe = s.get("pe")
            price = s.get("price", 0.0)
            if roe is not None and pe is not None and pe >= min_pe and min_roe <= roe <= max_roe:
                item = dict(s)
                item["highlight_label"] = f"{roe:.2f}%"
                yield_str = f"{item['yield_pct']:.2f}%" if item.get('yield_pct', 0) > 0 else "-"
                item["sub_info"] = f"PE: {pe:.1f} | PB: {item['pb']:.2f} | 殖利率: {yield_str} | 現價: {price}"
                pool.append(item)

        ranked = sorted(pool, key=lambda x: x["roe"], reverse=True)
        return ranked[:limit]
