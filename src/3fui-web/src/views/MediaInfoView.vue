<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { api, copyToClipboard, errorText } from '../api'
import { useToast } from '../store'

interface Entry { name: string; path: string; isDirectory: boolean; isMedia: boolean; size?: number }
interface StreamInfo {
  index?: number
  codec_type?: string
  codec_name?: string
  codec_long_name?: string
  profile?: string
  level?: number
  width?: number
  height?: number
  r_frame_rate?: string
  avg_frame_rate?: string
  sample_rate?: string
  channels?: number
  channel_layout?: string
  bit_rate?: string
  pix_fmt?: string
  bits_per_raw_sample?: string
  tags?: Record<string, string>
  disposition?: Record<string, number>
}
interface FormatInfo {
  filename?: string
  format_name?: string
  format_long_name?: string
  duration?: string
  size?: string
  bit_rate?: string
  nb_streams?: number
  tags?: Record<string, string>
}

const toast = useToast()
const currentPath = ref('/media')
const parent = ref<string | null>(null)
const entries = ref<Entry[]>([])
const probePath = ref('')
const probeInfo = ref<Record<string, unknown> | null>(null)
const probeError = ref('')
const loading = ref(false)
const showRaw = ref(false)

const format = computed<FormatInfo | null>(() => (probeInfo.value?.['format'] as FormatInfo) ?? null)
const streams = computed<StreamInfo[]>(() => (probeInfo.value?.['streams'] as StreamInfo[] | undefined) ?? [])

function fmtDuration(seconds?: number | string) {
  const total = Number(seconds ?? 0)
  if (!total || Number.isNaN(total)) return '—'
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  return `${h > 0 ? String(h).padStart(2, '0') + ':' : ''}${String(m).padStart(2, '0')}:${s.toFixed(2).padStart(5, '0')}`
}

function fmtSize(bytes?: number | string) {
  const value = Number(bytes ?? 0)
  if (!value) return '—'
  if (value >= 1073741824) return (value / 1073741824).toFixed(2) + ' GB'
  if (value >= 1048576) return (value / 1048576).toFixed(1) + ' MB'
  if (value >= 1024) return (value / 1024).toFixed(0) + ' KB'
  return value + ' B'
}

function fmtBitrate(bits?: number | string) {
  const value = Number(bits ?? 0)
  if (!value) return '—'
  return value >= 1000000 ? (value / 1000000).toFixed(2) + ' Mbps' : Math.round(value / 1000) + ' kbps'
}

function fmtFps(rate?: string) {
  if (!rate) return '—'
  const [num, den] = rate.split('/').map(Number)
  if (!den || den === 0) return '—'
  const fps = num / den
  return fps > 0 ? fps.toFixed(3) + ' fps' : '—'
}

function streamSummary(stream: StreamInfo) {
  if (stream.codec_type === 'video') {
    const size = stream.width && stream.height ? `${stream.width}×${stream.height}` : ''
    return [size, stream.pix_fmt, stream.bits_per_raw_sample ? stream.bits_per_raw_sample + 'bit' : ''].filter(Boolean).join(' · ')
  }
  if (stream.codec_type === 'audio') {
    const rate = stream.sample_rate ? Number(stream.sample_rate) / 1000 + ' kHz' : ''
    const ch = stream.channels ? `${stream.channels} 声道` : stream.channel_layout ?? ''
    return [rate, ch].filter(Boolean).join(' · ')
  }
  return stream.codec_long_name ?? ''
}

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

function probe(path: string) {
  loading.value = true
  probeError.value = ''
  probeInfo.value = null
  probePath.value = path
  showRaw.value = false
  api.probe.info(path)
    .then(info => {
      if (info && typeof info === 'object' && 'error' in info) probeError.value = errorText((info as { error: unknown }).error)
      else probeInfo.value = info
    })
    .catch(error => { probeError.value = errorText(error) })
    .finally(() => { loading.value = false })
}

function copyRaw() {
  if (!probeInfo.value) return
  // 走 api 的双路径兜底：HTTP/IP 直连等非安全上下文下 navigator.clipboard 不可用
  copyToClipboard(JSON.stringify(probeInfo.value, null, 2))
    .then(ok => toast.push(ok ? 'ok' : 'err', ok ? '完整 JSON 已复制' : '复制失败'))
}

onMounted(() => browse())
</script>

