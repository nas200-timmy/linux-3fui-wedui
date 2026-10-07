<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { api, type AgentToolDef, type CatalogModel, type CatalogProvider, type PresetData } from '../api'
import { useCurrentPreset, usePendingFiles, useToast } from '../store'
import { marked } from '../markdown'
import ModernComboBox from '../components/ModernComboBox.vue'
import ProviderPickerDialog from '../components/ProviderPickerDialog.vue'
import ToolCallCard from '../components/ToolCallCard.vue'
import ToolConfirmCard from '../components/ToolConfirmCard.vue'
import { providerBaseUrl, providerDocUrl } from '../providers'
import {
  checkBeforeEnqueue,
  createToolCallAccumulator,
  runBrowserTool,
  toOpenAiTools,
  ToolDenied,
  type BrowserToolContext,
  type ToolCallView,
} from '../agentTools'

interface Message {
  role: 'user' | 'assistant' | 'tool'
  content: string
  /** assistant 请求的工具调用（带执行结果，用于渲染工具卡片） */
  tool_calls?: ToolCallView[]
  /** role='tool' 时回灌给上游的调用 ID 与工具名 */
  tool_call_id?: string
  name?: string
}
interface Conversation { id: string; title: string; time: string; messages: Message[] }

const STORAGE_KEY = 'linux-3fui-agent-conversations'

const 选项 = (values: string[]) => values.map(v => ({ value: v, label: v }))
const ONLINE_MODES = 选项(['本地联网', '端点联网', '禁用联网'])
// 权限级别沿用上游三档（设置_v6.Agent权限级别）；档位 2 的文件与命令工具尚未移植
const PERMISSION_LEVELS = [
  { value: '0', label: '安全区域' },
  { value: '1', label: '环境控制' },
  { value: '2', label: '系统访问' },
]
const REASONING_LEVELS = [{ value: '', label: '默认' }, ...选项(['low', 'medium', 'high'])]
/** 单轮对话里最多允许几轮工具调用（上游没有上限，这里加个保险） */
const MAX_ROUNDS = 12
/** 回灌给模型的单个工具结果最多多少字符（对齐上游 Form_v6_Agent_运行.vb:287 的 16000） */
const TOOL_RESULT_LIMIT = 16000

const toast = useToast()
const currentPresetStore = useCurrentPreset()
const pendingFilesStore = usePendingFiles()
const conversations = ref<Conversation[]>([])
const activeId = ref('')
const input = ref('')
const endpoint = ref('')
const model = ref('')
const hasApiKey = ref(false)
const reasoningEffort = ref('')
const onlineMode = ref('本地联网')
const permissionLevel = ref('0')
const permissionName = ref('安全区域')
const toolCatalog = ref<AgentToolDef[]>([])
const currentRound = ref(0)
const streaming = ref(false)
const abort = ref<AbortController | null>(null)
const showConfig = ref(false)
const showTips = ref(false)

/** 写操作确认卡（挂起时循环停在这里等用户点） */
const pendingConfirm = ref<{ tool: AgentToolDef; args: Record<string, unknown>; settle: (ok: boolean) => void } | null>(null)
/** loadConfig 期间不要触发保存（否则开页面就会回写设置） */
let applyingConfig = false

const SYSTEM_PROMPT =
  '你是 linux-3fui 的智能副驾驶。linux-3fui 是 FFmpegFreeUI（3FUI）的 Linux/网页版，一个面向进阶用户的 FFmpeg 交互外壳。' +
  '你熟悉视频压制、x264/x265/AV1、NVENC/QSV/VAAPI 硬件编码、滤镜（scale/crop/yadif/deband/subtitles 等）、色彩管理（HDR/SDR 转换）、批量转码流程。' +
  '请用简体中文，简明专业地回答编码相关问题。'

const active = computed(() => conversations.value.find(c => c.id === activeId.value))
const messages = computed<Message[]>(() => active.value?.messages ?? [])
const tokenCount = computed(() => Math.round(messages.value.reduce((sum, m) => sum + m.content.length, 0) / 2))

// ── token 预算（原版格式：百分比 | 已用 / 预算）──
const TOKEN_BUDGET = 200000
const tokenPercent = computed(() => Math.min(100, Math.round((tokenCount.value / TOKEN_BUDGET) * 100)))

// ── 模型管理：models.dev 厂商目录 + 端点实时扫描 ──
// 上游（Lake1059/FFmpegFreeUI）的厂商清单来自赞助者专用的远端 sp-agent-endpoints.json，公开版拿不到；
// 这里改用 models.dev 目录补厂商/base URL，模型候选仍以上游的「端点实时拉取」为主（标记「端点实时」）。
const MODEL_HISTORY_KEY = 'linux-3fui-agent-models'
const MODEL_PRESETS = ['deepseek-chat', 'kimi-k2-0905-preview', 'gpt-4o-mini', 'qwen-plus']
const PROVIDER_KEY = 'linux-3fui-agent-provider'
const CATALOG_KEY = 'linux-3fui-agent-catalog'
const CATALOG_TTL = 24 * 3600 * 1000

