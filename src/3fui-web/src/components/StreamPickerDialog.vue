<script setup lang="ts">
// 可视化流选择器（原版 Form_v6_媒体流选择器）：对文件跑 ffprobe → 按 视频/音频/字幕 勾选 →
// 写回三个 stringlist 字段，格式 {文件索引}:{v|a|s}:{类型内序号}（与核心 规范流列表 解析一致）。
// 文件源：优先「准备文件」页的待处理文件；也可手动添加（不影响待处理列表）。
import { onMounted, ref } from 'vue'
import ParamDialog from './ParamDialog.vue'
import DirPickerDialog from './DirPickerDialog.vue'
import { api } from '../api'
import { getByPath, setByPath } from '../schema'
import { usePendingFiles, useToast } from '../store'

interface ProbeStream {
  index?: number
  codec_type?: string
  codec_name?: string
  tags?: { language?: string; title?: string }
}

interface StreamGroups { video: ProbeStream[]; audio: ProbeStream[]; subtitle: ProbeStream[] }
interface FileEntry { path: string; streams: StreamGroups; error: string }

const props = defineProps<{ preset: Record<string, unknown> }>()
const emit = defineEmits<{ close: [] }>()

const toast = useToast()
const pending = usePendingFiles()

const files = ref<FileEntry[]>([])
const manualPaths = ref<string[]>([])
const loading = ref(false)
const manualPicker = ref(false)
const selected = ref<Set<string>>(new Set())

const GROUPS: { type: 'v' | 'a' | 's'; label: string; key: keyof StreamGroups }[] = [
  { type: 'v', label: '视频流', key: 'video' },
  { type: 'a', label: '音频流', key: 'audio' },
  { type: 's', label: '字幕流', key: 'subtitle' },
]

async function loadFile(path: string): Promise<FileEntry> {
  const empty: StreamGroups = { video: [], audio: [], subtitle: [] }
  try {
    // 服务端 /api/probe 直接返回 ffprobe 的 JSON（request() 已做 JSON 解析）
    const data = await api.probe.info(path) as unknown as { streams?: ProbeStream[] }
    const groups: StreamGroups = { video: [], audio: [], subtitle: [] }
    for (const stream of data.streams ?? []) {
      if (stream.codec_type === 'video') groups.video.push(stream)
      else if (stream.codec_type === 'audio') groups.audio.push(stream)
      else if (stream.codec_type === 'subtitle') groups.subtitle.push(stream)
    }
    return { path, streams: groups, error: '' }
  } catch (error) {
    return { path, streams: empty, error: error instanceof Error ? error.message : String(error) }
  }
}

async function load() {
  loading.value = true
  const sources = [...pending.files, ...manualPaths.value]
  files.value = await Promise.all(sources.map(loadFile))
  applyPresetTokens()
  loading.value = false
}

function describe(stream: ProbeStream): string {
  const parts = [stream.codec_name ?? '未知编码']
  if (stream.tags?.language) parts.push(stream.tags.language)
  if (stream.tags?.title) parts.push(stream.tags.title)
  return parts.join(' · ')
}

function isChecked(fileIdx: number, type: string, ord: number): boolean {
  return selected.value.has(`${fileIdx}:${type}:${ord}`)
}

function toggle(fileIdx: number, type: string, ord: number, on: boolean) {
  const next = new Set(selected.value)
  const key = `${fileIdx}:${type}:${ord}`
  if (on) next.add(key)
  else next.delete(key)
  selected.value = next
}

/** 回显：解析三个字段现有的选择器串（支持 0:v:0 显式与 0:v 整类简写） */
function applyPresetTokens() {
  const next = new Set<string>()
  const specs: { type: string; path: string }[] = [
    { type: 'v', path: '流控制_将视频参数应用于指定流' },
    { type: 'a', path: '流控制_将音频参数应用于指定流' },
    { type: 's', path: '流控制_将字幕参数应用于指定流' },
  ]
  for (const spec of specs) {
    const raw = String(getByPath(props.preset, spec.path) ?? '').trim()
    if (!raw) continue
    for (const token of raw.split(',')) {
      const match = /^(\d+):([vas])(?::(\d+))?$/.exec(token.trim())
      if (!match) continue
      const fileIdx = Number(match[1])
      const type = match[2]
      const ord = match[3]
      if (type !== spec.type) continue
      const file = files.value[fileIdx]
      if (!file) continue
      const key = type === 'v' ? 'video' : type === 'a' ? 'audio' : 'subtitle'
      if (ord === undefined) {
        // 简写 0:v = 该文件全部该类型流
        for (let i = 0; i < file.streams[key].length; i++) next.add(`${fileIdx}:${type}:${i}`)
      } else {
        next.add(`${fileIdx}:${type}:${Number(ord)}`)
      }
    }
  }
  selected.value = next
}

