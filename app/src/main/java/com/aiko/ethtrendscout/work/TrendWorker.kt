package com.aiko.ethtrendscout.work

import android.content.Context
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import com.aiko.ethtrendscout.*
import com.aiko.ethtrendscout.notify.Notifier
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

class TrendWorker(
  appContext: Context,
  params: WorkerParameters
) : CoroutineWorker(appContext, params) {

  override suspend fun doWork(): Result = withContext(Dispatchers.IO) {
    try {
      val api = BinanceApi()
      val store = TrendStore(applicationContext)

      for (symbol in Pairs.DEFAULT) {
        val lastPrice = api.fetchLastPrice(symbol)
        val d1 = api.fetchKlines(symbol, "1d", 260)
        val h4 = api.fetchKlines(symbol, "4h", 260)
        val h1 = api.fetchKlines(symbol, "1h", 260)
        val m15 = api.fetchKlines(symbol, "15m", 260)
        val m5 = api.fetchKlines(symbol, "5m", 260)

        val res = TrendEngine.evaluate(symbol, lastPrice, d1, h4, h1, m15, m5)

        val prev = store.getLastAction(symbol)
        val now = res.action.name
        store.setLastAction(symbol, now)

        val notifyEnabled = store.isNotifyEnabled()
        if (notifyEnabled && prev != now && res.action != Action.NO_TRADE) {
          Notifier.send(applicationContext, res, "Signal changed:  → ")
        }
      }
      Result.success()
    } catch (e: Exception) {
      Result.retry()
    }
  }
}