const modelHistory = ref<string[]>([])
const catalog = ref<CatalogProvider[]>([])
const catalogFetchedAt = ref('')
const catalogStale = ref(false)
const catalogLoading = ref(false)
const providerId = ref('')
const providerModels = ref<CatalogModel[]>([])
const scannedModelIds = ref<string[]>([])
const scanLoading = ref(false)
const scanMessage = ref('')
const showProviderPicker = ref(false)
const apiKeyInput = ref('')
const apiKeySaving = ref(false)
const confirmClearKey = ref(false)

const selectedProvider = computed(() => catalog.value.find(item => item.id === providerId.value) ?? null)
const providerLabel = computed(() => selectedProvider.value?.name ?? '')
const providerBase = computed(() => (selectedProvider.value === null ? '' : providerBaseUrl(selectedProvider.value)))
const providerDoc = computed(() => (selectedProvider.value === null ? '' : providerDocUrl(selectedProvider.value)))
const activeModel = computed(() => providerModels.value.find(item => item.id === model.value.trim()) ?? null)

function describeModel(item: CatalogModel): string {
  const parts: string[] = []
  if (item.context) parts.push(`${Math.round(item.context / 1000)}K 上下文`)
  if (item.costIn !== null && item.costOut !== null) parts.push(`$${item.costIn}/$${item.costOut} 每百万 token`)
  if (item.reasoning) parts.push('支持推理级别')
  return parts.join(' · ')
}

// 候选顺序对齐上游「模型来自端点、自定义补充」：端点实时 → 目录 → 用过 → 常用
const modelOptions = computed(() => {
  const options: { value: string; label: string; hint: string }[] = []
  const seen = new Set<string>()
  const add = (raw: string, source: string, detail = '') => {
    const value = raw.trim()
    if (value === '' || seen.has(value.toLowerCase())) return
    seen.add(value.toLowerCase())
    options.push({ value, label: value, hint: detail === '' ? source : `${source} · ${detail}` })
  }
  for (const id of scannedModelIds.value) add(id, '端点实时')
  for (const item of providerModels.value) add(item.id, '模型目录', describeModel(item))
  for (const id of modelHistory.value) add(id, '用过')
  for (const id of MODEL_PRESETS) add(id, '常用')
  return options
})

const modelSourceText = computed(() => {
  const parts: string[] = []
  if (scannedModelIds.value.length > 0) parts.push(`端点实时 ${scannedModelIds.value.length}`)
  if (providerModels.value.length > 0) parts.push(`${providerLabel.value || '目录'} ${providerModels.value.length}`)
  if (modelHistory.value.length > 0) parts.push(`用过 ${modelHistory.value.length}`)
  return parts.join(' · ')
})

function loadModelHistory() {
  try {
    modelHistory.value = JSON.parse(localStorage.getItem(MODEL_HISTORY_KEY) ?? '[]') as string[]
  } catch {
    modelHistory.value = []
  }
}

function rememberModel() {
  const m = model.value.trim()
  if (!m || modelHistory.value.includes(m)) return
  modelHistory.value = [m, ...modelHistory.value].slice(0, 12)
  try {
    localStorage.setItem(MODEL_HISTORY_KEY, JSON.stringify(modelHistory.value))
  } catch { /* 本地存储不可用时忽略 */ }
}

// ── models.dev 厂商目录（服务端拉取 + 24h 缓存；浏览器再缓存一份，避免每次进页面都请求）──
function readCachedCatalog(): { providers: CatalogProvider[]; fetchedAt: string; stale: boolean } | null {
  try {
    const raw = localStorage.getItem(CATALOG_KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw) as { providers?: CatalogProvider[]; fetchedAt?: string; stale?: boolean }
    if (!Array.isArray(parsed.providers) || parsed.providers.length === 0) return null
    const fetchedAt = parsed.fetchedAt ?? ''
    const time = new Date(fetchedAt).getTime()
    if (Number.isNaN(time) || Date.now() - time > CATALOG_TTL) return null
    return { providers: parsed.providers, fetchedAt, stale: parsed.stale === true }
  } catch {
    return null
  }
}

async function loadCatalog(force = false) {
  if (!force) {
    const cached = readCachedCatalog()
    if (cached) {
      catalog.value = cached.providers
      catalogFetchedAt.value = cached.fetchedAt
      catalogStale.value = cached.stale
      inferProviderFromEndpoint()
      return
    }
  }
  catalogLoading.value = true
  try {
    const data = await api.agent.catalog(force)
    catalog.value = data.providers ?? []
    catalogFetchedAt.value = data.fetchedAt
    catalogStale.value = data.stale
    try {
      localStorage.setItem(CATALOG_KEY, JSON.stringify({ providers: catalog.value, fetchedAt: data.fetchedAt, stale: data.stale }))
    } catch { /* 本地存储不可用时忽略 */ }
    inferProviderFromEndpoint()
    toast.push('ok', force ? `厂商目录已刷新（${data.count} 家）` : `厂商目录已就绪（${data.count} 家）`)
  } catch (error) {
    toast.push('err', `厂商目录加载失败：${error instanceof Error ? error.message : String(error)}`)
  } finally {
    catalogLoading.value = false
  }
}

