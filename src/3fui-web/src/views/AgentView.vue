<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { api } from '../api'
import { usePendingFiles, useToast } from '../store'
import { marked } from '../markdown'
import ModernComboBox from '../components/ModernComboBox.vue'

interface Message { role: 'user' | 'assistant'; content: string }
interface Conversation { id: string; title: string; time: string; messages: Message[] }

const STORAGE_KEY = 'linux-3fui-agent-conversations'

const 选项 = (values: string[]) => values.map(v => ({ value: v, label: v }))
const ONLINE_MODES = 选项(['本地联网', '端点联网', '禁用联网'])
const ACCESS_LEVELS = 选项(['系统访问', '仅对话'])
const REASONING_LEVELS = [{ value: '', label: '默认' }, ...选项(['low', 'medium', 'high'])]

const toast = useToast()
const conversations = ref<Conversation[]>([])
const activeId = ref('')
const input = ref('')
const endpoint = ref('')
const model = ref('')
const hasApiKey = ref(false)
const reasoningEffort = ref('')
const onlineMode = ref('本地联网')
const accessLevel = ref('系统访问')
const streaming = ref(false)
const abort = ref<AbortController | null>(null)
const showConfig = ref(false)
const showTips = ref(false)

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

// ── 模型选择：可编辑下拉 + localStorage 记住用过的模型 ──
const MODEL_HISTORY_KEY = 'linux-3fui-agent-models'
const MODEL_PRESETS = ['deepseek-chat', 'kimi-k2-0905-preview', 'gpt-4o-mini', 'qwen-plus']
const modelHistory = ref<string[]>([])
const modelOptions = computed(() => {
  const known = [...modelHistory.value, ...MODEL_PRESETS]
  return [...new Set(known.filter(Boolean))].map(v => ({ value: v, label: v }))
})
function loadModelHistory() {
  try { modelHistory.value = JSON.parse(localStorage.getItem(MODEL_HISTORY_KEY) ?? '[]') as string[] } catch { modelHistory.value = [] }
}
function rememberModel() {
  const m = model.value.trim()
  if (!m || modelHistory.value.includes(m)) return
  modelHistory.value = [m, ...modelHistory.value].slice(0, 12)
  try { localStorage.setItem(MODEL_HISTORY_KEY, JSON.stringify(modelHistory.value)) } catch { /* 本地存储不可用时忽略 */ }
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
  try {
    const config = await api.agent.config()
    endpoint.value = config.endpoint
    model.value = config.model
    hasApiKey.value = config.hasApiKey
    reasoningEffort.value = config.reasoningEffort
    if (config.reasoningEffort) accessLevel.value = '系统访问'
  } catch { /* 配置可能尚未初始化 */ }
}

function saveConfig() {
  api.agent.saveConfig({ endpoint: endpoint.value, model: model.value, reasoningEffort: reasoningEffort.value })
    .then(() => { toast.push('ok', 'Agent 配置已保存'); loadConfig() })
    .catch(error => toast.push('err', String(error)))
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
  const outgoing = [
    { role: 'system', content: `${SYSTEM_PROMPT}\n联网设置：${onlineMode.value}；访问级别：${accessLevel.value}；推理级别：${reasoningEffort.value || '默认'}。` },
    ...conv.messages.map(m => ({ role: m.role, content: m.content })),
    { role: 'user', content },
  ]
  conv.messages.push({ role: 'user', content })
  conv.messages.push({ role: 'assistant', content: '' })
  input.value = ''
  streaming.value = true
  const controller = new AbortController()
  abort.value = controller

  try {
    const response = await api.agent.chat(outgoing, model.value || undefined, controller.signal)
    if (!response.ok) {
      const body = await response.json().catch(() => ({ error: response.statusText }))
      throw new Error((body as { error?: string }).error ?? `HTTP ${response.status}`)
    }
    if (!response.body) throw new Error('响应无内容流')
    const reader = response.body.getReader()
    const decoder = new TextDecoder()
    let buffer = ''
    let assistant = ''
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
          const chunk = JSON.parse(data)
          const delta = chunk?.choices?.[0]?.delta?.content ?? chunk?.choices?.[0]?.message?.content
          if (delta) {
            assistant += delta
            conv.messages[conv.messages.length - 1].content = assistant
          }
        } catch { /* 跳过无法解析的 SSE 行 */ }
      }
    }
  } catch (error) {
    if ((error as Error).name !== 'AbortError') {
      // 写回发起请求的会话：流式期间用户可能已切换到其他对话，active 已是新会话
      const last = conv.messages[conv.messages.length - 1]
      last.content = last.content || `请求失败：${(error as Error).message}`
    }
  } finally {
    streaming.value = false
    abort.value = null
    persist()
  }
}

