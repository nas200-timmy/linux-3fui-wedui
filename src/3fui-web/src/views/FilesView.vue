<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { api, errorText } from '../api'
import { useCurrentPreset, usePendingFiles, useToast, 入队兼容确认 } from '../store'
import { 默认音频编码器 } from '../schema'
import ModernComboBox from '../components/ModernComboBox.vue'

interface Entry { name: string; path: string; isDirectory: boolean; isMedia: boolean; size?: number }

const SORT_OPTIONS = [
  { value: 'name', label: '排序：按文件名' },
  { value: 'size', label: '排序：按大小' },
  { value: 'ext', label: '排序：按后缀' },
]

const emit = defineEmits<{ 'go-preset': []; 'go-queue': [] }>()

const toast = useToast()
const pendingFiles = usePendingFiles()
const currentPresetStore = useCurrentPreset()
const enqueueBusy = ref(false)
const currentPath = ref('/media')
const parent = ref<string | null>(null)
const entries = ref<Entry[]>([])
const showBrowser = ref(false)
const selected = ref<Set<string>>(new Set())
const sortBy = ref<'name' | 'size' | 'ext'>('name')
const meta = ref<Record<string, { name: string; size?: number }>>({})

function browse(path?: string) {
  api.probe.browse(path ?? currentPath.value)
    .then(result => {
      const data = result as { path: string; parent: string | null; entries: Entry[] }
      currentPath.value = data.path
      parent.value = data.parent
      entries.value = data.entries
    })
    .catch(error => toast.push('err', errorText(error)))
}

function remember(entry: Entry) {
  meta.value[entry.path] = { name: entry.name, size: entry.size }
}

function toggleEntry(entry: Entry) {
  if (entry.isDirectory) return browse(entry.path)
  if (!entry.isMedia) return toast.push('err', '不支持的文件类型')
  if (pendingFiles.files.includes(entry.path)) {
    pendingFiles.remove(entry.path)
    selected.value.delete(entry.path)
  } else {
    remember(entry)
    pendingFiles.add([entry.path])
  }
}

// 真递归：逐层 browse 下钻子目录（原实现只取单层，却宣称递归，剧集目录会静默漏掉全部子目录文件）
async function collectMediaRecursive(path: string, visited: Set<string>, depth: number): Promise<Entry[]> {
  if (visited.has(path) || depth > 10) return []
  visited.add(path)
  const data = (await api.probe.browse(path)) as { entries: Entry[] }
  const files = data.entries.filter(entry => entry.isMedia && !entry.isDirectory)
  const dirs = data.entries.filter(entry => entry.isDirectory)
  const nested = await Promise.all(dirs.map(dir => collectMediaRecursive(dir.path, visited, depth + 1)))
  return [...files, ...nested.flat()]
}

function addDirectoryRecursive(path: string) {
  collectMediaRecursive(path, new Set(), 0).then(files => {
    if (files.length === 0) return toast.push('err', '目录（含子目录）下没有可加入的媒体文件')
    files.forEach(remember)
    pendingFiles.add(files.map(entry => entry.path))
    toast.push('ok', `已递归加入 ${files.length} 个文件`)
  }).catch(error => toast.push('err', errorText(error)))
}

// ── 待处理列表 ──
function fileName(path: string) {
  return meta.value[path]?.name ?? path.split('/').pop() ?? path
}
function filePath(path: string) {
  const parts = path.split('/')
  parts.pop()
  return parts.join('/') || '/'
}
function fileExt(path: string) {
  const name = fileName(path)
  const index = name.lastIndexOf('.')
  return index >= 0 ? name.slice(index + 1).toUpperCase() : ''
}
function fileSize(path: string) {
  const size = meta.value[path]?.size
  if (size === undefined) return '—'
  if (size >= 1073741824) return (size / 1073741824).toFixed(2) + ' GB'
  if (size >= 1048576) return (size / 1048576).toFixed(1) + ' MB'
  if (size >= 1024) return (size / 1024).toFixed(0) + ' KB'
  return size + ' B'
}

const sortedFiles = computed(() => {
  const list = [...pendingFiles.files]
  if (sortBy.value === 'name') list.sort((a, b) => fileName(a).localeCompare(fileName(b), 'zh-CN'))
  else if (sortBy.value === 'size') list.sort((a, b) => (meta.value[b]?.size ?? 0) - (meta.value[a]?.size ?? 0))
  else list.sort((a, b) => fileExt(a).localeCompare(fileExt(b)))
  return list
})

function toggleSelect(path: string) {
  if (selected.value.has(path)) selected.value.delete(path)
  else selected.value.add(path)
  selected.value = new Set(selected.value)
}

function removeSelected() {
  if (selected.value.size === 0) return toast.push('err', '请先勾选要移除的文件')
  for (const path of selected.value) pendingFiles.remove(path)
  toast.push('ok', `已移除 ${selected.value.size} 个文件`)
  selected.value = new Set()
}

function removeAll() {
  if (pendingFiles.files.length === 0) return toast.push('err', '列表已为空')
  pendingFiles.clear()
  selected.value = new Set()
}

function addDirectoryAsFiles() {
  const dirs = entries.value.filter(entry => entry.isDirectory)
  if (dirs.length === 0) return toast.push('err', '当前目录没有子文件夹')
  dirs.forEach(dir => addDirectoryRecursive(dir.path))
}

