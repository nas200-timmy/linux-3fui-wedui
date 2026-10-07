// API 客户端：与 3fui-server 的 REST 接口一一对应。
import { useAuth } from './store'

// 复制到剪贴板：navigator.clipboard 在非安全上下文（HTTP 明文/IP 直连）不可用，回落 execCommand
export async function copyToClipboard(text: string): Promise<boolean> {
  try {
    if (navigator.clipboard && window.isSecureContext) {
      await navigator.clipboard.writeText(text)
      return true
    }
  } catch {
    /* 走兜底 */
  }
  try {
    const area = document.createElement('textarea')
    area.value = text
    area.style.cssText = 'position:fixed;left:-9999px;top:0;opacity:0'
    document.body.appendChild(area)
    area.focus()
    area.select()
    const ok = document.execCommand('copy')
    document.body.removeChild(area)
    return ok
  } catch {
    return false
  }
}
export type PresetData = Record<string, unknown> & {
  预设备注?: string
  预设文件版本?: number
  输出容器?: string
}

// 编码器切换记录（QueueTask.切换记录 的元素；无切换时为空数组）
export interface EncoderSwitchRecord {
  原编码器: string
  新编码器: string
  原因: string
  触发方式: string
  换算明细: string[]
  丢弃参数: string[]
  时间: string
}

// WS /ws 推送的编码器切换告知消息
export interface EncoderSwitchMessage {
  type: 'encoder-switch'
  任务ID: string
  任务名称: string
  原编码器: string
  新编码器: string
  原因: string
  触发方式: string
  换算明细: string[]
  丢弃参数: string[]
  时间: string
}

export interface QueueTask {
  ID: string
  任务名称: string
  输入文件: string
  输出文件: string
  状态: string
  状态码: number
  命令行: string
  进度文本: string
  效率文本: string
  输出大小文本: string
  质量文本: string
  比特率文本: string
  时间文本: string
  百分比: number
  实时输出: string
  最新底部日志文本: string
  最新底部日志是否错误: boolean
  媒体总时长: string
  当前进程ID: number
  允许自动启动: boolean
  可移除: boolean
  可重置: boolean
  可排序: boolean
  预设编码器: string
  实际编码器: string
  切换记录: EncoderSwitchRecord[]
}

export interface TlsStatus {
  active: boolean
  port: number
  subject: string | null
  issuer?: string | null
  notBefore?: string | null
  notAfter?: string | null
  certPath?: string | null
  envConfigured?: boolean
}

// /api/perf/history 返回的历史序列（服务端环形缓冲 1 小时，按 maxPoints 抽稀）
export interface PerfHistory {
  intervalMs: number
  points: number
  from: number
  to: number
  cpu: number[]
  cpuUser: number[]
  cpuSystem: number[]
  memory: number[]
  diskRead: number[]
  diskWrite: number[]
  netRx: number[]
  netTx: number[]
  gpuUtil: number[][]
  gpuMem: number[][]
  cores: number[][] | null
}

// models.dev 厂商目录（/api/agent/catalog）：一个厂商的元数据，模型列表另取
export interface CatalogProvider {
  id: string
  name: string
  /** OpenAI 兼容 base URL；models.dev 对少数厂商省略 */
  api: string | null
  doc: string | null
  env: string[] | null
  /** 该厂商在 models.dev 里的模型数量 */
  models: number
}

export interface ProviderCatalog {
  fetchedAt: string
  /** true＝网络不可用，返回的是数据目录里的过期缓存 */
  stale: boolean
  count: number
  providers: CatalogProvider[]
}

export interface CatalogModel {
  id: string
  name: string | null
  reasoning: boolean
  toolCall: boolean
  context: number | null
  costIn: number | null
  costOut: number | null
  releaseDate: string | null
}

export interface ProviderModels {
  id: string
  stale: boolean
  count: number
  models: CatalogModel[]
}

/** /api/agent/scan：端点自身 /models 的扫描结果 */
export interface ModelScanResult {
  /** 真正取到模型列表的 API 基地址（探明前缀时带在末尾） */
  endpoint: string
  prefix: string
  count: number
  models: { id: string; ownedBy: string }[]
}

/** /api/agent/tools 下发的单个工具（已按 Agent权限级别 过滤） */
export interface AgentToolDef {
  name: string
  description: string
  parameters: Record<string, unknown>
  /** server：服务端执行；browser：参数面板/准备文件，状态在浏览器里，由前端执行 */
  scope: 'server' | 'browser'
  level: number
  levelName: string
  write: boolean
}