async function loadProviderModels(id: string) {
  try {
    const data = await api.agent.catalogModels(id)
    providerModels.value = data.models ?? []
  } catch {
    providerModels.value = []
  }
}

function pickProvider(provider: CatalogProvider) {
  showProviderPicker.value = false
  providerId.value = provider.id
  providerModels.value = []
  try {
    localStorage.setItem(PROVIDER_KEY, provider.id)
  } catch { /* 本地存储不可用时忽略 */ }
  const base = providerBaseUrl(provider)
  if (base === '') {
    toast.push('err', `${provider.name} 没有公开的兼容地址，请手填端点地址`)
  } else {
    endpoint.value = base
    saveConfig(true)
  }
  void loadProviderModels(provider.id)
}

/** 没选过厂商时，按已保存的端点反查厂商（含 /v1 与不带 /v1 两种写法）。 */
function inferProviderFromEndpoint() {
  if (providerId.value !== '' || catalog.value.length === 0) return
  const endpointValue = endpoint.value.trim()
  if (endpointValue === '') return
  const normalize = (value: string) => value.replace(/\/+$/, '').toLowerCase()
  const target = normalize(endpointValue)
  const same = (base: string) => {
    const known = normalize(base)
    return known !== '' && (known === target || `${known}/v1` === target || `${target}/v1` === known)
  }
  const hit = catalog.value.find(provider => same(providerBaseUrl(provider)))
  if (!hit) return
  providerId.value = hit.id
  void loadProviderModels(hit.id)
}

function restoreProvider() {
  try {
    const saved = localStorage.getItem(PROVIDER_KEY) ?? ''
    if (saved === '') return
    providerId.value = saved
    void loadProviderModels(saved)
  } catch { /* 本地存储不可用时忽略 */ }
}

/** 从端点自身拉模型列表（上游 TryGetModelsAsync 的对应实现，密钥只在服务端使用）。 */
async function scanEndpointModels() {
  scanLoading.value = true
  scanMessage.value = ''
  try {
    const endpointValue = endpoint.value.trim()
    const keyValue = apiKeyInput.value.trim()
    const data = await api.agent.scanModels({
      endpoint: endpointValue === '' ? undefined : endpointValue,
      apiKey: keyValue === '' ? undefined : keyValue,
    })
    scannedModelIds.value = data.models.map(item => item.id)
    if (data.count === 0) {
      scanMessage.value = '端点返回 0 个模型：这个 Key 下可能没有可用模型'
    } else {
      const first = data.models[0]
      scanMessage.value = `端点返回 ${data.count} 个模型${data.prefix === '' ? '' : `（API 前缀 ${data.prefix}）`}`
      if (model.value.trim() === '' && first) model.value = first.id
    }
    toast.push('ok', scanMessage.value)
  } catch (error) {
    scanMessage.value = error instanceof Error ? error.message : String(error)
    toast.push('err', scanMessage.value)
  } finally {
    scanLoading.value = false
  }
}

async function saveApiKey() {
  const key = apiKeyInput.value.trim()
  if (key === '') {
    toast.push('err', '请先填入 API Key')
    return
  }
  apiKeySaving.value = true
  try {
    await api.agent.saveConfig({ apiKey: key })
    apiKeyInput.value = ''
    hasApiKey.value = true
    confirmClearKey.value = false
    toast.push('ok', 'API Key 已保存到服务端（网页不回显明文）')
  } catch (error) {
    toast.push('err', error instanceof Error ? error.message : String(error))
  } finally {
    apiKeySaving.value = false
  }
}

function clearApiKey() {
  if (!confirmClearKey.value) {
    confirmClearKey.value = true
    window.setTimeout(() => { confirmClearKey.value = false }, 4000)
    return
  }
  apiKeySaving.value = true
  api.agent.saveConfig({ apiKey: '' })
    .then(() => {
      hasApiKey.value = false
      confirmClearKey.value = false
      toast.push('ok', 'API Key 已清除')
    })
    .catch(error => toast.push('err', error instanceof Error ? error.message : String(error)))
    .finally(() => { apiKeySaving.value = false })
}