function addManual(path: string) {
  if (!manualPaths.value.includes(path)) manualPaths.value.push(path)
  manualPicker.value = false
  load()
}

/** 确认：按类型分组写回三个字段（按 文件索引→类型内序号 排序） */
function writeBack() {
  const buckets: Record<string, string[]> = { v: [], a: [], s: [] }
  for (const key of selected.value) {
    const [fileIdx, type, ord] = key.split(':')
    if (buckets[type]) buckets[type].push(`${fileIdx}:${type}:${ord}`)
  }
  for (const type of ['v', 'a', 's']) {
    buckets[type].sort((x, y) => {
      const [fx, , ox] = x.split(':').map(Number)
      const [fy, , oy] = y.split(':').map(Number)
      return fx !== fy ? fx - fy : (ox ?? 0) - (oy ?? 0)
    })
    const path = type === 'v' ? '流控制_将视频参数应用于指定流' : type === 'a' ? '流控制_将音频参数应用于指定流' : '流控制_将字幕参数应用于指定流'
    setByPath(props.preset, path, buckets[type])
  }
  toast.push('ok', '已把选中的流写回流控制文本框')
  emit('close')
}

onMounted(load)
</script>

<template>
  <ParamDialog title="可视化流选择器" :width="680" @close="emit('close')">
    <div class="stream-picker">
      <div class="stream-picker-bar">
        <span class="muted">
          {{ pending.files.length > 0 ? `文件来自「准备文件」待处理列表（${pending.files.length} 个），勾选后写回三个文本框` : '待处理文件为空，请先手动添加文件' }}
        </span>
        <button class="small" @click="manualPicker = true">手动添加文件</button>
        <button class="small" @click="load">重新探测</button>
      </div>

      <div v-if="loading" class="stream-picker-empty">正在探测文件流信息…</div>

      <div v-else class="stream-picker-files">
        <div v-for="(file, fileIdx) in files" :key="file.path" class="stream-picker-file">
          <div class="stream-picker-file-head mono">
            <span class="muted">#{{ fileIdx }}</span> {{ file.path }}
            <span v-if="file.error" class="txt-red">（探测失败：{{ file.error }}）</span>
          </div>
          <div class="stream-picker-groups">
            <div v-for="group in GROUPS" :key="group.type" class="stream-picker-group">
              <div class="stream-picker-group-title">{{ group.label }}（{{ file.streams[group.key].length }}）</div>
              <label v-for="(stream, ord) in file.streams[group.key]" :key="ord" class="stream-picker-item">
                <input
                  type="checkbox"
                  :checked="isChecked(fileIdx, group.type, ord)"
                  @change="toggle(fileIdx, group.type, ord, ($event.target as HTMLInputElement).checked)"
                />
                <span class="mono">{{ fileIdx }}:{{ group.type }}:{{ ord }}</span>
                <span class="muted">{{ describe(stream) }}</span>
              </label>
              <div v-if="file.streams[group.key].length === 0" class="muted" style="font-size: 12px">无</div>
            </div>
          </div>
        </div>
        <div v-if="files.length === 0" class="stream-picker-empty">没有可探测的文件</div>
      </div>

      <div class="flex" style="justify-content: flex-end">
        <button class="small" @click="emit('close')">取消</button>
        <button class="small primary" @click="writeBack">写回选择</button>
      </div>
    </div>

    <DirPickerDialog
      v-if="manualPicker"
      :model-value="''"
      title="选择要探测的媒体文件"
      files
      @close="manualPicker = false"
      @pick="addManual"
    />
  </ParamDialog>
</template>