export interface ToolCatalog {
  permissionLevel: number
  permissionName: string
  count: number
  tools: AgentToolDef[]
}

/**
 * 从任意错误值里取一句可读文案。
 * 上游端点常返回 `{"error":{"message":"…","type":"…"}}`（嵌套对象）——直接塞进 Error 就会变成
 * `[object Object]`（Agent 页踩过），所以统一走这里逐层剥。
 */
export function errorText(error: unknown, maxLength = 600): string {
  const pick = (value: unknown): string => {
    if (value === null || value === undefined) return ''
    if (typeof value === 'string') return value
    if (typeof value === 'number' || typeof value === 'boolean') return String(value)
    if (value instanceof Error) return value.message
    if (typeof value === 'object') {
      const record = value as Record<string, unknown>
      for (const key of ['message', 'error', 'detail', 'title']) {
        const nested = record[key]
        if (typeof nested === 'string' && nested.trim() !== '') return nested
        if (nested && typeof nested === 'object') {
          const deeper = pick(nested)
          if (deeper !== '') return deeper
        }
      }
      try {
        return JSON.stringify(value)
      } catch {
        return ''
      }
    }
    return ''
  }

  const text = pick(error).trim().replace(/\s+/g, ' ')
  if (text === '') return '未知错误'
  return text.length > maxLength ? `${text.slice(0, maxLength)}…` : text
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    headers: { 'Content-Type': 'application/json', ...(init?.headers ?? {}) },
    ...init,
  })
  if (response.status === 401) {
    useAuth().onUnauthorized()
    throw new Error('未登录或会话已过期')
  }
  if (!response.ok) {
    let message = `${response.status} ${response.statusText}`
    try {
      const body = await response.json()
      const detail = errorText(body, 400)
      if (detail !== '未知错误') message = detail
    } catch {
      /* 忽略非 JSON 错误体 */
    }
    throw new Error(message)
  }
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

