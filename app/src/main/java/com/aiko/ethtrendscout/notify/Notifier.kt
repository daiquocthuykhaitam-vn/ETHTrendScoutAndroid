package com.aiko.ethtrendscout.notify

import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.aiko.ethtrendscout.Action
import com.aiko.ethtrendscout.R
import com.aiko.ethtrendscout.TrendResult

object Notifier {
  const val CHANNEL_ID = "trend_alerts"

  fun ensureChannel(ctx: Context) {
    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
      val name = ctx.getString(R.string.notif_channel_name)
      val desc = ctx.getString(R.string.notif_channel_desc)
      val channel = NotificationChannel(CHANNEL_ID, name, NotificationManager.IMPORTANCE_DEFAULT).apply {
        description = desc
      }
      val nm = ctx.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
      nm.createNotificationChannel(channel)
    }
  }

  fun send(ctx: Context, result: TrendResult, message: String) {
    ensureChannel(ctx)
    val title = " • "
    val text = " | Price= | S= R="
    val notif = NotificationCompat.Builder(ctx, CHANNEL_ID)
      .setSmallIcon(android.R.drawable.stat_notify_more)
      .setContentTitle(title)
      .setContentText(text)
      .setStyle(NotificationCompat.BigTextStyle().bigText(text))
      .setPriority(NotificationCompat.PRIORITY_DEFAULT)
      .build()

    NotificationManagerCompat.from(ctx).notify(result.symbol.hashCode(), notif)
  }

  private fun fmt(v: Double?): String = if (v == null) "-" else String.format("%.2f", v)
}
