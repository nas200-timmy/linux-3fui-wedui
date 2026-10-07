import { defineStore, storeToRefs } from 'pinia'
import { reactive, ref, watch } from 'vue'
import { api } from './api'
import type { EncoderSwitchMessage, PresetData } from './api'
import { newPreset } from './schema'

// 待处理文件列表（「准备文件」页面选好后，供「参数面板」添加到队列）
export const usePendingFiles = defineStore('pendingFiles', () => {
  const files = ref<string[]>([])
  function add(paths: string[]) {
    for (const path of paths) if (path && !files.value.includes(path)) files.value.push(path)
  }
  function remove(path: string) {
    files.value = files.value.filter(item => item !== path)
  }
  function clear() {
    files.value = []
  }
  return { files, add, remove, clear }
})

// 全局 toast 提示（单例：所有视图共用同一份，否则各视图 push 的提示不会被 App.vue 渲染出来）
interface ToastItem { id: number; kind: 'ok' | 'err'; text: string }
let toastId = 0
const toasts = reactive<ToastItem[]>([])

export const useToast = () => {
  function push(kind: 'ok' | 'err', text: string) {
    const id = ++toastId
    toasts.push({ id, kind, text })
    setTimeout(() => {
      const index = toasts.findIndex(item => item.id === id)
      if (index >= 0) toasts.splice(index, 1)
    }, 3600)
  }
  return { toasts, push }
}

// 编码器切换告知（单例，照 useToast）：App.vue 开 WS 收 encoder-switch 消息入队，弹窗逐条确认
const encoderSwitchPending = ref<EncoderSwitchMessage[]>([])

export const useEncoderSwitch = () => {
  function push(msg: EncoderSwitchMessage) {
    // 同一任务重复推送（如运行时报错再次触发）只保留最新一条
    const index = encoderSwitchPending.value.findIndex(item => item.任务ID === msg.任务ID)
    if (index >= 0) encoderSwitchPending.value.splice(index, 1, msg)
    else encoderSwitchPending.value.push(msg)
  }
  async function resolve(msg: EncoderSwitchMessage, 选择: 'preset' | 'once') {
    await api.encoderSwitchRespond(msg.任务ID, 选择)
    const index = encoderSwitchPending.value.indexOf(msg)
    if (index >= 0) encoderSwitchPending.value.splice(index, 1)
  }
  return { pending: encoderSwitchPending, push, resolve }
}

// 全局状态（TLS / 服务信息）
export const useAppState = defineStore('appState', () => {
  const status = ref<Record<string, unknown>>({})
  const tls = ref<{ active: boolean; port: number; subject: string | null; notAfter?: string | null }>({ active: false, port: 0, subject: null })
  return { status, tls }
})

// 登录认证状态：401 时 api.ts 会把 needLogin 置 true，App.vue 显示登录页
export const useAuth = defineStore('auth', () => {
  const enabled = ref(false)
  const authenticated = ref(false)
  const needLogin = ref(false)
  async function refresh() {
    try {
      const data = await fetch('/api/auth/status').then(response => response.json())
      enabled.value = Boolean(data.enabled)
      authenticated.value = Boolean(data.authenticated)
      needLogin.value = enabled.value && !authenticated.value
    } catch {
      /* 状态接口挂了就不挡页面 */
    }
  }
  function onUnauthorized() {
    if (enabled.value) {
      authenticated.value = false
      needLogin.value = true
    }
  }
  return { enabled, authenticated, needLogin, refresh, onUnauthorized }
})

// 队列实时推送 WebSocket 的连接状态（标题栏指示灯，与具体页面解耦）
export const useWsStatus = defineStore('wsStatus', () => {
  const connected = ref(false)
  function set(value: boolean) {
    connected.value = value
  }
  return { connected, set }
})

// 当前编辑中的预设（参数面板的唯一数据源，「准备文件」一键入队也用它）
// v4：默认值改为「全保留」策略（全音轨/字幕/元数据），旧缓存的空白流控制字段会挡住新默认值
const PRESET_CACHE_KEY = 'linux-3fui-preset-v4'

// ── 队列实时事件：App.vue 的全局 /ws 把 event/task/progress/encoder-switch 分流到这里，
//    Agent 视图消费它生成「⚙ harness 通知」（不新开 WebSocket 连接）──
export interface QueueFeedItem { seq: number; data: Record<string, unknown> }

export const useQueueFeed = defineStore('queueFeed', () => {
  const events = ref<QueueFeedItem[]>([])
  let seq = 0
  function push(data: Record<string, unknown>) {
    seq += 1
    // 只留最近 200 条，防止长跑任务把内存顶满
    events.value = [...events.value, { seq, data }].slice(-200)
  }
  return { events, push }
})

