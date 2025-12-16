package com.aiko.ethtrendscout

import android.content.Context

class TrendStore(ctx: Context) {
  private val sp = ctx.getSharedPreferences("trend_store", Context.MODE_PRIVATE)

  fun getLastAction(symbol: String): String = sp.getString("last_action_", Action.NO_TRADE.name)!!
  fun setLastAction(symbol: String, action: String) {
    sp.edit().putString("last_action_", action).apply()
  }

  fun isNotifyEnabled(): Boolean = sp.getBoolean("notify_enabled", true)
  fun setNotifyEnabled(enabled: Boolean) {
    sp.edit().putBoolean("notify_enabled", enabled).apply()
  }

  fun getAutoRefreshInApp(): Boolean = sp.getBoolean("auto_refresh_in_app", true)
  fun setAutoRefreshInApp(enabled: Boolean) {
    sp.edit().putBoolean("auto_refresh_in_app", enabled).apply()
  }
}
