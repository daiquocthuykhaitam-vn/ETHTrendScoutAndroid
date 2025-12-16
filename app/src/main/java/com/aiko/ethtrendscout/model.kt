package com.aiko.ethtrendscout

data class Candle(
  val openTime: Long,
  val open: Double,
  val high: Double,
  val low: Double,
  val close: Double,
  val volume: Double
)

enum class Bias { BULL, BEAR, NEUTRAL }
enum class Action { LONG, SHORT, NO_TRADE }

data class TimeframeStatus(
  val bias: Bias,
  val reason: String
)

data class TrendResult(
  val symbol: String,
  val lastPrice: Double,
  val d1: TimeframeStatus,
  val h4: TimeframeStatus,
  val h1: TimeframeStatus,
  val m15Filter: String,
  val m5Filter: String,
  val support: Double?,
  val resistance: Double?,
  val action: Action,
  val updatedAtMs: Long
)
