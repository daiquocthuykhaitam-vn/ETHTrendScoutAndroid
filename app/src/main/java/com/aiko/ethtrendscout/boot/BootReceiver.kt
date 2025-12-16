package com.aiko.ethtrendscout.boot

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import com.aiko.ethtrendscout.work.WorkScheduler

class BootReceiver : BroadcastReceiver() {
  override fun onReceive(context: Context, intent: Intent) {
    WorkScheduler.schedule(context)
  }
}
