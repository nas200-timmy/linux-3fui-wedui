<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { api } from '../api'
import type { PerfHistory } from '../api'
import PerfChart from '../components/PerfChart.vue'

// ── 类型（与 /api/perf、/api/perf/history 对应）──
interface CpuInfo { cores?: number; usagePercent?: number; user?: number; system?: number; iowait?: number; model?: string; perCore?: number[] }
interface MemInfo { totalMb?: number; usedMb?: number; availableMb?: number; usagePercent?: number; buffersCacheMb?: number; swapTotalMb?: number; swapUsedMb?: number }
interface LoadInfo { one?: string; five?: string; fifteen?: string }
interface ProcInfo { pid?: string; name?: string; cpuTicks?: number; rssMb?: number }
interface GpuInfo { name?: string; utilization?: number; memoryUsedMb?: number; memoryTotalMb?: number; temperature?: number }
interface DiskDevice { name?: string; readKbps?: number; writeKbps?: number }
interface DiskInfo { readKbps?: number; writeKbps?: number; devices?: DiskDevice[] }
interface NetIface { name?: string; rxKbps?: number; txKbps?: number }
interface NetInfo { rxKbps?: number; txKbps?: number; interfaces?: NetIface[] }
type HardwareKey = 'cpu' | 'memory' | 'gpu' | 'disk' | 'network'

const perf = reactive<{ cpu?: CpuInfo; memory?: MemInfo; loadavg?: LoadInfo; processes?: ProcInfo[]; gpu?: GpuInfo[]; disk?: DiskInfo; network?: NetInfo }>({})

// ── 历史序列（服务端环形缓冲 1h；切窗口重拉，轮询实时追加）──
const HIST_CAP = 720
const hist = reactive<PerfHistory>({
  intervalMs: 2000, points: 0, from: 0, to: 0,
  cpu: [], cpuUser: [], cpuSystem: [], memory: [],
  diskRead: [], diskWrite: [], netRx: [], netTx: [],
  gpuUtil: [], gpuMem: [], cores: null,
})

const selected = ref<HardwareKey | ''>('')
const windowMinutes = ref<10 | 30 | 60>(10)
let timer: number | undefined

function append(list: number[], value: number | undefined) {
  if (value === undefined || Number.isNaN(value)) return
  list.push(value)
  if (list.length > HIST_CAP) list.shift()
}

function applyHistory(data: PerfHistory) {
  hist.intervalMs = data.intervalMs
  hist.points = data.points
  hist.from = data.from
  hist.to = data.to
  hist.cpu = data.cpu ?? []
  hist.cpuUser = data.cpuUser ?? []
  hist.cpuSystem = data.cpuSystem ?? []
  hist.memory = data.memory ?? []
  hist.diskRead = data.diskRead ?? []
  hist.diskWrite = data.diskWrite ?? []
  hist.netRx = data.netRx ?? []
  hist.netTx = data.netTx ?? []
  hist.gpuUtil = data.gpuUtil ?? []
  hist.gpuMem = data.gpuMem ?? []
  hist.cores = data.cores ?? null
}

function loadHistory(withCores: boolean) {
  api.perfHistory({ minutes: windowMinutes.value, maxPoints: HIST_CAP, ...(withCores ? { cores: 1 } : {}) })
    .then(applyHistory)
    .catch(() => {})
}

