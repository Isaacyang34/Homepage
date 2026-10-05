/**
 * Cloudflare Worker: Taiwan ETF & Stock CORS Proxy
 * 
 * 免費部署步驟 (只需 2 分鐘)：
 * 1. 前往 https://dash.cloudflare.com/ 註冊/登入免費 Cloudflare 帳號
 * 2. 點擊側邊欄「Workers & Pages」 -> 「Create Application」 -> 「Create Worker」
 * 3. 將本檔案全部內容複製貼上替換 Worker 編輯器內的代碼
 * 4. 點擊「Save and Deploy」取得專屬 Worker 網址 (如 https://tw-stock-proxy.yourname.workers.dev)
 * 5. 前端即可透過該網址即時查詢全台 1,800+ 檔任意個股與 ETF！
 */

export default {
  async fetch(request, env, ctx) {
    // 處理 CORS Preflight (OPTIONS) 請求
    if (request.method === "OPTIONS") {
      return new Response(null, {
        headers: {
          "Access-Control-Allow-Origin": "*",
          "Access-Control-Allow-Methods": "GET, OPTIONS",
          "Access-Control-Allow-Headers": "Content-Type"
        }
      });
    }

    const url = new URL(request.url);
    let code = (url.searchParams.get("code") || "").trim().toUpperCase().replace(".TW", "").replace(".TWO", "");

    if (!code) {
      return new Response(JSON.stringify({ error: "Missing ticker 'code' parameter" }), {
        status: 400,
        headers: { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" }
      });
    }

    // 判斷交易所後綴：上櫃 (TPEx / 債券型 ETF) 用 .TWO，其餘用 .TW
    const isTPEx = code.endsWith("B");
    const primarySymbol = isTPEx ? `${code}.TWO` : `${code}.TW`;
    const secondarySymbol = isTPEx ? `${code}.TW` : `${code}.TWO`;

    async function fetchYahoo(symbol) {
      const targetUrl = `https://query1.finance.yahoo.com/v8/finance/chart/${symbol}?interval=1d&events=div&range=2y`;
      const resp = await fetch(targetUrl, {
        headers: {
          "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        }
      });
      if (!resp.ok) return null;
      const data = await resp.json();
      const chart = data?.chart?.result?.[0];
      if (!chart || !chart.meta?.regularMarketPrice) return null;
      return chart;
    }

    let chart = await fetchYahoo(primarySymbol);
    if (!chart) {
      chart = await fetchYahoo(secondarySymbol);
    }

    if (!chart) {
      return new Response(JSON.stringify({ error: `Ticker ${code} not found on TWSE/TPEx` }), {
        status: 404,
        headers: { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" }
      });
    }

    const meta = chart.meta;
    const price = meta.regularMarketPrice;
    const shortName = meta.shortName || meta.longName || code;
    const events = chart.events?.dividends || {};

    // 計算近 365 天實收現金股利總額
    const nowSec = Math.floor(Date.now() / 1000);
    const oneYearAgoSec = nowSec - 365 * 86400;
    let recentDivs = [];
    let payoutMonths = [];

    for (const key in events) {
      const item = events[key];
      if (item.date >= oneYearAgoSec && item.amount > 0) {
        recentDivs.push(item.amount);
        const m = new Date(item.date * 1000).getMonth() + 1;
        if (!payoutMonths.includes(m)) payoutMonths.push(m);
      }
    }

    let totalDiv = 0;
    let divCount = 0;
    if (recentDivs.length > 0) {
      totalDiv = recentDivs.reduce((a, b) => a + b, 0);
      divCount = recentDivs.length;
    } else if (Object.keys(events).length > 0) {
      const allDivs = Object.values(events);
      totalDiv = allDivs[allDivs.length - 1].amount || 0;
      divCount = 1;
    }

    // 判斷配息頻率
    let frequency = "quarterly";
    let frequencyDesc = "季配息";
    if (divCount >= 10) {
      frequency = "monthly";
      frequencyDesc = "月配息";
      if (payoutMonths.length < 6) payoutMonths = [1,2,3,4,5,6,7,8,9,10,11,12];
    } else if (divCount >= 3) {
      frequency = "quarterly";
      frequencyDesc = "季配息";
    } else if (divCount === 2) {
      frequency = "semi-annual";
      frequencyDesc = "半年配";
    } else if (divCount === 1) {
      frequency = "annual";
      frequencyDesc = "年配息";
    }

    const isBond = code.endsWith("B") || shortName.includes("債");
    const isSingleStock = !code.startsWith("00");
    const isActive = shortName.includes("主動") || code === "00406A";

    const yieldRate = (totalDiv > 0 && price > 0) ? (totalDiv / price) : 0.055;
    const yieldPct = parseFloat((yieldRate * 100).toFixed(2));
    payoutMonths.sort((a, b) => a - b);

    const payload = {
      code: code,
      symbol: isTPEx ? `${code}.TWO` : `${code}.TW`,
      name: isSingleStock ? `${shortName} (個股)` : shortName,
      price: price,
      annual_dividend: parseFloat(totalDiv.toFixed(3)),
      dividend_count: divCount || (frequency === "monthly" ? 12 : 4),
      yield_rate: parseFloat(yieldRate.toFixed(4)),
      yield_pct: yieldPct,
      frequency: frequency,
      frequency_desc: frequencyDesc,
      payout_months: payoutMonths,
      category: isBond ? "bond" : (isSingleStock ? "single_stock" : (isActive ? "active" : "stock")),
      beta: isBond ? 0.35 : (isSingleStock ? 1.05 : 0.88),
      mdd_1y: isBond ? -0.12 : (isSingleStock ? -0.28 : -0.18)
    };

    return new Response(JSON.stringify(payload), {
      status: 200,
      headers: {
        "Content-Type": "application/json; charset=utf-8",
        "Access-Control-Allow-Origin": "*",
        "Cache-Control": "public, max-age=300" // 快取 5 分鐘減少後端壓力
      }
    });
  }
};