// ── 向 AI 发送文件（类）：文本读内容，其他附文件名/路径摘要，随下一条消息发出（服务端零改动）──
interface Attachment { name: string; text: string }
const attachments = ref<Attachment[]>([])
const attachInput = ref<HTMLInputElement | null>(null)
const pending = usePendingFiles()
const pendingPick = ref('')
const TEXT_EXTENSIONS = ['.json', '.txt', '.3fui', '.log', '.md', '.vb', '.srt', '.ass', '.ssa', '.ffmetadata', '.csv', '.xml']
function fileExt(name: string) {
  const i = name.lastIndexOf('.')
  return i < 0 ? '' : name.slice(i).toLowerCase()
}
async function addLocalFiles(event: Event) {
  const input = event.target as HTMLInputElement
  const list = [...(input.files ?? [])]
  input.value = ''
  for (const file of list) {
    if (!TEXT_EXTENSIONS.includes(fileExt(file.name)) || file.size > 512 * 1024) {
      const reason = file.size > 512 * 1024 ? '文件超过 512KB，未读取内容' : '非文本文件，未读取内容'
      attachments.value = [...attachments.value, { name: file.name, text: `（${reason}，大小 ${(file.size / 1024).toFixed(1)} KB）` }]
      continue
    }
    attachments.value = [...attachments.value, { name: file.name, text: await file.text() }]
  }
}
function removeAttachment(index: number) {
  attachments.value = attachments.value.filter((_, i) => i !== index)
}
watch(pendingPick, async path => {
  if (!path) return
  pendingPick.value = ''
  let summary = ''
  try {
    const data = await api.probe.info(path) as unknown as { streams?: { codec_type?: string }[]; format?: { duration?: string; format_name?: string } }
    const streams = data.streams ?? []
    const count = (t: string) => streams.filter(s => s.codec_type === t).length
    summary = `容器：${data.format?.format_name ?? '未知'}，时长：${data.format?.duration ?? '未知'}，视频 ${count('video')} / 音频 ${count('audio')} / 字幕 ${count('subtitle')}`
  } catch {
    summary = '（探测失败，仅附路径）'
  }
  attachments.value = [...attachments.value, { name: path, text: `服务器文件路径：${path}\n${summary}` }]
})

function deleteConversationById(id: string) {
  conversations.value = conversations.value.filter(c => c.id !== id)
  if (activeId.value === id) {
    activeId.value = conversations.value[0]?.id ?? ''
    if (!activeId.value) newConversation()
  }
  persist()
}

function persist() {
  try { localStorage.setItem(STORAGE_KEY, JSON.stringify(conversations.value)) } catch { /* 本地存储不可用时忽略 */ }
}

function load() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw) conversations.value = JSON.parse(raw) as Conversation[]
  } catch { conversations.value = [] }
  if (conversations.value.length === 0) newConversation()
  else activeId.value = conversations.value[0].id
}

function newConversation() {
  const now = new Date()
  const conv: Conversation = {
    id: Math.random().toString(16).slice(2, 10),
    title: `新对话 ${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')} ${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}`,
    time: now.toLocaleString(),
    messages: [],
  }
  conversations.value.unshift(conv)
  activeId.value = conv.id
  persist()
}

function deleteConversation() {
  if (!activeId.value) return
  conversations.value = conversations.value.filter(c => c.id !== activeId.value)
  activeId.value = conversations.value[0]?.id ?? ''
  if (!activeId.value) newConversation()
  persist()
}

async function loadConfig() {
  applyingConfig = true
  try {
    const config = await api.agent.config()
    endpoint.value = config.endpoint
    model.value = config.model
    hasApiKey.value = config.hasApiKey
    reasoningEffort.value = config.reasoningEffort
    permissionLevel.value = String(config.permissionLevel ?? 0)
    permissionName.value = config.permissionName ?? '安全区域'
    if (providerId.value === '' && config.endpoint !== '') inferProviderFromEndpoint()
  } catch { /* 配置可能尚未初始化 */ } finally {
    applyingConfig = false
  }
  await loadTools()
}

/** 拉取按当前权限级别过滤后的工具目录（失败就没有工具，聊天照常） */
async function loadTools() {
  applyingConfig = true
  try {
    const data = await api.agent.tools()
    toolCatalog.value = data.tools
    permissionLevel.value = String(data.permissionLevel)
    permissionName.value = data.permissionName
  } catch {
    toolCatalog.value = []
  } finally {
    applyingConfig = false
  }
}

watch(permissionLevel, async (next, previous) => {
  if (applyingConfig || next === previous) return
  try {
    await api.agent.saveConfig({ permissionLevel: Number(next) })
    await loadTools()
    toast.push('ok', `权限级别已切到「${permissionName.value}」，可用工具 ${toolCatalog.value.length} 个`)
  } catch (error) {
    toast.push('err', error instanceof Error ? error.message : String(error))
  }
})

function saveConfig(silent = false) {
  api.agent.saveConfig({ endpoint: endpoint.value, model: model.value, reasoningEffort: reasoningEffort.value })
    .then(() => { if (!silent) toast.push('ok', 'Agent 配置已保存') })
    .catch(error => toast.push('err', error instanceof Error ? error.message : String(error)))
}