function load() {
  api.perf().then(data => {
    Object.assign(perf, data)
    const cpu = data['cpu'] as CpuInfo | undefined
    const mem = data['memory'] as MemInfo | undefined
    const disk = data['disk'] as DiskInfo | undefined
    const net = data['network'] as NetInfo | undefined
    const gpus = data['gpu'] as GpuInfo[] | undefined
    append(hist.cpu, cpu?.usagePercent)
    append(hist.cpuUser, cpu?.user)
    append(hist.cpuSystem, cpu?.system)
    if (cpu?.perCore?.length) {
      if (!hist.cores) hist.cores = []
      hist.cores.push(cpu.perCore)
      while (hist.cores.length > HIST_CAP) hist.cores.shift()
    }
    append(hist.memory, mem?.usagePercent)
    append(hist.diskRead, disk?.readKbps)
    append(hist.diskWrite, disk?.writeKbps)
    append(hist.netRx, net?.rxKbps)
    append(hist.netTx, net?.txKbps)
    if (gpus?.length) {
      while (hist.gpuUtil.length < gpus.length) { hist.gpuUtil.push([]); hist.gpuMem.push([]) }
      hist.gpuUtil.length = gpus.length
      hist.gpuMem.length = gpus.length
      gpus.forEach((gpu, index) => {
        append(hist.gpuUtil[index] ?? [], gpu.utilization)
        append(hist.gpuMem[index] ?? [], gpu.memoryUsedMb)
      })
    }
    hist.to = Date.now()
    if (!hist.from) hist.from = hist.to - windowMinutes.value * 60000
  }).catch(() => {})
}

onMounted(() => {
  loadHistory(false)
  load()
  timer = window.setInterval(load, 2000)
})
onBeforeUnmount(() => {
  if (timer !== undefined) window.clearInterval(timer)
})

function select(key: HardwareKey) {
  if (selected.value === key) {
    selected.value = ''
    return
  }
  selected.value = key
  if (key === 'cpu') loadHistory(true)
}

function setWindow(minutes: 10 | 30 | 60) {
  windowMinutes.value = minutes
  loadHistory(selected.value === 'cpu')
}

// ── 格式化 ──
function barClass(percent: number | undefined): string {
  if (percent === undefined) return ''
  if (percent >= 90) return 'crit'
  if (percent >= 70) return 'warn'
  return ''
}
function valueClass(percent: number | undefined): string {
  return barClass(percent)
}
function fmtMb(mb: number | undefined): string {
  if (mb === undefined) return '—'
  return mb >= 1024 ? `${(mb / 1024).toFixed(1)}G` : `${Math.round(mb)}M`
}
function fmtRate(kbps: number | undefined): string {
  if (kbps === undefined) return '—'
  if (kbps >= 1024) return `${(kbps / 1024).toFixed(1)} MB/s`
  return `${Math.round(kbps)} KB/s`
}
function gpuMemPercent(gpu: GpuInfo): number {
  if (!gpu.memoryTotalMb) return 0
  return Math.min(100, (gpu.memoryUsedMb ?? 0) / gpu.memoryTotalMb * 100)
}
const swapPercent = computed(() => {
  const total = perf.memory?.swapTotalMb
  if (!total) return 0
  return Math.min(100, (perf.memory?.swapUsedMb ?? 0) / total * 100)
})

// ── 迷你曲线（SVG polyline；rate 类按峰值缩放）──
function sparkPoints(history: number[], maxValue = 100, width = 300, height = 40): string {
  if (history.length < 2) return ''
  const step = width / Math.max(1, HIST_CAP - 1)
  const offset = HIST_CAP - history.length
  return history.map((value, index) =>
    `${((index + offset) * step).toFixed(1)},${(height - Math.min(value, maxValue) * (height / maxValue)).toFixed(1)}`,
  ).join(' ')
}
const sparkCpu = computed(() => sparkPoints(hist.cpu))
const sparkMem = computed(() => sparkPoints(hist.memory))
const sparkDiskRead = computed(() => sparkPoints(hist.diskRead, diskPeak.value))
const sparkDiskWrite = computed(() => sparkPoints(hist.diskWrite, diskPeak.value))
const sparkNetRx = computed(() => sparkPoints(hist.netRx, netPeak.value))
const sparkNetTx = computed(() => sparkPoints(hist.netTx, netPeak.value))
const diskPeak = computed(() => Math.max(1024, ...hist.diskRead, ...hist.diskWrite))
const netPeak = computed(() => Math.max(1024, ...hist.netRx, ...hist.netTx))
function gpuSpark(index: number): string {
  return sparkPoints(hist.gpuUtil[index] ?? [])
}