function stop() {
  abort.value?.abort()
}

onMounted(() => { load(); loadConfig(); loadModelHistory() })
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
        <div class="panel-title">端点配置</div>
        <div class="field-item">
          <div class="field-label">端点地址</div>
          <div class="field-control"><input v-model="endpoint" type="text" placeholder="如 https://api.deepseek.com/v1" /></div>
        </div>
        <div class="field-item" style="margin-top: 6px">
          <div class="field-label">模型</div>
          <div class="field-control"><input v-model="model" type="text" placeholder="如 deepseek-chat / kimi-k2" /></div>
        </div>
        <div class="field-item" style="margin-top: 6px">
          <div class="field-label">推理级别</div>
          <div class="field-control"><input v-model="reasoningEffort" type="text" placeholder="low / medium / high" /></div>
        </div>
        <div class="muted" style="margin-top: 6px">API Key 在服务端 Settings.json 的 AgentApiKey 中配置，网页不回显密钥。</div>
        <button class="small primary" style="margin-top: 8px" @click="saveConfig">保存</button>
      </div>

      <div v-if="showTips" class="panel-box panel" style="margin-top: 10px">
        <div class="panel-title">操作提示</div>
        <div class="muted" style="line-height: 1.9">
          ① 在「重载连接」里填好端点与模型；<br />
          ② API Key 写到服务器 data/Settings.json 的 AgentApiKey；<br />
          ③ 输入问题后 Ctrl+Enter 发送，可随时停止；<br />
          ④ 对话记录保存在浏览器本地，不上传。
        </div>
      </div>
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
          <div v-for="(message, index) in messages" :key="index" class="chat-msg" :class="message.role">
            <div class="role">{{ message.role === 'user' ? '你' : 'Agent' }}</div>
            <div v-if="message.role === 'assistant'" v-html="marked(message.content)"></div>
            <div v-else style="white-space: pre-wrap">{{ message.content }}</div>
          </div>
        </div>

        <div class="chat-input">
          <textarea v-model="input" rows="2" placeholder="输入问题，Ctrl+Enter 发送" @keydown.ctrl.enter="send" @keydown.meta.enter="send" />
          <button v-if="!streaming" class="primary" @click="send">发送</button>
          <button v-else class="danger" @click="stop">停止</button>
        </div>

        <!-- 底部控制栏（原版：联网 / 系统访问 / 推理级别 / 模型 / 发送） -->
        <div class="flex-wrap" style="margin-top: 8px">
          <ModernComboBox v-model="onlineMode" :options="ONLINE_MODES" :width="120" />
          <ModernComboBox v-model="accessLevel" :options="ACCESS_LEVELS" :width="120" />
          <ModernComboBox v-model="reasoningEffort" :options="REASONING_LEVELS" :width="120" />
          <ModernComboBox v-model="model" :options="modelOptions" watermark="模型选择" :width="200" editable />
          <span class="muted">{{ hasApiKey ? '密钥已配置' : '密钥未配置（服务端 Settings.json）' }}</span>
        </div>
      </div>
    </div>
  </div>
</template>