export const useCurrentPreset = defineStore('currentPreset', () => {
  const preset = ref<PresetData>(newPreset())
  const saveName = ref('')
  // 二级页选择：原版默认「不要自动重置页面」，跨一级选项卡切换保留
  const subPage = ref('overview')

  try {
    const raw = localStorage.getItem(PRESET_CACHE_KEY)
    if (raw) {
      const data = JSON.parse(raw) as { preset?: PresetData; saveName?: string; subPage?: string }
      if (data?.preset && typeof data.preset === 'object') preset.value = Object.assign(newPreset(), data.preset)
      if (typeof data?.saveName === 'string') saveName.value = data.saveName
      if (typeof data?.subPage === 'string' && data.subPage) subPage.value = data.subPage
    }
  } catch {
    /* 缓存损坏则忽略，用默认值 */
  }

  let cacheTimer: number | undefined
  watch([preset, saveName, subPage], () => {
    window.clearTimeout(cacheTimer)
    cacheTimer = window.setTimeout(() => {
      try {
        localStorage.setItem(PRESET_CACHE_KEY, JSON.stringify({ preset: preset.value, saveName: saveName.value, subPage: subPage.value }))
      } catch {
        /* 容量或隐私模式导致失败时静默忽略 */
      }
    }, 400)
  }, { deep: true })

  function replace(data: PresetData, name = '') {
    preset.value = Object.assign(newPreset(), data)
    saveName.value = name
  }

  return { preset, saveName, subPage, replace }
})

export { storeToRefs }

// ── 入队前容器兼容检查 ──
// 「全保留」默认策略（全音轨/字幕/元数据/章节/附件）遇到 MP4/WebM 等受限容器时 ffmpeg 会硬报错。
// 入队前检查：有冲突就弹窗让用户选「改用 MKV」或「丢弃不兼容项」，而不是让任务跑到报错。
export interface 容器冲突项 { 项目: string; 说明: string; off: [string, unknown] }

interface 容器限制 { flag: string; 项目: string; 说明: string; off: [string, unknown] }

const MP4_LIKE = [
  { flag: '流控制_附件选项', 项目: '附件', 说明: '该容器不支持附件流（字体、封面等）', off: ['流控制_附件选项', ''] as [string, unknown] },
  { flag: '流控制_启用保留其他字幕流', 项目: '字幕', 说明: '该容器仅支持 mov_text 字幕；SRT/ASS/PGS 等原样复制会报错', off: ['流控制_启用保留其他字幕流', false] as [string, unknown] },
]
// 纯音频容器：「全保留」策略下的视频/字幕/附件/章节全装不下（m4a 例外支持章节）
const AUDIO_ONLY = [
  { flag: '视频参数_编码器_具体编码', 项目: '视频', 说明: '纯音频容器不能装视频流', off: ['视频参数_编码器_具体编码', ''] as [string, unknown] },
  { flag: '流控制_启用保留其他字幕流', 项目: '字幕', 说明: '纯音频容器不支持字幕流', off: ['流控制_启用保留其他字幕流', false] as [string, unknown] },
  { flag: '流控制_附件选项', 项目: '附件', 说明: '纯音频容器不支持附件流', off: ['流控制_附件选项', ''] as [string, unknown] },
  { flag: '流控制_章节选项', 项目: '章节', 说明: '纯音频容器不支持章节', off: ['流控制_章节选项', ''] as [string, unknown] },
]
const CONTAINER_LIMITS: Record<string, 容器限制[]> = {
  mp4: MP4_LIKE, m4v: MP4_LIKE, mov: MP4_LIKE,
  '3gp': [...MP4_LIKE, { flag: '流控制_章节选项', 项目: '章节', 说明: '3GP 不支持章节', off: ['流控制_章节选项', ''] }],
  ogv: [
    { flag: '流控制_附件选项', 项目: '附件', 说明: 'OGV 不支持附件流', off: ['流控制_附件选项', ''] },
    { flag: '流控制_启用保留其他字幕流', 项目: '字幕', 说明: 'Ogg 容器仅支持极少数字幕编码，原样复制会报错', off: ['流控制_启用保留其他字幕流', false] },
    { flag: '流控制_章节选项', 项目: '章节', 说明: 'OGV 不支持章节', off: ['流控制_章节选项', ''] },
  ],
  mxf: [
    { flag: '流控制_附件选项', 项目: '附件', 说明: 'MXF 不支持附件流', off: ['流控制_附件选项', ''] },
    { flag: '流控制_启用保留其他字幕流', 项目: '字幕', 说明: 'MXF 仅支持专业字幕格式，原样复制大概率报错', off: ['流控制_启用保留其他字幕流', false] },
    { flag: '流控制_章节选项', 项目: '章节', 说明: 'MXF 不支持章节', off: ['流控制_章节选项', ''] },
  ],
  mp3: AUDIO_ONLY, flac: AUDIO_ONLY, wav: AUDIO_ONLY, ogg: AUDIO_ONLY, opus: AUDIO_ONLY, wma: AUDIO_ONLY, ac3: AUDIO_ONLY, ape: AUDIO_ONLY,
  m4a: AUDIO_ONLY.filter(item => item.项目 !== '章节'),
  webm: [
    { flag: '流控制_附件选项', 项目: '附件', 说明: 'WebM 不支持附件流', off: ['流控制_附件选项', ''] },
    { flag: '流控制_启用保留其他字幕流', 项目: '字幕', 说明: 'WebM 仅支持 WebVTT 字幕', off: ['流控制_启用保留其他字幕流', false] },
    { flag: '流控制_启用保留其他音频流', 项目: '多音轨', 说明: 'WebM 音轨仅支持 Opus/Vorbis，AAC 等格式会报错', off: ['流控制_启用保留其他音频流', false] },
  ],
  avi: [
    { flag: '流控制_附件选项', 项目: '附件', 说明: 'AVI 不支持附件流', off: ['流控制_附件选项', ''] },
    { flag: '流控制_启用保留其他字幕流', 项目: '字幕', 说明: 'AVI 字幕支持极差，原样复制大概率报错', off: ['流控制_启用保留其他字幕流', false] },
    { flag: '流控制_章节选项', 项目: '章节', 说明: 'AVI 不支持章节', off: ['流控制_章节选项', ''] },
  ],
  flv: [
    { flag: '流控制_附件选项', 项目: '附件', 说明: 'FLV 不支持附件流', off: ['流控制_附件选项', ''] },
    { flag: '流控制_启用保留其他字幕流', 项目: '字幕', 说明: 'FLV 不支持字幕流', off: ['流控制_启用保留其他字幕流', false] },
    { flag: '流控制_章节选项', 项目: '章节', 说明: 'FLV 不支持章节', off: ['流控制_章节选项', ''] },
  ],
  ts: [
    { flag: '流控制_附件选项', 项目: '附件', 说明: 'TS 不支持附件流', off: ['流控制_附件选项', ''] },
    { flag: '流控制_启用保留其他字幕流', 项目: '字幕', 说明: 'TS 不支持字幕流', off: ['流控制_启用保留其他字幕流', false] },
    { flag: '流控制_章节选项', 项目: '章节', 说明: 'TS 不支持章节', off: ['流控制_章节选项', ''] },
  ],
  m2ts: [
    { flag: '流控制_附件选项', 项目: '附件', 说明: 'M2TS 不支持附件流', off: ['流控制_附件选项', ''] },
    { flag: '流控制_启用保留其他字幕流', 项目: '字幕', 说明: 'M2TS 不支持字幕流', off: ['流控制_启用保留其他字幕流', false] },
  ],
}