<template>
  <div class="fill-col">
    <div class="page-header">
      <h2>ffprobe 媒体信息</h2>
      <span class="desc">查看媒体文件的封装、流、编码与时长等完整信息</span>
      <span class="spacer" />
      <button class="small" :disabled="!probeInfo" @click="copyRaw">复制完整 JSON</button>
      <button class="small" :disabled="!probeInfo" @click="showRaw = !showRaw">{{ showRaw ? '显示摘要' : '显示原始 JSON' }}</button>
    </div>

    <div style="display: flex; gap: 10px; flex: 1; min-height: 0">
      <!-- 文件浏览 -->
      <div class="panel-box" style="width: 340px; flex: none; display: flex; flex-direction: column; min-height: 0">
        <div class="panel" style="padding-bottom: 8px">
          <div class="flex" style="margin-bottom: 8px">
            <button class="small" :disabled="!parent" @click="browse(parent ?? undefined)">↑ 上级</button>
            <button class="small" @click="browse(currentPath)">刷新</button>
          </div>
          <input
            type="text"
            :value="currentPath"
            class="mono"
            style="width: 100%"
            @keydown.enter="browse(($event.target as HTMLInputElement).value)"
          />
        </div>
        <div class="fill-scroll">
          <table class="list">
            <tbody>
              <tr v-for="entry in entries" :key="entry.path" :class="{ selected: entry.path === probePath }"
                  @click="entry.isDirectory ? browse(entry.path) : entry.isMedia && probe(entry.path)">
                <td style="width: 20px">
                  <span v-if="entry.isDirectory" class="txt-blue">▸</span>
                  <span v-else-if="entry.isMedia" class="muted">▹</span>
                </td>
                <td>
                  <span v-if="entry.isDirectory" class="txt-blue">{{ entry.name }}/</span>
                  <span v-else-if="entry.isMedia">{{ entry.name }}</span>
                  <span v-else class="muted">{{ entry.name }}</span>
                </td>
                <td class="mono muted" style="width: 86px; text-align: right">{{ entry.isDirectory ? '' : fmtSize(entry.size) }}</td>
              </tr>
              <tr v-if="entries.length === 0"><td colspan="3" class="empty">空目录</td></tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- 信息详情 -->
      <div class="panel-box fill-scroll" style="flex: 1; min-width: 0">
        <div v-if="loading" class="empty">正在解析…</div>

        <div v-else-if="probeError" class="panel">
          <div class="panel-title">解析失败</div>
          <div class="log-view" style="max-height: 200px"><span class="hl-error">{{ probeError }}</span></div>
          <div class="muted" style="margin-top: 8px">
            常见原因：文件在容器内不可读（权限/路径）、文件扩展名与实际格式不符、或该文件不是有效媒体。
          </div>
          <button class="small" style="margin-top: 8px" @click="probe(probePath)">重试</button>
        </div>

        <template v-else-if="probeInfo">
          <!-- 原始 JSON -->
          <div v-if="showRaw" class="panel">
            <div class="panel-title">原始 JSON（{{ probePath }}）</div>
            <div class="log-view" style="max-height: 62vh">{{ JSON.stringify(probeInfo, null, 2) }}</div>
          </div>

          <!-- 摘要 -->
          <template v-else>
            <div class="panel">
              <div class="panel-title">封装信息</div>
              <div class="probe-grid">
                <div class="probe-kv"><span>文件</span><b class="mono">{{ format?.filename ?? probePath }}</b></div>
                <div class="probe-kv"><span>容器</span><b>{{ format?.format_long_name ?? format?.format_name ?? '—' }}</b></div>
                <div class="probe-kv"><span>时长</span><b class="mono">{{ fmtDuration(format?.duration) }}</b></div>
                <div class="probe-kv"><span>大小</span><b class="mono">{{ fmtSize(format?.size) }}</b></div>
                <div class="probe-kv"><span>总码率</span><b class="mono">{{ fmtBitrate(format?.bit_rate) }}</b></div>
                <div class="probe-kv"><span>流数量</span><b class="mono">{{ format?.nb_streams ?? streams.length }}</b></div>
              </div>
            </div>

            <div class="panel" style="padding-top: 0">
              <div class="panel-title">流信息（{{ streams.length }} 条）</div>
              <table class="list">
                <thead>
                  <tr>
                    <th style="width: 40px">#</th>
                    <th style="width: 70px">类型</th>
                    <th style="width: 190px">编码</th>
                    <th>规格</th>
                    <th style="width: 110px">帧率</th>
                    <th style="width: 100px">码率</th>
                    <th style="width: 150px">语言 / 标题</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="stream in streams" :key="stream.index">
                    <td class="mono">{{ stream.index }}</td>
                    <td>
                      <span :class="stream.codec_type === 'video' ? 'txt-blue' : stream.codec_type === 'audio' ? 'txt-green' : 'txt-purple'">
                        {{ stream.codec_type === 'video' ? '视频' : stream.codec_type === 'audio' ? '音频' : stream.codec_type === 'subtitle' ? '字幕' : stream.codec_type }}
                      </span>
                    </td>
                    <td>
                      <div>{{ stream.codec_name }}</div>
                      <div class="muted" style="font-size: 11px">{{ stream.profile }}{{ stream.level ? ' @L' + stream.level : '' }}</div>
                    </td>
                    <td class="muted" style="font-size: 12px">{{ streamSummary(stream) }}</td>
                    <td class="mono">{{ stream.codec_type === 'video' ? fmtFps(stream.r_frame_rate ?? stream.avg_frame_rate) : '—' }}</td>
                    <td class="mono">{{ fmtBitrate(stream.bit_rate) }}</td>
                    <td class="muted" style="font-size: 12px">
                      <span v-if="stream.tags?.language">{{ stream.tags.language }}</span>
                      <span v-if="stream.tags?.title"> · {{ stream.tags.title }}</span>
                      <span v-if="stream.disposition?.default === 1" class="txt-gold" style="margin-left: 4px">默认</span>
                    </td>
                  </tr>
                  <tr v-if="streams.length === 0"><td colspan="7" class="empty">该文件没有可用的流信息</td></tr>
                </tbody>
              </table>
            </div>
          </template>
        </template>

        <div v-else class="empty">点击左侧媒体文件查看完整 ffprobe 信息</div>
      </div>
    </div>
  </div>
</template>