// ── 写操作确认（挂起循环等用户点；停止时按拒绝处理）──
function confirmWrite(tool: AgentToolDef, args: Record<string, unknown>): Promise<boolean> {
  return new Promise<boolean>(resolve => {
    pendingConfirm.value = {
      tool,
      args,
      settle: (ok: boolean) => {
        pendingConfirm.value = null
        resolve(ok)
      },
    }
  })
}

function settleConfirm(ok: boolean) {
  pendingConfirm.value?.settle(ok)
}

/** 给浏览器侧工具的执行上下文（参数面板与准备文件都在 store 里） */
function browserToolContext(): BrowserToolContext {
  return {
    getPreset: () => currentPresetStore.preset as PresetData,
    setPreset: next => { currentPresetStore.preset = next },
    replacePreset: (data, name) => currentPresetStore.replace(data, name),
    getPendingFiles: () => [...pendingFilesStore.files],
    setPendingFiles: paths => { pendingFilesStore.files = paths },
    enqueuePreparedFiles: async () => {
      const preset = currentPresetStore.preset as PresetData
      const files = [...pendingFilesStore.files]
      // 与界面同款预检；容器装不下时不弹兼容对话框，而是把问题交回模型
      const problem = checkBeforeEnqueue(preset, files)
      if (problem) throw new Error(problem)
      const tasks = await api.queue.addTasks(files, preset, currentPresetStore.saveName || undefined)
      pendingFilesStore.clear()
      return JSON.stringify({ ok: true, 已入队: tasks.length, 任务: tasks.map(task => ({ ID: task.ID, 任务名称: task.任务名称 })) })
    },
  }
}

/** 组装发给上游的消息：assistant 带 tool_calls 时 content 置 null，工具结果用 role='tool' + tool_call_id。 */
function buildOutgoingMessages(conv: Conversation): Record<string, unknown>[] {
  const messages: Record<string, unknown>[] = [
    {
      role: 'system',
      content: `${SYSTEM_PROMPT}\n联网设置：${onlineMode.value}；权限级别：${permissionName.value}（当前可用工具 ${toolCatalog.value.length} 个）；推理级别：${reasoningEffort.value || '默认'}。\n需要读参数面板、改参数、看队列或控制任务时**用工具**去拿真实状态，不要凭猜。`,
    },
  ]
  for (const message of conv.messages) {
    if (message.role === 'tool') {
      messages.push({ role: 'tool', tool_call_id: message.tool_call_id, name: message.name, content: message.content })
      continue
    }
    if (message.role === 'assistant' && message.tool_calls && message.tool_calls.length > 0) {
      messages.push({
        role: 'assistant',
        content: message.content || null,
        tool_calls: message.tool_calls.map(call => ({
          id: call.id,
          type: 'function',
          function: { name: call.name, arguments: call.arguments },
        })),
      })
      continue
    }
    if (message.role === 'assistant' && message.content === '') continue
    messages.push({ role: message.role, content: message.content })
  }
  return messages
}

function parseToolArguments(raw: string): Record<string, unknown> {
  try {
    const parsed = JSON.parse(raw || '{}') as unknown
    return typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed) ? parsed as Record<string, unknown> : {}
  } catch {
    return {}
  }
}

/** 读一轮 SSE：正文累加到 assistant.content，tool_calls 交给分片累加器 */
async function readStream(response: Response, assistant: Message): Promise<ToolCallView[]> {
  if (!response.body) throw new Error('响应无内容流')
  const accumulator = createToolCallAccumulator()
  const reader = response.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''
  for (;;) {
    const { done, value } = await reader.read()
    if (done) break
    buffer += decoder.decode(value, { stream: true })
    const lines = buffer.split('\n')
    buffer = lines.pop() ?? ''
    for (const line of lines) {
      const trimmed = line.trim()
      if (!trimmed.startsWith('data:')) continue
      const data = trimmed.slice(5).trim()
      if (data === '[DONE]') continue
      try {
        const chunk = JSON.parse(data) as { choices?: { delta?: { content?: string; tool_calls?: unknown }; message?: { content?: string } }[] }
        const choice = chunk.choices?.[0]
        const piece = choice?.delta?.content ?? choice?.message?.content
        if (typeof piece === 'string' && piece !== '') assistant.content += piece
        if (choice?.delta?.tool_calls) accumulator.push(choice.delta.tool_calls)
      } catch { /* 跳过无法解析的 SSE 行 */ }
    }
  }
  return accumulator.result()
}

