package com.aiko.ethtrendscout

object TrendEngine {

  data class Config(
    val d1EmaFast: Int = 50,
    val d1EmaSlow: Int = 200,
    val hEma: Int = 50,
    val rsiPeriod: Int = 14,
    val shortM15RsiMin: Double = 45.0,
    val shortM15RsiMax: Double = 60.0,
    val longM15RsiMax: Double = 40.0,
    val extremeM5RsiLow: Double = 20.0
  )

  fun evaluate(
    symbol: String,
    lastPrice: Double,
    d1: List<Candle>,
    h4: List<Candle>,
    h1: List<Candle>,
    m15: List<Candle>,
    m5: List<Candle>,
    cfg: Config = Config()
  ): TrendResult {

    val d1Closes = d1.map { it.close }
    val h4Closes = h4.map { it.close }
    val h1Closes = h1.map { it.close }
    val m15Closes = m15.map { it.close }
    val m5Closes = m5.map { it.close }

    val d1Ema50 = Indicators.ema(d1Closes, cfg.d1EmaFast).lastOrNull()
    val d1Ema200 = Indicators.ema(d1Closes, cfg.d1EmaSlow).lastOrNull()
    val d1Rsi = Indicators.rsi(d1Closes, cfg.rsiPeriod).lastOrNull()

    val d1Bias = run {
      val ema50 = d1Ema50 ?: return@run Bias.NEUTRAL
      val ema200 = d1Ema200 ?: return@run Bias.NEUTRAL
      val close = d1Closes.last()
      if (close > ema50 && ema50 > ema200) Bias.BULL
      else if (close < ema50 || ema50 < ema200) Bias.BEAR
      else Bias.NEUTRAL
    }
    val d1Reason = "close= | EMA= | EMA= | RSI="

    val h4Ema = Indicators.ema(h4Closes, cfg.hEma).lastOrNull()
    val h1Ema = Indicators.ema(h1Closes, cfg.hEma).lastOrNull()
    val h1Rsi = Indicators.rsi(h1Closes, cfg.rsiPeriod).lastOrNull()

    val h4Bias = biasByEma(h4Closes.last(), h4Ema)
    val h1Bias = biasByEma(h1Closes.last(), h1Ema)

    val h4Reason = "close= | EMA="
    val h1Reason = "close= | EMA= | RSI="

    val m15Rsi = Indicators.rsi(m15Closes, cfg.rsiPeriod).lastOrNull()
    val m5Rsi = Indicators.rsi(m5Closes, cfg.rsiPeriod).lastOrNull()
    val m15Ema20 = Indicators.ema(m15Closes, 20).lastOrNull()

    var m15Filter = "N/A"
    var m5Filter = "N/A"

    val action = when (d1Bias) {
      Bias.BEAR -> {
        if (h4Bias == Bias.BEAR && h1Bias == Bias.BEAR) {
          m15Filter = if (m15Rsi != null && m15Rsi in cfg.shortM15RsiMin..cfg.shortM15RsiMax) "OK (RSI= in 45-60)"
                      else "WAIT (RSI=; prefer rebound 45-60)"
          m5Filter = if (m5Rsi != null && m5Rsi < cfg.extremeM5RsiLow) "WAIT (M5 RSI= too low, avoid chasing)"
                     else "OK (M5 RSI=)"
          Action.SHORT
        } else {
          m15Filter = "WAIT (H4/H1 not aligned)"
          m5Filter = "WAIT"
          Action.NO_TRADE
        }
      }
      Bias.BULL -> {
        if (h4Bias == Bias.BULL && h1Bias == Bias.BULL) {
          m15Filter = if (m15Rsi != null && m15Rsi <= cfg.longM15RsiMax && (m15Ema20 == null || m15Closes.last() >= m15Ema20)) {
            "OK (pullback RSI= <= 40)"
          } else "WAIT (RSI=; prefer pullback <= 40 & hold EMA20)"
          m5Filter = "OK (M5 RSI=)"
          Action.LONG
        } else {
          m15Filter = "WAIT (H4/H1 not aligned)"
          m5Filter = "WAIT"
          Action.NO_TRADE
        }
      }
      Bias.NEUTRAL -> {
        m15Filter = "WAIT (D1 neutral)"
        m5Filter = "WAIT"
        Action.NO_TRADE
      }
    }

    val swings = Indicators.swings(h4, lookback = 200)
    return TrendResult(
      symbol = symbol,
      lastPrice = lastPrice,
      d1 = TimeframeStatus(d1Bias, d1Reason),
      h4 = TimeframeStatus(h4Bias, h4Reason),
      h1 = TimeframeStatus(h1Bias, h1Reason),
      m15Filter = m15Filter,
      m5Filter = m5Filter,
      support = swings.support,
      resistance = swings.resistance,
      action = action,
      updatedAtMs = System.currentTimeMillis()
    )
  }

  private fun biasByEma(close: Double, ema: Double?): Bias {
    ema ?: return Bias.NEUTRAL
    return when {
      close > ema -> Bias.BULL
      close < ema -> Bias.BEAR
      else -> Bias.NEUTRAL
    }
  }

  private fun fmt(v: Double?): String = if (v == null || v.isNaN()) "-" else String.format("%.2f", v)
}
