package com.aiko.ethtrendscout

import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONArray

class BinanceApi(
  private val client: OkHttpClient = OkHttpClient()
) {
  fun fetchKlines(symbol: String, interval: String, limit: Int = 300): List<Candle> {
    val url = "https://fapi.binance.com/fapi/v1/klines?symbol=&interval=&limit="
    val req = Request.Builder().url(url).get().build()
    client.newCall(req).execute().use { resp ->
      if (!resp.isSuccessful) throw RuntimeException("HTTP : ")
      val body = resp.body?.string() ?: throw RuntimeException("Empty response")
      val arr = JSONArray(body)
      val out = ArrayList<Candle>(arr.length())
      for (i in 0 until arr.length()) {
        val row = arr.getJSONArray(i)
        out.add(
          Candle(
            openTime = row.getLong(0),
            open = row.getString(1).toDouble(),
            high = row.getString(2).toDouble(),
            low  = row.getString(3).toDouble(),
            close = row.getString(4).toDouble(),
            volume = row.getString(5).toDouble()
          )
        )
      }
      return out
    }
  }

  fun fetchLastPrice(symbol: String): Double {
    val url = "https://fapi.binance.com/fapi/v1/ticker/price?symbol="
    val req = Request.Builder().url(url).get().build()
    client.newCall(req).execute().use { resp ->
      if (!resp.isSuccessful) throw RuntimeException("HTTP : ")
      val body = resp.body?.string() ?: throw RuntimeException("Empty response")
      val obj = org.json.JSONObject(body)
      return obj.getString("price").toDouble()
    }
  }
}
