<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { api, copyToClipboard, errorText, openQueueWebSocket, type QueueTask } from '../api'
import { usePendingFiles, useToast } from '../store'

const toast = useToast()
const pendingFiles = usePendingFiles()
const tasks = ref<QueueTask[]>([])
const selected = ref<Set<string>>(new Set())
const showLog = ref<string | null>(null)
const logDetail = ref<Record<string, unknown> | null>(null)
const logLines = ref<{ 文本: string; 是否错误: boolean; 类别: string }[]>([])
const logFilter = ref<'all' | 'error'>('all')
const commandLineTask = ref('')
const menuOpen = ref(false)

const STATUS_LABEL: Record<string, string> = {
  未处理: 'pending', 正在处理: 'running', 已暂停: 'paused', 已完成: 'done', 错误: 'error', 已停止: 'stopped',
}
const STATUS_TEXT: Record<string, string> = {
  未处理: '未处理', 正在处理: '正在处理', 已暂停: '已暂停', 已完成: '已完成', 错误: '错误', 已停止: '已停止',
}

let socket: WebSocket | null = null

const runningCount = () => tasks.value.filter(t => t.状态 === '正在处理').length
const errorCount = () => tasks.value.filter(t => t.状态 === '错误').length

// 排队序列：当前执行中的任务与下一个未处理任务（让用户知道会不会继续跑下一个）
const runningTask = computed(() => tasks.value.find(t => t.状态 === '正在处理'))
const nextTask = computed(() => tasks.value.find(t => t.状态 === '未处理'))

/** 行的序列标识：正在处理 / 下一个 / 第 N 位等待 */
function seqInfo(task: QueueTask): { text: string; cls: string } {
  if (task.状态 === '正在处理') return { text: '当前', cls: 'now' }
  if (task.状态 === '未处理') {
    if (nextTask.value?.ID === task.ID) return { text: '下一个', cls: 'next' }
    const waiting = tasks.value.filter(t => t.状态 === '未处理')
    const index = waiting.findIndex(t => t.ID === task.ID)
    return { text: `#${index + 1}`, cls: '' }
  }
  return { text: '', cls: '' }
}

// 底部实时输出：优先选中的运行中任务，否则当前运行任务（原版：编码队列项底部显示最新日志）
const liveOutput = computed(() => {
  const picked = tasks.value.find(t => selected.value.has(t.ID) && (t.状态 === '正在处理' || t.状态 === '已暂停'))
    ?? runningTask.value
  if (!picked) return null
  return {
    name: picked.任务名称,
    text: picked.实时输出 || picked.最新底部日志文本 || '',
    isError: picked.最新底部日志是否错误,
  }
})

// 日志对话框里「编码器切换记录」一节：取自任务 DTO，旧服务端响应没有该字段时按空数组处理
const logSwitches = computed(() => tasks.value.find(t => t.ID === showLog.value)?.切换记录 ?? [])

function applyMessage(message: Record<string, unknown>) {
  if (message.type === 'queue') {
    tasks.value = (message.tasks ?? []) as QueueTask[]
  } else if (message.type === 'task') {
    const task = message.task as QueueTask
    const index = tasks.value.findIndex(t => t.ID === task.ID)
    if (index >= 0) tasks.value[index] = task
  } else if (message.type === 'progress') {
    const progresses = (message.tasks ?? []) as Partial<QueueTask>[]
    for (const progress of progresses) {
      const task = tasks.value.find(t => t.ID === progress.ID)
      if (task) Object.assign(task, progress)
    }
  } else if (message.type === 'event') {
    const event = message as { name: string; taskId: string }
    if (event.name === 'task.completed' || event.name === 'task.failed') refresh()
  }
}

function refresh() {
  api.queue.list().then(list => { tasks.value = list }).catch(() => {})
}

function toggleSelect(id: string) {
  if (selected.value.has(id)) selected.value.delete(id)
  else selected.value.add(id)
  selected.value = new Set(selected.value)
}

function selectAll() {
  selected.value = tasks.value.length > 0 && selected.value.size === tasks.value.length
    ? new Set()
    : new Set(tasks.value.map(task => task.ID))
}

function act(action: string, ids: string[] = [...selected.value]) {
  if (ids.length === 0) return toast.push('err', '请先勾选任务')
  api.queue.action(action, ids).then(() => refresh()).catch(error => toast.push('err', errorText(error)))
}

function openLog(id: string) {
  showLog.value = id
  loadLog(id)
}

async function loadLog(id: string) {
  try {
    const detail = await api.queue.detail(id)
    logDetail.value = detail
    const snapshot = (detail['日志快照'] ?? {}) as { 条目?: { 文本: string; 是否错误: boolean; 类别: string }[] }
    logLines.value = (snapshot['条目'] ?? []).map(line => ({
      文本: line.文本,
      是否错误: Boolean(line.是否错误),
      类别: String(line.类别),
    }))
  } catch {
    logLines.value = []
  }
}

