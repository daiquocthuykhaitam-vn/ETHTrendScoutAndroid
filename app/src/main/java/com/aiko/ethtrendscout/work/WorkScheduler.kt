package com.aiko.ethtrendscout.work

import android.content.Context
import androidx.work.*
import java.util.concurrent.TimeUnit

object WorkScheduler {
  private const val UNIQUE_NAME = "TrendWorkerPeriodic"

  fun schedule(ctx: Context) {
    val req = PeriodicWorkRequestBuilder<TrendWorker>(15, TimeUnit.MINUTES)
      .setConstraints(
        Constraints.Builder()
          .setRequiredNetworkType(NetworkType.CONNECTED)
          .build()
      )
      .build()

    WorkManager.getInstance(ctx).enqueueUniquePeriodicWork(
      UNIQUE_NAME,
      ExistingPeriodicWorkPolicy.UPDATE,
      req
    )
  }
}