// 原版「加入编码队列」：当前参数面板快照 + 列表全部文件一键入队，清空列表并切到队列页
async function enqueueAll() {
  if (pendingFiles.files.length === 0) return toast.push('err', '请先添加文件')
  const preset = currentPresetStore.preset
  const 视频编码器 = String(preset['视频参数_编码器_具体编码'] ?? '').trim()
  const 音频编码器 = String(preset['音频参数_编码器_代号'] ?? '').trim()
  if (视频编码器 === '' && (音频编码器 === '' || 音频编码器 === 默认音频编码器)) {
    toast.push('err', '还没有选择视频编码器或音频编码器，请先在「参数面板」选择')
    emit('go-preset')
    return
  }
  if (String(preset['输出容器'] ?? '').trim() === '') {
    toast.push('err', '还没有指定输出容器（后缀），请先在「参数面板 → 输出文件设置」选择')
    emit('go-preset')
    return
  }
  // 「全保留」策略装不下当前容器时，弹窗让用户选：换 MKV / 丢弃不兼容项 / 取消
  if (!(await 入队兼容确认(currentPresetStore))) return
  enqueueBusy.value = true
  api.queue.addTasks([...pendingFiles.files], currentPresetStore.preset)
    .then(tasks => {
      toast.push('ok', `已添加 ${tasks.length} 个任务`)
      pendingFiles.clear()
      selected.value = new Set()
      emit('go-queue')
    })
    .catch(error => toast.push('err', errorText(error)))
    .finally(() => { enqueueBusy.value = false })
}

onMounted(() => browse())
</script>

<template>
  <div class="fill-col">
    <!-- 按钮组（原版顺序：加入编码队列 / 添加文件 / 添加文件夹及子目录 / 移除选中 / 移除全部 / 排序） -->
    <div class="page-header">
      <button class="small" :disabled="pendingFiles.files.length === 0 || enqueueBusy" @click="enqueueAll">加入编码队列</button>
      <button class="small" :class="{ primary: showBrowser }" @click="showBrowser = !showBrowser">添加文件</button>
      <button class="small" @click="addDirectoryAsFiles">添加文件夹及子目录</button>
      <button class="small" :disabled="selected.size === 0" @click="removeSelected">移除选中</button>
      <button class="small" :disabled="pendingFiles.files.length === 0" @click="removeAll">移除全部</button>
      <span class="spacer" />
      <ModernComboBox v-model="sortBy" :options="SORT_OPTIONS" :width="250" />
    </div>

    <!-- 目录浏览（网页版添加文件的入口，默认收起） -->
    <div v-if="showBrowser" class="panel-box panel" style="margin-bottom: 10px; flex: none">
      <div class="flex" style="margin-bottom: 8px">
        <button class="small" :disabled="!parent" @click="browse(parent ?? undefined)">↑ 上级</button>
        <input type="text" :value="currentPath" class="mono" style="flex: 1" @keydown.enter="browse(($event.target as HTMLInputElement).value)" />
        <button class="small" @click="browse(currentPath)">跳转</button>
        <button class="small" @click="browse()">刷新</button>
      </div>
      <div style="max-height: 30vh; overflow: auto">
        <table class="list">
          <thead>
            <tr>
              <th style="width: 34px"></th>
              <th>名称</th>
              <th style="width: 90px">后缀</th>
              <th style="width: 110px">大小</th>
              <th style="width: 200px">操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="entry in entries" :key="entry.path">
              <td>
                <input
                  v-if="!entry.isDirectory && entry.isMedia"
                  type="checkbox"
                  :checked="pendingFiles.files.includes(entry.path)"
                  @change="toggleEntry(entry)"
                />
              </td>
              <td @click="toggleEntry(entry)">
                <span v-if="entry.isDirectory" class="txt-blue">{{ entry.name }}/</span>
                <span v-else-if="entry.isMedia">{{ entry.name }}</span>
                <span v-else class="muted">{{ entry.name }}</span>
              </td>
              <td class="mono">{{ entry.isDirectory ? '' : fileExt(entry.path) }}</td>
              <td class="mono">{{ entry.isDirectory ? '' : fileSize(entry.path) }}</td>
              <td class="actions">
                <button v-if="entry.isDirectory" class="small" @click="addDirectoryRecursive(entry.path)">递归加入</button>
                <button v-else-if="entry.isMedia" class="small" @click="toggleEntry(entry)">
                  {{ pendingFiles.files.includes(entry.path) ? '移出' : '加入' }}
                </button>
              </td>
            </tr>
            <tr v-if="entries.length === 0"><td colspan="5" class="empty">空目录</td></tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- 准备文件列表（原版列：文件名 / 路径 / 大写后缀 / 大小） -->
    <div class="panel-box fill-scroll">
      <table class="list">
        <thead>
          <tr>
            <th style="width: 34px"></th>
            <th style="width: 300px">文件名</th>
            <th>路径</th>
            <th style="width: 96px">大写后缀</th>
            <th style="width: 110px">大小</th>
            <th style="width: 90px">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="path in sortedFiles" :key="path" :class="{ selected: selected.has(path) }">
            <td><input type="checkbox" :checked="selected.has(path)" @change="toggleSelect(path)" /></td>
            <td @click="toggleSelect(path)">{{ fileName(path) }}</td>
            <td class="mono muted" @click="toggleSelect(path)">{{ filePath(path) }}</td>
            <td class="mono" @click="toggleSelect(path)">{{ fileExt(path) }}</td>
            <td class="mono" @click="toggleSelect(path)">{{ fileSize(path) }}</td>
            <td class="actions"><button class="small" @click="pendingFiles.remove(path)">移除</button></td>
          </tr>
          <tr v-if="pendingFiles.files.length === 0">
            <td colspan="6" class="empty">尚未选择文件。用上面的「添加文件」浏览 NAS 目录并勾选媒体文件。</td>
          </tr>
        </tbody>
      </table>
    </div>

    <div class="hint-bar">可以直接把文件推进编码队列来开始，如果文件很多或者有其他需求再用这个页面</div>
  </div>
</template>