async function copyCommandLine(task: QueueTask) {
  // 预设任务的命令行在详情接口里现场生成，列表字段可能为空
  let text = task.命令行 || ''
  if (!text) {
    try {
      const detail = await api.queue.detail(task.ID)
      text = String(detail['命令行'] ?? '')
    } catch { /* 忽略，按空处理 */ }
  }
  if (!text) return toast.push('err', '该任务还没有命令行')
  const ok = await copyToClipboard(text)
  toast.push(ok ? 'ok' : 'err', ok ? '命令行已复制' : '复制失败，请打开「命令行」对话框手动复制')
}

function locateTask(task: QueueTask) {
  const path = task.输出文件 || task.输入文件
  if (!path) return toast.push('err', '该任务还没有输出路径')
  copyToClipboard(path).then(ok => toast.push(ok ? 'ok' : 'err', ok ? `已复制路径（网页版无法打开文件管理器）：${path}` : `路径：${path}`))
}

// ── 任务管理菜单（原版右键/菜单项：全选、选中错误任务、上移、下移）──
function menuSelectAll() {
  selected.value = new Set(tasks.value.map(t => t.ID))
  menuOpen.value = false
}
function menuSelectErrors() {
  selected.value = new Set(tasks.value.filter(t => t.状态 === '错误').map(t => t.ID))
  menuOpen.value = false
  if (selected.value.size === 0) toast.push('ok', '没有错误任务')
}
function menuSelectNone() {
  selected.value = new Set()
  menuOpen.value = false
}
function menuMove(delta: number) {
  if (selected.value.size !== 1) { toast.push('err', '上移/下移仅支持选中一个任务'); return }
  const id = [...selected.value][0]
  const index = tasks.value.findIndex(t => t.ID === id)
  const next = index + delta
  if (index < 0 || next < 0 || next >= tasks.value.length) return
  const ids = tasks.value.map(t => t.ID)
  ;[ids[index], ids[next]] = [ids[next], ids[index]]
  api.queue.reorder(ids).then(() => { refresh(); menuOpen.value = false }).catch(error => toast.push('err', errorText(error)))
}

// 原版「定位」仅单选生效（Form_v6_编码队列：ids.Count <> 1 Then Exit Sub）
function locateSelected() {
  if (selected.value.size !== 1) return toast.push('err', '定位仅支持选中一个任务')
  const task = tasks.value.find(t => selected.value.has(t.ID))
  if (task) locateTask(task)
}

// 原版双击行按状态分流：已完成→定位输出文件；其他→打开任务日志
function onRowDblClick(task: QueueTask) {
  if (task.状态 === '已完成') locateTask(task)
  else openLog(task.ID)
}

// 原版快捷键：Enter=开始、Delete=移除、空格=暂停/恢复（输入框聚焦时不抢键）
function onKeydown(event: KeyboardEvent) {
  const target = event.target as HTMLElement
  if (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable) return
  // 弹窗（日志对话框等）打开时不响应队列快捷键，避免 Delete 误删任务
  if (showLog.value) return
  if (selected.value.size === 0) return
  if (event.key === 'Enter') { event.preventDefault(); act('start') }
  else if (event.key === 'Delete') { event.preventDefault(); act('remove') }
  else if (event.key === ' ') {
    event.preventDefault()
    const list = tasks.value.filter(t => selected.value.has(t.ID))
    // 全部已暂停则恢复，否则暂停（原版空格=暂停/恢复切换）
    act(list.every(t => t.状态 === '已暂停') ? 'resume' : 'pause')
  }
}

function addCommandLine() {
  if (!commandLineTask.value.trim()) return toast.push('err', '请输入命令行')
  api.queue.addCommandLine(commandLineTask.value.trim(), `命令行任务 ${new Date().toLocaleTimeString()}`)
    .then(() => { commandLineTask.value = ''; toast.push('ok', '命令行任务已添加') })
    .catch(error => toast.push('err', errorText(error)))
}

function onDrop(event: DragEvent) {
  const files = [...(event.dataTransfer?.files ?? [])].filter(file => file.type.startsWith('video/') || file.name.match(/\.(mp4|mkv|avi|mov|ts|m2ts|webm|mp3|flac|wav|m4a|aac|ass|srt)$/i))
  if (files.length > 0) {
    const paths = files.map(file => (file as File & { path?: string }).path ?? '')
    if (paths.every(path => path !== '')) {
      pendingFiles.add(paths)
      toast.push('ok', `已加入待处理：${paths.length} 个文件`)
    } else {
      toast.push('err', '浏览器无法直接获取文件路径，请使用「准备文件」页面浏览选择')
    }
  }
}

