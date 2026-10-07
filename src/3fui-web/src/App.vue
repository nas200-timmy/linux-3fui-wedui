<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import HomeView from './views/HomeView.vue'
import PresetView from './views/PresetView.vue'
import QueueView from './views/QueueView.vue'
import FilesView from './views/FilesView.vue'
import SettingsView from './views/SettingsView.vue'
import AgentView from './views/AgentView.vue'
import PerfView from './views/PerfView.vue'
import MediaInfoView from './views/MediaInfoView.vue'
import PlaceholderView from './views/PlaceholderView.vue'
import LoginView from './views/LoginView.vue'
import EncoderSwitchDialog from './components/EncoderSwitchDialog.vue'
import ContainerCompatDialog from './components/ContainerCompatDialog.vue'
import { api, openQueueWebSocket } from './api'
import { useAuth, useEncoderSwitch, usePendingFiles, useToast, useWsStatus } from './store'
import type { EncoderSwitchMessage, PresetData } from './api'

type NavId =
  | 'home'
  | 'queue'
  | 'files'
  | 'preset'
  | 'agent'
  | 'studios'
  | 'mediainfo'
  | 'player'
  | 'perf'
  | 'tools'
  | 'settings'
  | 'supporter'

type NavEntry = { type: 'item'; id: NavId; label: string } | { type: 'sep' }

// 严格按原版 FormMain_v6.Designer.vb 的选项顺序与分组（含 4 条分隔线）
const NAV: NavEntry[] = [
  { type: 'item', id: 'home', label: '起始页面' },
  { type: 'item', id: 'queue', label: '编码队列' },
  { type: 'sep' },
  { type: 'item', id: 'files', label: '准备文件' },
  { type: 'item', id: 'preset', label: '参数面板' },
  { type: 'item', id: 'agent', label: 'Agent 智能体' },
  { type: 'item', id: 'studios', label: '3FUI Studios' },
  { type: 'sep' },
  { type: 'item', id: 'mediainfo', label: 'ffprobe 媒体信息' },
  { type: 'item', id: 'player', label: 'ffplay 调试播放器' },
  { type: 'item', id: 'perf', label: '性能监控' },
  { type: 'item', id: 'tools', label: '集成工具' },
  { type: 'sep' },
  { type: 'item', id: 'settings', label: '软件设置' },
  { type: 'item', id: 'supporter', label: '支持者' },
  { type: 'sep' },
]

const pendingFiles = usePendingFiles()
const toasts = useToast()
const auth = useAuth()
const encoderSwitch = useEncoderSwitch()
const wsStatus = useWsStatus()
const hasEncoderSwitchPending = computed(() => encoderSwitch.pending.value.length > 0)
const current = ref<NavId>('home')
const search = ref('')
const queueCount = ref(0)
const status = reactive<Record<string, unknown>>({})
const perf = reactive<Record<string, unknown>>({})

// 编码器切换告知：服务端自动切换编码器后经 WS 推送，由全局弹窗逐条确认（与 QueueView 各自开一条 /ws）
let socket: WebSocket | null = null
function applySocketMessage(data: Record<string, unknown>) {
  if (data.type === 'encoder-switch') encoderSwitch.push(data as unknown as EncoderSwitchMessage)
}
function connectSocket() {
  socket = openQueueWebSocket(applySocketMessage, value => wsStatus.set(value))
  // 外层覆盖 onclose 会顶掉 openQueueWebSocket 内部的状态回调，断线状态必须在这里补发
  socket.onclose = () => { wsStatus.set(false); window.setTimeout(connectSocket, 3000) }
}

const presetViewRef = ref<{ loadExternal: (data: PresetData, name?: string) => void } | null>(null)

const filteredNav = computed<NavEntry[]>(() => {
  const q = search.value.trim().toLowerCase()
  if (!q) return NAV
  return NAV.filter(entry => {
    if (entry.type === 'sep') return true
    return entry.label.toLowerCase().includes(q)
  })
})

// 标题栏右侧实时系统监控（原版：FFmpegFreeUI | CPU x% | RAM xM/xM | GPU x%）
const titleStats = computed(() => {
  const cpu = (perf['cpu'] as Record<string, unknown> | undefined) ?? {}
  const mem = (perf['memory'] as Record<string, unknown> | undefined) ?? {}
  const gpu = ((perf['gpu'] as Record<string, unknown>[] | undefined) ?? [])[0]
  const parts: string[] = ['FFmpegFreeUI']
  parts.push(`CPU ${cpu['usagePercent'] ?? 0}%`)
  parts.push(`RAM ${Math.round(Number(mem['usedMb'] ?? 0))}M / ${Math.round(Number(mem['totalMb'] ?? 0))}M`)
  parts.push(gpu ? `GPU ${gpu['utilization']}%` : 'GPU 未检测')
  return parts.join('   |   ')
})