export const api = {
  status: () => request<Record<string, unknown>>('/api/status'),

  presets: {
    list: () => request<{ user: string[]; community: string[] }>('/api/presets'),
    load: (source: string, name: string) => request<PresetData>(`/api/presets/${source}/${name}`),
    save: (source: string, name: string, preset: PresetData) =>
      request(`/api/presets/${source}/${name}`, { method: 'POST', body: JSON.stringify(preset) }),
    remove: (source: string, name: string) => request(`/api/presets/${source}/${name}`, { method: 'DELETE' }),
    importFile: (file: File) => {
      const form = new FormData()
      form.append('file', file)
      return request<{ name: string }>('/api/presets/import', { method: 'POST', headers: {}, body: form })
    },
    builtin: () => request<{ 名称: string; 数据: PresetData }[]>('/api/builtin-presets'),
    preview: (preset: PresetData, input?: string, output?: string) =>
      request<{ 命令行: string }>('/api/preset/preview', {
        method: 'POST',
        body: JSON.stringify({ preset, input, output }),
      }),
  },

  encoderDb: () => request<Record<string, unknown>>('/api/encoder-db'),

  queue: {
    list: () => request<QueueTask[]>('/api/queue'),
    detail: (id: string) => request<Record<string, unknown>>(`/api/queue/tasks/${id}`),
    addTasks: (files: string[], preset: PresetData, 预设名称?: string) =>
      request<{ ID: string; 任务名称: string }[]>('/api/queue/tasks', { method: 'POST', body: JSON.stringify({ files, preset, 预设名称 }) }),
    addCommandLine: (args: string, name?: string, output?: string, input?: string) =>
      request<{ ID: string }>('/api/queue/commandline', { method: 'POST', body: JSON.stringify({ args, name, output, input }) }),
    action: (action: string, ids: string[]) =>
      request('/api/queue/action', { method: 'POST', body: JSON.stringify({ action, ids }) }),
    reorder: (ids: string[]) => request('/api/queue/reorder', { method: 'POST', body: JSON.stringify({ ids }) }),
    syncPreset: (ids: string[] | null, preset: PresetData) =>
      request('/api/queue/sync-preset', { method: 'POST', body: JSON.stringify({ ids, preset }) }),
  },

  // 编码器切换告知的回应：永久写回预设 / 仅本次任务
  encoderSwitchRespond: (任务ID: string, 选择: 'preset' | 'once') =>
    request<{ ok: boolean; 已写回预设?: boolean; 消息?: string }>('/api/encoder-switch/respond', {
      method: 'POST',
      body: JSON.stringify({ 任务ID, 选择 }),
    }),

  probe: {
    info: (path: string) => request<Record<string, unknown>>(`/api/probe?path=${encodeURIComponent(path)}`),
    browse: (path: string) =>
      request<{ path: string; parent: string | null; root: string | null; entries: { name: string; path: string; isDirectory: boolean; isMedia: boolean }[] }>(
        `/api/browse?path=${encodeURIComponent(path)}`,
      ),
  },

  settings: {
    get: () => request<Record<string, unknown>>('/api/settings'),
    put: (settings: Record<string, unknown>) => request('/api/settings', { method: 'PUT', body: JSON.stringify(settings) }),
  },

  tls: {
    status: () => request<TlsStatus>('/api/tls'),
    installPem: (certPem: string, keyPem: string) =>
      request<{ ok: boolean; port: number; subject?: string }>('/api/tls', { method: 'POST', body: JSON.stringify({ certPem, keyPem }) }),
    installFiles: (cert: File, key: File) => {
      const form = new FormData()
      form.append('cert', cert)
      form.append('key', key)
      return request<{ ok: boolean; port: number; subject?: string }>('/api/tls', { method: 'POST', headers: {}, body: form })
    },
    remove: () => request('/api/tls', { method: 'DELETE' }),
  },

  agent: {
    config: () => request<{ endpoint: string; hasApiKey: boolean; model: string; reasoningEffort: string; extraHeaders: string; permissionLevel: number; permissionName: string; onlineMode: number }>('/api/agent/config'),
    saveConfig: (cfg: Record<string, unknown>) => request('/api/agent/config', { method: 'PUT', body: JSON.stringify(cfg) }),
    // 工具目录（已按权限级别过滤）与 server 范围工具的执行入口
    tools: () => request<ToolCatalog>('/api/agent/tools'),
    runTool: (name: string, args: Record<string, unknown>) =>
      request<{ ok: boolean; result: string }>(`/api/agent/tools/${encodeURIComponent(name)}`, {
        method: 'POST',
        body: JSON.stringify(args),
      }),
    // models.dev 厂商目录：refresh=true 绕过 24h 缓存强制重拉
    catalog: (refresh = false) => request<ProviderCatalog>(`/api/agent/catalog${refresh ? '?refresh=1' : ''}`),
    catalogModels: (providerId: string) => request<ProviderModels>(`/api/agent/catalog/${encodeURIComponent(providerId)}`),
    // 从端点自身拉模型列表；不传字段即用服务端已保存的端点与密钥
    scanModels: (payload: { endpoint?: string; apiKey?: string } = {}) =>
      request<ModelScanResult>('/api/agent/scan', { method: 'POST', body: JSON.stringify(payload) }),
    chat: (payload: { messages: unknown[]; model?: string; tools?: unknown[]; toolChoice?: string }, signal?: AbortSignal) =>
      fetch('/api/agent/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          messages: payload.messages,
          model: payload.model,
          tools: payload.tools,
          tool_choice: payload.toolChoice,
          stream: true,
        }),
        signal,
      }),
  },

  perf: () => request<Record<string, unknown>>('/api/perf'),
  perfHistory: (params: { minutes?: number; maxPoints?: number; cores?: number }) => {
    const query = new URLSearchParams()
    if (params.minutes !== undefined) query.set('minutes', String(params.minutes))
    if (params.maxPoints !== undefined) query.set('maxPoints', String(params.maxPoints))
    if (params.cores !== undefined) query.set('cores', String(params.cores))
    const suffix = query.toString() !== '' ? `?${query.toString()}` : ''
    return request<PerfHistory>(`/api/perf/history${suffix}`)
  },

  auth: {
    status: () => request<{ enabled: boolean; authenticated: boolean }>('/api/auth/status'),
    login: (username: string, password: string) =>
      request<{ ok: boolean }>('/api/auth/login', { method: 'POST', body: JSON.stringify({ username, password }) }),
    logout: () => request('/api/auth/logout', { method: 'POST' }),
    configure: (enable: boolean, username?: string, password?: string) =>
      request('/api/auth/config', { method: 'POST', body: JSON.stringify({ enable, username, password }) }),
  },
}

export function openQueueWebSocket(onMessage: (data: Record<string, unknown>) => void, onStatus?: (connected: boolean) => void): WebSocket {
  const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:'
  const socket = new WebSocket(`${protocol}//${location.host}/ws`)
  socket.onmessage = (event) => {
    try {
      onMessage(JSON.parse(event.data))
    } catch {
      /* 忽略无法解析的消息 */
    }
  }
  socket.onopen = () => onStatus?.(true)
  socket.onclose = () => onStatus?.(false)
  return socket
}