// disposed 防「幽灵重连」：卸载时 close 会触发 onclose，不能再排新的重连
let disposed = false
function connect() {
  if (disposed) return
  socket = openQueueWebSocket(applyMessage)
  socket.onclose = () => { if (!disposed) window.setTimeout(connect, 3000) }
}

onMounted(() => {
  refresh()
  connect()
  window.addEventListener('keydown', onKeydown)
})

onBeforeUnmount(() => {
  disposed = true
  if (socket) socket.onclose = null
  socket?.close()
  window.removeEventListener('keydown', onKeydown)
  // 日志弹窗轮询也必须随组件卸载，否则 1.5s 轮询永久运行
  if (logTimer !== undefined) { window.clearInterval(logTimer); logTimer = undefined }
})
// 日志对话框打开期间每秒拉一次， ffmpeg 输出持续滚动
let logTimer: number | undefined
watch(showLog, id => {
  if (logTimer !== undefined) { window.clearInterval(logTimer); logTimer = undefined }
  if (id) {
    loadLog(id)
    logTimer = window.setInterval(() => { if (showLog.value) loadLog(showLog.value) }, 1500)
  }
})
</script>

<template>
  <div class="fill-col" @dragover.prevent @drop.prevent="onDrop" @click="menuOpen = false">
    <!-- 工具行：原版「任务管理菜单」是可点下拉菜单 + 彩色纯文字按钮 + 计数 -->
    <div class="page-header">
      <div class="menu-wrap" @click.stop>
        <button class="queue-tab" style="border: none; cursor: pointer" @click="menuOpen = !menuOpen">任务管理菜单 ▼</button>
        <div v-if="menuOpen" class="menu-pop">
          <button class="menu-item" @click="menuSelectAll">全选</button>
          <button class="menu-item" @click="menuSelectErrors">选中错误任务</button>
          <button class="menu-item" @click="menuSelectNone">取消选择</button>
          <button class="menu-item" :disabled="selected.size !== 1" @click="menuMove(-1)">上移选中任务</button>
          <button class="menu-item" :disabled="selected.size !== 1" @click="menuMove(1)">下移选中任务</button>
        </div>
      </div>
      <button class="plain small txt-green" style="margin-left: 28px" @click="act('start')">开始</button>
      <button class="plain small txt-gold" @click="act('pause')">暂停</button>
      <button class="plain small txt-green" @click="act('resume')">恢复</button>
      <button class="plain small txt-red" @click="act('stop')">停止</button>
      <button class="plain small txt-red" @click="act('remove')">移除</button>
      <button class="plain small txt-gold" @click="act('reset')">重置</button>
      <button class="plain small txt-purple" @click="locateSelected">定位</button>
      <span class="spacer" />
      <span class="queue-count">数量 <b class="txt-blue">{{ selected.size }}/{{ tasks.length }}</b></span>
      <span class="queue-count">运行 <b class="txt-green">{{ runningCount() }}</b></span>
      <span class="queue-count">错误 <b class="txt-red">{{ errorCount() }}</b></span>
    </div>

    <div class="flex" style="margin-bottom: 8px">
      <input v-model="commandLineTask" type="text" placeholder="直接执行命令行任务：-i 输入 -c:v libx264 -crf 23 输出" class="mono" style="flex: 1" />
      <button class="small" @click="addCommandLine">添加命令行任务</button>
    </div>

    <div class="panel-box fill-scroll">
      <table class="list">
        <thead>
          <tr>
            <th style="width: 32px"><input type="checkbox" :checked="tasks.length > 0 && selected.size === tasks.length" @change="selectAll" /></th>
            <th>任务名称</th>
            <th style="width: 86px">状态</th>
            <th style="width: 190px">进度</th>
            <th style="width: 90px">效率</th>
            <th style="width: 110px">大小/预估</th>
            <th style="width: 90px">质量</th>
            <th style="width: 100px">比特率</th>
            <th style="width: 120px">剩余/已用</th>
            <th style="width: 170px">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="task in tasks" :key="task.ID" :class="{ selected: selected.has(task.ID) }" @dblclick="onRowDblClick(task)">
            <td><input type="checkbox" :checked="selected.has(task.ID)" @change="toggleSelect(task.ID)" @click.stop /></td>
            <td @click="toggleSelect(task.ID)">
              <div>
                <span v-if="seqInfo(task).text" class="seq-badge" :class="seqInfo(task).cls">{{ seqInfo(task).text }}</span>
                <span>{{ task.任务名称 }}</span>
              </div>
              <div class="muted mono" style="font-size: 11px">{{ task.输入文件 }}</div>
              <div v-if="task.输出文件" class="muted mono" style="font-size: 11px">→ {{ task.输出文件 }}</div>
              <div
                v-if="task.实际编码器 && task.预设编码器 && task.实际编码器 !== task.预设编码器"
                class="badge"
                style="margin-top: 2px; color: var(--gold)"
                :title="`预设编码器 ${task.预设编码器}，实际按 ${task.实际编码器} 运行`"
              >实际编码器 {{ task.实际编码器 }}</div>
            </td>
            <td @click="toggleSelect(task.ID)">
              <span class="badge" :class="STATUS_LABEL[task.状态] ?? ''">{{ STATUS_TEXT[task.状态] ?? task.状态 }}</span>
            </td>
            <td @click="toggleSelect(task.ID)">
              <div class="progress"><div :style="{ width: (task.百分比 * 100) + '%' }" /></div>
              <div class="muted" style="margin-top: 2px; font-size: 11px">{{ task.进度文本 }}</div>
            </td>
            <td @click="toggleSelect(task.ID)" class="mono">{{ task.效率文本 }}</td>
            <td @click="toggleSelect(task.ID)" class="mono">{{ task.输出大小文本 }}</td>
            <td @click="toggleSelect(task.ID)" class="mono">{{ task.质量文本 }}</td>
            <td @click="toggleSelect(task.ID)" class="mono">{{ task.比特率文本 }}</td>
            <td @click="toggleSelect(task.ID)" class="mono">{{ task.时间文本 }}</td>
            <td class="actions">
              <button class="small" @click="openLog(task.ID)">日志</button>
              <button class="small" style="margin-left: 4px" @click="copyCommandLine(task)">命令行</button>
            </td>
          </tr>
          <tr v-if="tasks.length === 0">
            <td colspan="10" class="empty">队列为空：去「准备文件」选文件，点“加入编码队列”即可自动开始</td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- 底部实时输出：选中任务或当前运行任务的 ffmpeg 最新一行输出（原版：队列项底部显示最新日志） -->
    <div v-if="liveOutput" class="queue-live" :title="liveOutput.text">
      <span class="lab">实时输出 · {{ liveOutput.name }}</span>
      <span :class="{ err: liveOutput.isError }">{{ liveOutput.text || '（暂无输出）' }}</span>
    </div>
    <div v-else-if="nextTask" class="queue-live">
      <span class="lab">下一个任务</span>
      <span>{{ nextTask.任务名称 }}</span>
    </div>

    <!-- 任务日志对话框 -->
    <div v-if="showLog" class="modal-mask" @click.self="showLog = null">
      <div class="modal" style="width: min(900px, 94vw)">
        <div class="modal-header">
          <span>任务日志 · {{ tasks.find(t => t.ID === showLog)?.任务名称 ?? showLog }}</span>
          <div class="flex">
            <button class="small" :class="{ primary: logFilter === 'all' }" @click="logFilter = 'all'">全部</button>
            <button class="small" :class="{ primary: logFilter === 'error' }" @click="logFilter = 'error'">仅错误</button>
            <button class="small" @click="loadLog(showLog)">刷新</button>
            <button class="small" @click="showLog = null">关闭</button>
          </div>
        </div>
        <div class="modal-body">
          <div v-if="logDetail" class="flex-wrap">
            <span class="muted">状态：</span>
            <span class="badge" :class="STATUS_LABEL[String(logDetail['状态'])] ?? ''">{{ STATUS_TEXT[String(logDetail['状态'])] ?? logDetail['状态'] }}</span>
          </div>
          <div v-if="logDetail" class="code">{{ logDetail['命令行'] }}</div>
          <div class="log-view" style="max-height: 52vh">
            <template v-for="(line, index) in logLines.filter(l => logFilter === 'all' || l.是否错误)" :key="index">
              <div :class="{ 'hl-error': line.是否错误 }">{{ line.文本 }}</div>
            </template>
            <div v-if="logLines.length === 0" class="muted">暂无日志</div>
          </div>
          <div v-if="logSwitches.length" class="switch-records">
            <div class="switch-records-title">编码器切换记录</div>
            <div v-for="(rec, index) in logSwitches" :key="index" class="switch-records-item">
              <div class="switch-records-head">{{ rec.时间 }}　{{ rec.原编码器 }} → {{ rec.新编码器 }}（{{ rec.触发方式 }}）</div>
              <div class="muted">原因：{{ rec.原因 }}</div>
              <div v-if="rec.丢弃参数?.length" class="muted">丢弃参数：{{ rec.丢弃参数.join('、') }}</div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.switch-records { display: flex; flex-direction: column; gap: 6px; border-top: 1px solid #2a2a2a; padding-top: 10px; }
.switch-records-title { font-size: 12px; color: var(--blue); }
.switch-records-item {
  display: flex; flex-direction: column; gap: 2px;
  padding: 8px 10px; border: 1px solid var(--border-control); border-radius: var(--radius-control);
  font-size: 12px; line-height: 1.6;
}
.switch-records-head { color: var(--text); font-family: var(--mono); }
</style>