function applyBuiltinPreset(preset: PresetData, name: string) {
  presetViewRef.value?.loadExternal(preset, name)
  current.value = 'preset'
}

function refreshStatus() {
  api.status().then(data => Object.assign(status, data)).catch(() => {})
  api.queue.list().then(list => { queueCount.value = list.length }).catch(() => {})
  api.perf().then(data => Object.assign(perf, data)).catch(() => {})
}

onMounted(() => {
  auth.refresh()
  refreshStatus()
  const timer = setInterval(refreshStatus, 2000)
  window.addEventListener('beforeunload', () => clearInterval(timer))
  connectSocket()
})

onBeforeUnmount(() => { socket?.close() })
</script>

<template>
  <!-- 启用登录且未认证：整页只剩登录框 -->
  <LoginView v-if="auth.needLogin" />
  <div v-else class="app-shell">
    <!-- 标题栏：42px，#303030，居中实时系统监控（网页版不含窗口控制按钮） -->
    <header class="titlebar">
      <span class="titlebar-icon">3F</span>
      <span class="titlebar-name">FFmpegFreeUI</span>
      <span class="titlebar-stats">{{ titleStats }}</span>
      <span class="ws-dot" :class="{ on: wsStatus.connected }" :title="wsStatus.connected ? '实时推送已连接' : '实时推送已断开，3 秒后自动重连'">
        <i />{{ wsStatus.connected ? '已连接' : '已断开' }}
      </span>
      <span class="titlebar-tag">Linux 网页版</span>
    </header>

    <div class="app-body">
      <!-- 一级侧栏：200px，#303030 -->
      <nav class="nav">
        <div class="nav-search">
          <input v-model="search" type="text" placeholder="搜索选项卡标题" />
        </div>
        <div class="nav-list">
          <template v-for="(entry, index) in filteredNav" :key="index">
            <div v-if="entry.type === 'sep'" class="nav-sep" />
            <div v-else class="nav-item" :class="{ active: current === entry.id }" @click="current = entry.id">
              <span class="label">{{ entry.label }}</span>
              <span v-if="entry.id === 'queue' && queueCount > 0" class="count">{{ queueCount }}</span>
              <span v-if="entry.id === 'files' && pendingFiles.files.length > 0" class="count">{{ pendingFiles.files.length }}</span>
            </div>
          </template>
        </div>
      </nav>

      <main class="main">
        <!-- 参数面板常驻（v-show）：原版默认「不要自动重置页面」，切走再回来二级页与编辑状态都在 -->
        <PresetView v-show="current === 'preset'" ref="presetViewRef" />
        <div v-if="current !== 'preset'" class="page" :class="{ fill: current === 'queue' || current === 'files' || current === 'agent' || current === 'mediainfo' }">
          <HomeView v-if="current === 'home'" @apply="(data, name) => applyBuiltinPreset(data as PresetData, name)" />
          <QueueView v-else-if="current === 'queue'" />
          <FilesView v-else-if="current === 'files'" @go-preset="current = 'preset'" @go-queue="current = 'queue'" />
          <AgentView v-else-if="current === 'agent'" style="height: 100%" />
          <PlaceholderView
            v-else-if="current === 'studios'"
            title="3FUI Studios"
            desc="SP 支持者专属内容页。linux-3fui 为全功能免费版，此页暂未移植。"
          />
          <MediaInfoView v-else-if="current === 'mediainfo'" />
          <PlaceholderView
            v-else-if="current === 'player'"
            title="ffplay 调试播放器"
            desc="原版用于 ffplay 预览调试。网页版暂未移植播放器，后续可考虑 HLS 预览。"
          />
          <PerfView v-else-if="current === 'perf'" />
          <PlaceholderView
            v-else-if="current === 'tools'"
            title="集成工具"
            desc="原版的混流/合并等集成工具。核心转码功能已在「参数面板」与「编码队列」中实现。"
          />
          <SettingsView v-else-if="current === 'settings'" />
          <PlaceholderView
            v-else-if="current === 'supporter'"
            title="支持者"
            desc="感谢支持 3FUI 开发者。linux-3fui 为开源社区移植版，暂无付费内容。"
          />
        </div>
      </main>
    </div>

    <div class="toast-wrap">
      <div v-for="toast in toasts.toasts" :key="toast.id" class="toast" :class="toast.kind">{{ toast.text }}</div>
    </div>

    <!-- 编码器切换告知：有待确认消息时挂全局弹窗（内部取队首，确认一条自动换下一条） -->
    <EncoderSwitchDialog v-if="hasEncoderSwitchPending" />
    <!-- 入队前容器兼容确认（MP4/WebM 装不下「全保留」内容时弹出） -->
    <ContainerCompatDialog />
  </div>
</template>