/** 一轮工具调用的执行（写操作先过确认卡；未授权/被拒/失败都作为工具结果回灌） */
async function executeToolCalls(conv: Conversation, calls: ToolCallView[], signal: AbortSignal) {
  const context = browserToolContext()
  for (const call of calls) {
    const started = Date.now()
    const tool = toolCatalog.value.find(item => item.name === call.name)
    let text = ''
    let ok = true
    if (!tool) {
      ok = false
      call.unauthorized = true
      text = `未授权的工具：${call.name}。当前权限级别「${permissionName.value}」的目录里没有它，已拒绝执行。`
    } else {
      try {
        // 写操作一律先过确认卡（服务端工具与浏览器工具都要；上游只靠提示词，这里是硬确认）
        if (tool.write) {
          const allowed = await confirmWrite(tool, parseToolArguments(call.arguments))
          if (!allowed) throw new ToolDenied()
        }
        if (tool.scope === 'server') {
          const outcome = await api.agent.runTool(call.name, parseToolArguments(call.arguments))
          ok = outcome.ok
          text = outcome.result
        } else {
          text = await runBrowserTool(call, tool, context)
        }
      } catch (error) {
        ok = false
        if (error instanceof ToolDenied) {
          call.denied = true
          text = '用户拒绝了该操作（没有执行）。请换一个用户能接受的做法，或先说明为什么需要它。'
        } else if ((error as Error).name === 'AbortError' || signal.aborted) {
          call.denied = true
          text = '已取消：用户中止了本轮。'
        } else {
          text = `工具执行失败：${error instanceof Error ? error.message : String(error)}`
        }
      }
    }
    call.ok = ok
    call.ms = Date.now() - started
    call.result = text.length > TOOL_RESULT_LIMIT ? `${text.slice(0, TOOL_RESULT_LIMIT)}…（已截断，原文 ${text.length} 字符）` : text
    conv.messages.push({ role: 'tool', content: call.result, tool_call_id: call.id, name: call.name })
    persist()
  }
}

async function send() {
  const text = input.value.trim()
  const attachmentBlock = attachments.value.map(a => `【附件：${a.name}】\n${a.text}`).join('\n\n')
  if ((!text && !attachmentBlock) || streaming.value) return
  if (!endpoint.value.trim()) {
    showConfig.value = true
    return toast.push('err', '请先配置 Agent 端点（兼容 OpenAI SDK 的 API 地址）')
  }
  rememberModel()
  const conv = active.value
  if (!conv) return
  const content = attachmentBlock ? (text ? `${attachmentBlock}\n\n${text}` : attachmentBlock) : text
  conv.messages.push({ role: 'user', content })
  attachments.value = []
  input.value = ''
  streaming.value = true
  currentRound.value = 0
  const controller = new AbortController()
  abort.value = controller

  try {
    for (let round = 1; round <= MAX_ROUNDS; round++) {
      currentRound.value = round
      const hasTools = toolCatalog.value.length > 0
      const response = await api.agent.chat({
        messages: buildOutgoingMessages(conv),
        model: model.value || undefined,
        tools: hasTools ? toOpenAiTools(toolCatalog.value) : undefined,
        toolChoice: hasTools ? 'auto' : undefined,
      }, controller.signal)
      if (!response.ok) {
        const body = await response.json().catch(() => ({ error: response.statusText }))
        throw new Error((body as { error?: string }).error ?? `HTTP ${response.status}`)
      }
      const assistant: Message = { role: 'assistant', content: '' }
      conv.messages.push(assistant)
      const calls = await readStream(response, assistant)
      if (calls.length === 0) { persist(); return }
      assistant.tool_calls = calls
      persist()
      await executeToolCalls(conv, calls, controller.signal)
    }
    active.value?.messages.push({
      role: 'assistant',
      content: `（本轮已经连续调用工具 ${MAX_ROUNDS} 次，先停在这里。可以继续追问，或直接看上面的工具调用结果。）`,
    })
  } catch (error) {
    if ((error as Error).name !== 'AbortError') {
      // 写回发起请求的会话：流式期间用户可能已切换到其他对话
      const last = conv.messages[conv.messages.length - 1]
      if (last && last.role === 'assistant') last.content = last.content || `请求失败：${(error as Error).message}`
      else conv.messages.push({ role: 'assistant', content: `请求失败：${(error as Error).message}` })
    }
  } finally {
    streaming.value = false
    abort.value = null
    currentRound.value = 0
    settleConfirm(false)
    persist()
  }
}

function stop() {
  abort.value?.abort()
  // 挂起的写操作确认也要收尾，否则循环会一直等在那里
  settleConfirm(false)
}

onMounted(() => {
  load()
  void loadConfig()
  loadModelHistory()
  restoreProvider()
  void loadCatalog()
})
</script>

