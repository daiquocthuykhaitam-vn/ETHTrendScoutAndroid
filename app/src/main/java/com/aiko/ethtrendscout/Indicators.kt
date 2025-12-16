package com.aiko.ethtrendscout

import kotlin.math.max

object Indicators {
  fun ema(values: List<Double>, period: Int): List<Double?> {
    if (values.isEmpty()) return emptyList()
    val k = 2.0 / (period + 1.0)
    val out = MutableList<Double?>(values.size) { null }
    if (values.size < period) return out
    var sma = 0.0
    for (i in 0 until period) sma += values[i]
    sma /= period.toDouble()
    out[period - 1] = sma
    var prev = sma
    for (i in period until values.size) {
      val v = values[i]
      val e = v * k + prev * (1.0 - k)
      out[i] = e
      prev = e
    }
    return out
  }

  fun rsi(closes: List<Double>, period: Int = 14): List<Double?> {
    val out = MutableList<Double?>(closes.size) { null }
    if (closes.size <= period) return out
    var gain = 0.0
    var loss = 0.0
    for (i in 1..period) {
      val ch = closes[i] - closes[i - 1]
      if (ch >= 0) gain += ch else loss += -ch
    }
    var avgGain = gain / period
    var avgLoss = loss / period
    fun calcRsi(ag: Double, al: Double): Double {
      if (al == 0.0) return 100.0
      val rs = ag / al
      return 100.0 - (100.0 / (1.0 + rs))
    }
    out[period] = calcRsi(avgGain, avgLoss)
    for (i in period + 1 until closes.size) {
      val ch = closes[i] - closes[i - 1]
      val g = if (ch > 0) ch else 0.0
      val l = if (ch < 0) -ch else 0.0
      avgGain = (avgGain * (period - 1) + g) / period
      avgLoss = (avgLoss * (period - 1) + l) / period
      out[i] = calcRsi(avgGain, avgLoss)
    }
    return out
  }

  data class SwingLevels(val support: Double?, val resistance: Double?)

  fun swings(candles: List<Candle>, lookback: Int = 120): SwingLevels {
    if (candles.size < 3) return SwingLevels(null, null)
    val start = max(1, candles.size - lookback - 2)
    var support: Double? = null
    var resistance: Double? = null
    for (i in candles.size - 2 downTo start) {
      val prev = candles[i - 1]
      val cur = candles[i]
      val next = candles[i + 1]
      if (support == null && cur.low < prev.low && cur.low < next.low) support = cur.low
      if (resistance == null && cur.high > prev.high && cur.high > next.high) resistance = cur.high
      if (support != null && resistance != null) break
    }
    return SwingLevels(support, resistance)
  }
}