// ── 详情 ──
const detailTitle = computed(() => {
  switch (selected.value) {
    case 'cpu': return `CPU ${perf.cpu?.model ?? ''}（${perf.cpu?.cores ?? '—'} 核）`
    case 'memory': return '内存'
    case 'gpu': return perf.gpu?.[0]?.name ? `GPU ${perf.gpu[0].name}` : 'GPU'
    case 'disk': return '磁盘'
    case 'network': return '网络'
    default: return ''
  }
})

const detailSeries = computed(() => {
  switch (selected.value) {
    case 'cpu':
      return [
        { data: hist.cpu, color: '#6bb35b', label: '总占用', fill: true },
        { data: hist.cpuUser, color: '#5b9bd5', label: '用户' },
        { data: hist.cpuSystem, color: '#a05bd5', label: '系统' },
      ]
    case 'memory':
      return [{ data: hist.memory, color: '#5b9bd5', label: '已用', fill: true }]
    case 'gpu': {
      const total = perf.gpu?.[0]?.memoryTotalMb ?? 0
      return [
        { data: hist.gpuUtil[0] ?? [], color: '#d5b45b', label: '利用率', fill: true },
        ...(total > 0 ? [{ data: (hist.gpuMem[0] ?? []).map(v => v / total * 100), color: '#5b9bd5', label: '显存', fill: false }] : []),
      ]
    }
    case 'disk':
      return [
        { data: hist.diskRead, color: '#5b9bd5', label: '读取', fill: true },
        { data: hist.diskWrite, color: '#d5a05b', label: '写入', fill: true },
      ]
    case 'network':
      return [
        { data: hist.netRx, color: '#6bb35b', label: '接收', fill: true },
        { data: hist.netTx, color: '#a05bd5', label: '发送', fill: true },
      ]
    default:
      return []
  }
})
const detailYMax = computed(() => (selected.value === 'disk' || selected.value === 'network') ? undefined : 100)
const detailYFormatter = computed(() => {
  if (selected.value === 'disk' || selected.value === 'network') return fmtRate
  return (v: number) => `${v.toFixed(0)}%`
})

// 逻辑处理器小图（cores 为 sample 行序）
const coreCount = computed(() => perf.cpu?.perCore?.length ?? hist.cores?.[0]?.length ?? 0)
function corePoints(index: number): string {
  const rows = hist.cores
  if (!rows || rows.length < 2) return ''
  const width = 300
  const height = 28
  const step = width / Math.max(1, rows.length - 1)
  return rows.map((row, i) =>
    `${(i * step).toFixed(1)},${(height - Math.min(row[index] ?? 0, 100) * (height / 100)).toFixed(1)}`,
  ).join(' ')
}
function coreCurrent(index: number): number | undefined {
  return perf.cpu?.perCore?.[index]
}
/** 每核当前值：保留 1 位小数显示（服务端也 R1，这里再兜一层防长浮点） */
function coreText(index: number): string {
  const value = coreCurrent(index)
  return value === undefined ? '—' : `${Number(value.toFixed(1))}%`
}

// 内存构成
const memSegments = computed(() => {
  const total = perf.memory?.totalMb ?? 0
  if (!total) return []
  const used = perf.memory?.usedMb ?? 0
  const cache = Math.min(perf.memory?.buffersCacheMb ?? 0, Math.max(0, total - used))
  const free = Math.max(0, total - used - cache)
  return [
    { label: '已用', mb: used, pct: used / total * 100, color: '#5b9bd5' },
    { label: '缓冲/缓存', mb: cache, pct: cache / total * 100, color: '#46606e' },
    { label: '可用', mb: free, pct: free / total * 100, color: '#2c2c2c' },
  ]
})
</script>

