package com.aiko.ethtrendscout

import android.Manifest
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.aiko.ethtrendscout.notify.Notifier
import com.aiko.ethtrendscout.work.WorkScheduler
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class MainActivity : ComponentActivity() {

  private val reqNotifPerm = registerForActivityResult(
    ActivityResultContracts.RequestPermission()
  ) {}

  override fun onCreate(savedInstanceState: Bundle?) {
    super.onCreate(savedInstanceState)

    if (Build.VERSION.SDK_INT >= 33) {
      reqNotifPerm.launch(Manifest.permission.POST_NOTIFICATIONS)
    }

    Notifier.ensureChannel(this)
    WorkScheduler.schedule(this)

    setContent {
      MaterialTheme {
        Surface(modifier = Modifier.fillMaxSize()) {
          AppScreen()
        }
      }
    }
  }
}

@Composable
private fun AppScreen() {
  val api = remember { BinanceApi() }
  val store = remember { TrendStore(LocalContext.current) }

  var symbol by remember { mutableStateOf("ETHUSDT") }
  var result by remember { mutableStateOf<TrendResult?>(null) }
  var error by remember { mutableStateOf<String?>(null) }
  var loading by remember { mutableStateOf(false) }

  var autoRefresh by remember { mutableStateOf(store.getAutoRefreshInApp()) }
  var notifyEnabled by remember { mutableStateOf(store.isNotifyEnabled()) }

  val scope = rememberCoroutineScope()

  fun refresh() {
    scope.launch {
      loading = true
      error = null
      try {
        val res = withContext(Dispatchers.IO) {
          val last = api.fetchLastPrice(symbol)
          val d1 = api.fetchKlines(symbol, "1d", 260)
          val h4 = api.fetchKlines(symbol, "4h", 260)
          val h1 = api.fetchKlines(symbol, "1h", 260)
          val m15 = api.fetchKlines(symbol, "15m", 260)
          val m5 = api.fetchKlines(symbol, "5m", 260)
          TrendEngine.evaluate(symbol, last, d1, h4, h1, m15, m5)
        }
        result = res
      } catch (e: Exception) {
        error = e.message ?: "Unknown error"
      } finally {
        loading = false
      }
    }
  }

  LaunchedEffect(symbol) { refresh() }

  LaunchedEffect(autoRefresh, symbol) {
    store.setAutoRefreshInApp(autoRefresh)
    while (autoRefresh) {
      delay(5 * 60 * 1000L)
      refresh()
    }
  }

  Column(
    modifier = Modifier
      .fillMaxSize()
      .padding(16.dp)
      .verticalScroll(rememberScrollState()),
    verticalArrangement = Arrangement.spacedBy(12.dp)
  ) {
    Text("Trend Scout (Futures) – chỉ check xu hướng", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.SemiBold)

    Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
      SymbolDropdown(symbol = symbol, onChange = { symbol = it })
      Button(onClick = { refresh() }, enabled = !loading) { Text(if (loading) "Loading..." else "Refresh") }
    }

    Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
      FilterChip(selected = autoRefresh, onClick = { autoRefresh = !autoRefresh }, label = { Text("Auto 5m (in-app)") })
      FilterChip(selected = notifyEnabled, onClick = { notifyEnabled = !notifyEnabled; store.setNotifyEnabled(notifyEnabled) }, label = { Text("Notify (15m bg)") })
    }

    if (error != null) {
      Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.errorContainer)) {
        Column(Modifier.padding(12.dp)) {
          Text("Lỗi", fontWeight = FontWeight.Bold)
          Text(error!!)
        }
      }
    }

    if (result != null) {
      TrendCard(result!!)
      DetailCard(result!!)
    } else {
      Card { Column(Modifier.padding(12.dp)) { Text("Đang lấy dữ liệu...") } }
    }

    Text("Lưu ý: Background notify tối thiểu ~15 phút. Auto 5 phút là khi app đang mở.", style = MaterialTheme.typography.bodySmall)
  }
}

@Composable
private fun SymbolDropdown(symbol: String, onChange: (String) -> Unit) {
  var expanded by remember { mutableStateOf(false) }
  val items = Pairs.DEFAULT
  Box {
    OutlinedButton(onClick = { expanded = true }) { Text(symbol) }
    DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
      items.forEach {
        DropdownMenuItem(text = { Text(it) }, onClick = { expanded = false; onChange(it) })
      }
    }
  }
}

@Composable
private fun TrendCard(r: TrendResult) {
  val (title, color) = when (r.action) {
    Action.LONG -> "LONG ưu tiên" to MaterialTheme.colorScheme.primaryContainer
    Action.SHORT -> "SHORT ưu tiên" to MaterialTheme.colorScheme.errorContainer
    Action.NO_TRADE -> "NO TRADE (đứng ngoài)" to MaterialTheme.colorScheme.surfaceVariant
  }

  Card(colors = CardDefaults.cardColors(containerColor = color)) {
    Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
      Text(title, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
      Text("Giá: ")
      Text("Support:    |   Resistance: ")
    }
  }
}

@Composable
private fun DetailCard(r: TrendResult) {
  Card {
    Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
      Text("Chi tiết tín hiệu", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)

      TimeframeRow("D1", r.d1)
      TimeframeRow("H4", r.h4)
      TimeframeRow("H1", r.h1)

      Divider()
      Text("M15 filter: ")
      Text("M5 filter: ")
    }
  }
}

@Composable
private fun TimeframeRow(tf: String, st: TimeframeStatus) {
  val badge = when (st.bias) {
    Bias.BULL -> "BULL"
    Bias.BEAR -> "BEAR"
    Bias.NEUTRAL -> "NEUTRAL"
  }
  Row(horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
    Text(tf, fontWeight = FontWeight.Bold, modifier = Modifier.width(36.dp))
    AssistChip(onClick = {}, label = { Text(badge) })
  }
  Text(st.reason, style = MaterialTheme.typography.bodySmall)
}

private fun fmt(v: Double?): String = if (v == null) "-" else String.format("%.2f", v)