const 标志已开启 = (preset: PresetData, flag: string) => {
  const v = preset[flag]
  return v !== undefined && v !== null && v !== '' && v !== false && String(v) !== '未选择' && String(v) !== '0'
}

/** 返回当前容器装不下的「保留项」；容器不受限或 auto（按输入）时返回空 */
export function 检查容器兼容性(preset: PresetData): 容器冲突项[] {
  const container = String(preset['输出容器'] ?? '').trim().replace(/^\./, '').toLowerCase()
  if (container === '') return []
  const limits = CONTAINER_LIMITS[container]
  if (!limits) return []
  return limits.filter(l => 标志已开启(preset, l.flag))
}

// 弹窗（Promise 化）：'mkv' = 改用 MKV 继续；'drop' = 丢弃不兼容项继续；'cancel' = 中止入队
export type 兼容选择 = 'mkv' | 'drop' | 'cancel'
export const useCompatDialog = defineStore('compatDialog', () => {
  const visible = ref(false)
  const 容器 = ref('')
  const 冲突 = ref<容器冲突项[]>([])
  let resolver: ((v: 兼容选择) => void) | null = null
  function open(container: string, conflicts: 容器冲突项[]): Promise<兼容选择> {
    容器.value = container
    冲突.value = conflicts
    visible.value = true
    return new Promise(resolve => { resolver = resolve })
  }
  function choose(v: 兼容选择) {
    visible.value = false
    resolver?.(v)
    resolver = null
  }
  return { visible, 容器, 冲突, open, choose }
})

/** 入队前容器兼容确认：有冲突弹窗，直接改当前预设（store），返回是否继续入队 */
export async function 入队兼容确认(currentPresetStore: { preset: PresetData }): Promise<boolean> {
  const conflicts = 检查容器兼容性(currentPresetStore.preset)
  if (conflicts.length === 0) return true
  const container = String(currentPresetStore.preset['输出容器'] ?? '')
  const choice = await useCompatDialog().open(container, conflicts)
  if (choice === 'cancel') return false
  const target = currentPresetStore.preset as Record<string, unknown>
  if (choice === 'mkv') {
    target['输出容器'] = 'mkv'
    return true
  }
  for (const c of conflicts) {
    const [key, value] = c.off
    target[key] = value
  }
  return true
}