<template>
  <div>
    <div class="page-header">
      <h2>性能监控</h2>
      <span class="desc">Linux /proc 采集 · 2 秒刷新 · 服务端历史 1 小时 · 点击卡片查看该硬件详情</span>
    </div>

    <!-- 详情态：当前性能 + 历史曲线 -->
    <div v-if="selected" class="glass panel perf-detail">
      <div class="perf-detail-head">
        <button class="perf-back" @click="selected = ''">← 返回总览</button>
        <span class="perf-detail-title">{{ detailTitle }}</span>
        <div class="perf-window">
          <button v-for="w in [10, 30, 60]" :key="w" :class="{ active: windowMinutes === w }" @click="setWindow(w as 10 | 30 | 60)">{{ w }} 分钟</button>
        </div>
      </div>
      <div class="perf-legend">
        <span v-for="s in detailSeries" :key="s.label"><i :style="{ background: s.color }" />{{ s.label }}</span>
      </div>
      <PerfChart :series="detailSeries" :height="240" :y-max="detailYMax" :from="hist.from" :to="hist.to" :y-formatter="detailYFormatter" />

      <!-- CPU：逻辑处理器 + 构成 -->
      <template v-if="selected === 'cpu'">
        <div class="panel-title" style="margin-top: 14px">逻辑处理器</div>
        <div class="perf-core-grid">
          <div v-for="i in coreCount" :key="i" class="perf-core">
            <div class="perf-core-head"><span>CPU {{ i - 1 }}</span><span class="mono">{{ coreText(i - 1) }}</span></div>
            <svg v-if="corePoints(i - 1)" viewBox="0 0 300 28" preserveAspectRatio="none">
              <polyline :points="corePoints(i - 1)" fill="none" stroke="#6bb35b" stroke-width="1.2" />
            </svg>
          </div>
        </div>
        <div class="perf-bar-row" style="margin-top: 12px">
          <span class="lab">用户 / 系统 / IO 等待</span>
          <span class="val" style="flex: 1; text-align: left">{{ perf.cpu?.user ?? '—' }}% / {{ perf.cpu?.system ?? '—' }}% / {{ perf.cpu?.iowait ?? '—' }}%</span>
        </div>
      </template>

      <!-- 内存：构成 + Swap -->
      <template v-else-if="selected === 'memory'">
        <div class="panel-title" style="margin-top: 14px">内存构成</div>
        <div class="perf-membar">
          <div v-for="seg in memSegments" :key="seg.label" :style="{ width: seg.pct + '%', background: seg.color }" :title="`${seg.label} ${fmtMb(seg.mb)}`" />
        </div>
        <div class="perf-bar-row">
          <span class="lab">已用</span>
          <span class="val" style="flex: 1; text-align: left">{{ fmtMb(perf.memory?.usedMb) }} / {{ fmtMb(perf.memory?.totalMb) }}（{{ perf.memory?.usagePercent ?? '—' }}%）</span>
        </div>
        <div class="perf-bar-row">
          <span class="lab">缓冲/缓存</span>
          <span class="val" style="flex: 1; text-align: left">{{ fmtMb(perf.memory?.buffersCacheMb) }}</span>
        </div>
        <div class="perf-bar-row">
          <span class="lab">Swap</span>
          <div class="perf-bar" :class="barClass(swapPercent)">
            <div :style="{ width: swapPercent + '%' }" style="background: linear-gradient(90deg,#6b5a2a,#d5b45b)" />
          </div>
          <span class="val">{{ fmtMb(perf.memory?.swapUsedMb) }} / {{ fmtMb(perf.memory?.swapTotalMb) }}</span>
        </div>
      </template>

      <!-- GPU：每卡实时大数字 -->
      <template v-else-if="selected === 'gpu'">
        <div class="panel-title" style="margin-top: 14px">显卡</div>
        <div v-if="perf.gpu?.length" class="stat-row">
          <div v-for="(gpu, index) in perf.gpu" :key="index" class="stat-card">
            <span class="value" :class="valueClass(gpu.utilization)">{{ gpu.utilization ?? '—' }}%<small style="font-size: 12px; color: var(--text-faint)"> · {{ gpu.temperature ?? '—' }}°C</small></span>
            <span class="label">{{ gpu.name }} · 显存 {{ fmtMb(gpu.memoryUsedMb) }} / {{ fmtMb(gpu.memoryTotalMb) }}（{{ gpuMemPercent(gpu).toFixed(0) }}%）</span>
          </div>
        </div>
        <div v-else class="muted">未检测到 NVIDIA GPU。Intel / AMD 的利用率需要挂载 <code class="mono">/dev/dri</code> 且暂以编码进度为准。</div>
      </template>

      <!-- 磁盘：每设备速率 -->
      <template v-else-if="selected === 'disk'">
        <div class="panel-title" style="margin-top: 14px">物理磁盘</div>
        <table class="list">
          <thead><tr><th>设备</th><th>读取</th><th>写入</th></tr></thead>
          <tbody>
            <tr v-for="device in perf.disk?.devices" :key="device.name">
              <td class="mono">{{ device.name }}</td>
              <td class="mono">{{ fmtRate(device.readKbps) }}</td>
              <td class="mono">{{ fmtRate(device.writeKbps) }}</td>
            </tr>
            <tr v-if="!perf.disk?.devices?.length"><td colspan="3" class="empty">未检测到物理磁盘</td></tr>
          </tbody>
        </table>
      </template>

      <!-- 网络：每接口速率 -->
      <template v-else-if="selected === 'network'">
        <div class="panel-title" style="margin-top: 14px">网络接口（容器视角）</div>
        <table class="list">
          <thead><tr><th>接口</th><th>接收</th><th>发送</th></tr></thead>
          <tbody>
            <tr v-for="iface in perf.network?.interfaces" :key="iface.name">
              <td class="mono">{{ iface.name }}</td>
              <td class="mono">{{ fmtRate(iface.rxKbps) }}</td>
              <td class="mono">{{ fmtRate(iface.txKbps) }}</td>
            </tr>
            <tr v-if="!perf.network?.interfaces?.length"><td colspan="3" class="empty">未检测到网络接口</td></tr>
          </tbody>
        </table>
      </template>
    </div>

    <!-- 总览态：任务管理器式硬件卡片 -->
    <div v-else class="perf-grid">
      <div class="glass panel perf-card" @click="select('cpu')">
        <div class="perf-card-title"><span>CPU</span><span class="muted">{{ perf.cpu?.cores ?? '—' }} 核</span></div>
        <div class="perf-card-value" :class="valueClass(perf.cpu?.usagePercent)">{{ perf.cpu?.usagePercent ?? '—' }}<small>%</small></div>
        <div class="perf-bar" :class="barClass(perf.cpu?.usagePercent)"><div :style="{ width: (perf.cpu?.usagePercent ?? 0) + '%' }" /></div>
        <svg v-if="sparkCpu" viewBox="0 0 300 40" preserveAspectRatio="none"><polyline :points="sparkCpu" fill="none" stroke="#6bb35b" stroke-width="1.5" /></svg>
        <div class="perf-card-sub">{{ perf.cpu?.model ?? '' }}</div>
      </div>

      <div class="glass panel perf-card" @click="select('memory')">
        <div class="perf-card-title"><span>内存</span><span class="muted">{{ fmtMb(perf.memory?.usedMb) }} / {{ fmtMb(perf.memory?.totalMb) }}</span></div>
        <div class="perf-card-value" :class="valueClass(perf.memory?.usagePercent)">{{ perf.memory?.usagePercent ?? '—' }}<small>%</small></div>
        <div class="perf-bar" :class="barClass(perf.memory?.usagePercent)"><div :style="{ width: (perf.memory?.usagePercent ?? 0) + '%' }" /></div>
        <svg v-if="sparkMem" viewBox="0 0 300 40" preserveAspectRatio="none"><polyline :points="sparkMem" fill="none" stroke="#5b9bd5" stroke-width="1.5" /></svg>
        <div class="perf-card-sub">Swap {{ fmtMb(perf.memory?.swapUsedMb) }} / {{ fmtMb(perf.memory?.swapTotalMb) }}</div>
      </div>

      <template v-if="perf.gpu?.length">
        <div v-for="(gpu, index) in perf.gpu" :key="index" class="glass panel perf-card" @click="select('gpu')">
          <div class="perf-card-title"><span>GPU</span><span class="muted">{{ gpu.temperature ?? '—' }}°C</span></div>
          <div class="perf-card-value" :class="valueClass(gpu.utilization)">{{ gpu.utilization ?? '—' }}<small>%</small></div>
          <div class="perf-bar" :class="barClass(gpu.utilization)"><div :style="{ width: (gpu.utilization ?? 0) + '%' }" style="background: linear-gradient(90deg,#6b5a2a,#d5b45b)" /></div>
          <svg v-if="gpuSpark(index)" viewBox="0 0 300 40" preserveAspectRatio="none"><polyline :points="gpuSpark(index)" fill="none" stroke="#d5b45b" stroke-width="1.5" /></svg>
          <div class="perf-card-sub">{{ gpu.name }} · 显存 {{ fmtMb(gpu.memoryUsedMb) }} / {{ fmtMb(gpu.memoryTotalMb) }}</div>
        </div>
      </template>
      <div v-else class="glass panel perf-card">
        <div class="perf-card-title"><span>GPU</span></div>
        <div class="perf-card-value">—<small>%</small></div>
        <div class="perf-card-sub">未检测到 NVIDIA GPU（Intel / AMD 需挂载 /dev/dri）</div>
      </div>

      <div class="glass panel perf-card" @click="select('disk')">
        <div class="perf-card-title"><span>磁盘</span><span class="muted">{{ perf.disk?.devices?.length ?? 0 }} 块</span></div>
        <div class="perf-card-value" style="font-size: 20px; line-height: 1.6">{{ fmtRate(perf.disk?.readKbps) }}<small> 读</small><br />{{ fmtRate(perf.disk?.writeKbps) }}<small> 写</small></div>
        <svg v-if="sparkDiskRead" viewBox="0 0 300 40" preserveAspectRatio="none">
          <polyline :points="sparkDiskRead" fill="none" stroke="#5b9bd5" stroke-width="1.5" />
          <polyline :points="sparkDiskWrite" fill="none" stroke="#d5a05b" stroke-width="1.5" />
        </svg>
        <div class="perf-card-sub">累计读取 / 写入速率（所有物理磁盘）</div>
      </div>

      <div class="glass panel perf-card" @click="select('network')">
        <div class="perf-card-title"><span>网络</span><span class="muted">{{ perf.network?.interfaces?.length ?? 0 }} 个接口</span></div>
        <div class="perf-card-value" style="font-size: 20px; line-height: 1.6">↓ {{ fmtRate(perf.network?.rxKbps) }}<br />↑ {{ fmtRate(perf.network?.txKbps) }}</div>
        <svg v-if="sparkNetRx" viewBox="0 0 300 40" preserveAspectRatio="none">
          <polyline :points="sparkNetRx" fill="none" stroke="#6bb35b" stroke-width="1.5" />
          <polyline :points="sparkNetTx" fill="none" stroke="#a05bd5" stroke-width="1.5" />
        </svg>
        <div class="perf-card-sub">接收 / 发送速率（容器视角）</div>
      </div>
    </div>

    <!-- 底部：负载 + ffmpeg 进程 -->
    <div class="glass panel" style="margin-top: 12px">
      <div class="panel-title">系统负载 / ffmpeg 进程</div>
      <div class="perf-bar-row">
        <span class="lab">负载 1/5/15</span>
        <span class="val" style="flex: 1; text-align: left">{{ perf.loadavg?.one ?? '—' }} · {{ perf.loadavg?.five ?? '—' }} · {{ perf.loadavg?.fifteen ?? '—' }}</span>
      </div>
      <table class="list">
        <thead>
          <tr><th>PID</th><th>进程</th><th>CPU 秒</th><th>RSS</th></tr>
        </thead>
        <tbody>
          <tr v-for="process in perf.processes" :key="process.pid">
            <td class="mono">{{ process.pid }}</td>
            <td>{{ process.name }}</td>
            <td class="mono">{{ process.cpuTicks }}</td>
            <td class="mono">{{ process.rssMb?.toFixed(1) }} MB</td>
          </tr>
          <tr v-if="!perf.processes?.length">
            <td colspan="4" class="empty">当前无 ffmpeg / ffprobe 进程运行</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