<template>
  <div class="agent-layout">
    <!-- 左列：对话列表 -->
    <div class="agent-col agent-list">
      <div class="panel-box panel" style="flex: 1; display: flex; flex-direction: column; min-height: 0">
        <div class="panel-title">Agent 对话列表</div>
        <div style="flex: 1; overflow: auto; min-height: 0">
          <div
            v-for="conv in conversations"
            :key="conv.id"
            class="agent-item"
            :class="{ active: conv.id === activeId }"
            :title="conv.title"
            @click="activeId = conv.id"
          >
            <span class="agent-item-title">{{ conv.title }}</span>
            <button class="agent-item-del" title="删除此对话" @click.stop="deleteConversationById(conv.id)">✕</button>
          </div>
        </div>
        <div class="flex" style="margin-top: 8px">
          <button class="small plain txt-green" @click="newConversation">新建对话</button>
          <button class="small plain txt-red" @click="deleteConversation">删除对话</button>
        </div>
        <div class="flex" style="margin-top: 4px">
          <button class="small" @click="showConfig = !showConfig">重载连接</button>
          <button class="small" @click="showTips = !showTips">操作提示</button>
        </div>
        <div class="muted mono" style="margin-top: 8px">{{ tokenPercent }}% | {{ tokenCount }} / {{ TOKEN_BUDGET }}</div>
      </div>

      <!-- 向 AI 发送文件(类)（原版左栏第二个框） -->
      <div class="panel-box panel" style="margin-top: 10px">
        <div class="panel-title">向 AI 发送文件(类)</div>
        <div v-if="attachments.length" class="attach-list">
          <div v-for="(item, i) in attachments" :key="i" class="attach-chip">
            <span class="attach-name" :title="item.name">{{ item.name }}</span>
            <button class="small plain" @click="removeAttachment(i)">✕</button>
          </div>
        </div>
        <div class="flex" style="margin-top: 6px; flex-wrap: wrap">
          <button class="small" @click="attachInput?.click()">添加本地文件</button>
          <input ref="attachInput" type="file" multiple style="display: none" @change="addLocalFiles" />
          <ModernComboBox
            v-if="pending.files.length"
            v-model="pendingPick"
            :options="pending.files.map(p => ({ value: p, label: p.split('/').pop() ?? p }))"
            watermark="从待处理添加"
            :width="180"
            :max-items="8"
          />
        </div>
        <div class="muted" style="margin-top: 6px; font-size: 12px">文本类文件读取内容，其他只附文件名；内容随下一条消息一起发送。</div>
      </div>

      <div v-if="showConfig" class="panel-box panel" style="margin-top: 10px">
        <div class="panel-title">模型管理</div>

        <div class="field-item">
          <div class="field-label">厂商</div>
          <div class="field-control flex" style="gap: 6px; flex-wrap: wrap">
            <button class="small" @click="showProviderPicker = true">{{ selectedProvider === null ? '选择厂商' : '更换厂商' }}</button>
            <span class="muted" style="font-size: 12px">
              {{ selectedProvider === null ? (catalog.length > 0 ? `目录收录 ${catalog.length} 家，国内知名优先` : '目录加载中…') : providerLabel }}
            </span>
            <button class="small plain" :disabled="catalogLoading" @click="loadCatalog(true)">{{ catalogLoading ? '刷新中…' : '刷新目录' }}</button>
          </div>
        </div>

        <div class="field-item" style="margin-top: 6px">
          <div class="field-label">端点地址</div>
          <div class="field-control">
            <input v-model="endpoint" type="text" placeholder="如 https://api.deepseek.com/v1" />
            <div class="muted" style="font-size: 12px; margin-top: 4px; line-height: 1.6">
              <template v-if="providerBase">已按「{{ providerLabel }}」自动填写。本地 Ollama / LM Studio 改成 http://127.0.0.1:11434/v1 这类地址即可。</template>
              <template v-else>选厂商会自动填端点；也可以直接手填任意 OpenAI 兼容地址。</template>
              <a v-if="providerDoc" class="agent-doc-link" :href="providerDoc" target="_blank" rel="noopener noreferrer">文档 / 申请密钥</a>
            </div>
          </div>
        </div>

        <div class="field-item" style="margin-top: 6px">
          <div class="field-label">API Key</div>
          <div class="field-control flex" style="gap: 6px; flex-wrap: wrap">
            <input
              v-model="apiKeyInput"
              type="password"
              autocomplete="off"
              style="flex: 1; min-width: 150px"
              :placeholder="hasApiKey ? '已配置，留空不修改' : '粘贴该厂商的 API Key'"
            />
            <button class="small primary" :disabled="apiKeySaving" @click="saveApiKey">保存密钥</button>
            <button v-if="hasApiKey" class="small danger" :disabled="apiKeySaving" @click="clearApiKey">
              {{ confirmClearKey ? '确认清除？' : '清除密钥' }}
            </button>
          </div>
          <div class="muted" style="font-size: 12px; margin-top: 4px">
            {{ hasApiKey ? '服务端已保存密钥，网页不回显明文。' : '尚未配置密钥，填入后保存在服务端 Settings.json。' }}
          </div>
        </div>

        <div class="field-item" style="margin-top: 6px">
          <div class="field-label">模型</div>
          <div class="field-control flex" style="gap: 6px; flex-wrap: wrap">
            <ModernComboBox v-model="model" :options="modelOptions" watermark="模型 id" :width="230" editable />
            <button class="small" :disabled="scanLoading" @click="scanEndpointModels">{{ scanLoading ? '扫描中…' : '扫描端点模型' }}</button>
          </div>
          <div class="muted" style="font-size: 12px; margin-top: 4px; line-height: 1.6">
            {{ modelSourceText === '' ? '点「扫描端点模型」从端点拉取该 Key 下真实可用的模型。' : `候选来源：${modelSourceText}` }}
            <template v-if="activeModel">· {{ describeModel(activeModel) }}</template>
          </div>
        </div>

        <div class="field-item" style="margin-top: 6px">
          <div class="field-label">推理级别</div>
          <div class="field-control"><input v-model="reasoningEffort" type="text" placeholder="low / medium / high" /></div>
        </div>

        <div v-if="scanMessage" class="muted" style="margin-top: 6px; font-size: 12px">{{ scanMessage }}</div>
        <button class="small primary" style="margin-top: 8px" @click="saveConfig()">保存</button>
      </div>

      <div v-if="showTips" class="panel-box panel" style="margin-top: 10px">
        <div class="panel-title">操作提示</div>
        <div class="muted" style="line-height: 1.9">
          ① 「重载连接」→「模型管理」里选厂商（国内知名优先）自动填端点；<br />
          ② 填 API Key 保存后点「扫描端点模型」，从端点拉真实可用模型；<br />
          ③ 底部「权限级别」决定 Agent 能用哪些工具：<b>安全区域</b>只能读写参数面板，<b>环境控制</b>再加队列/预设/准备文件；<br />
          ④ 需要 Agent 动手时它会发起工具调用——<b>写操作会先弹确认卡</b>，你点允许才执行；<br />
          ⑤ 输入问题后 Ctrl+Enter 发送，可随时停止；对话记录保存在浏览器本地。
        </div>
      </div>

      <ProviderPickerDialog
        v-if="showProviderPicker"
        :providers="catalog"
        :selected="providerId"
        :stale="catalogStale"
        :fetched-at="catalogFetchedAt"
        @close="showProviderPicker = false"
        @pick="pickProvider"
      />
    </div>

    <!-- 右列：聊天区 + 输入区 -->
    <div class="agent-col agent-chat">
      <div class="panel-box panel" style="flex: 1; display: flex; flex-direction: column; min-height: 0">
        <div class="panel-title">Agent 智能体</div>
        <div class="chat-history">
          <div v-if="messages.length === 0" class="empty">
            向 3FUI 副驾驶提问：参数推荐、滤镜写法、报错排查、批量转码方案……<br />
            <span class="muted">例如：「N 卡压 AV1 用什么参数」「这段 ffmpeg 报错怎么修」「帮我写一个去色带的滤镜链」</span>
          </div>
          <div
            v-for="(message, index) in messages"
            v-show="message.role !== 'tool'"
            :key="index"
            class="chat-msg"
            :class="message.role"
          >
            <div class="role">{{ message.role === 'user' ? '你' : 'Agent' }}</div>
            <template v-if="message.role === 'assistant'">
              <div v-if="message.content" v-html="marked(message.content)"></div>
              <ToolCallCard v-if="message.tool_calls && message.tool_calls.length > 0" :calls="message.tool_calls" />
            </template>
            <div v-else style="white-space: pre-wrap">{{ message.content }}</div>
          </div>
        </div>

        <!-- 写操作确认卡：挂起循环等用户点允许/拒绝 -->
        <ToolConfirmCard
          v-if="pendingConfirm"
          :tool="pendingConfirm.tool"
          :args="pendingConfirm.args"
          @allow="settleConfirm(true)"
          @deny="settleConfirm(false)"
        />

        <div class="chat-input">
          <textarea v-model="input" rows="2" placeholder="输入问题，Ctrl+Enter 发送" @keydown.ctrl.enter="send" @keydown.meta.enter="send" />
          <button v-if="!streaming" class="primary" @click="send">发送</button>
          <button v-else class="danger" @click="stop">停止</button>
        </div>

        <!-- 底部控制栏（原版：联网 / 权限级别 / 推理级别 / 模型 / 发送） -->
        <div class="flex-wrap" style="margin-top: 8px">
          <ModernComboBox v-model="onlineMode" :options="ONLINE_MODES" :width="120" />
          <ModernComboBox v-model="permissionLevel" :options="PERMISSION_LEVELS" :width="120" />
          <ModernComboBox v-model="reasoningEffort" :options="REASONING_LEVELS" :width="120" />
          <ModernComboBox v-model="model" :options="modelOptions" watermark="模型选择" :width="200" editable />
          <span class="muted">{{ hasApiKey ? '密钥已配置' : '密钥未配置' }}</span>
          <span class="muted">可用工具 {{ toolCatalog.length }} 个<template v-if="streaming && currentRound > 0"> · 第 {{ currentRound }}/{{ MAX_ROUNDS }} 轮</template></span>
        </div>
      </div>
    </div>
  </div>
</template>
