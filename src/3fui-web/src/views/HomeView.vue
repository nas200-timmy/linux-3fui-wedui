<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { api, type PresetData } from '../api'

const emit = defineEmits<{ apply: [preset: PresetData, name: string] }>()
const builtin = ref<{ 名称: string; 数据: PresetData }[]>([])
const status = ref<Record<string, unknown>>({})
const ffmpegInfo = ref<{ version: string; encoders: string; filters: string } | null>(null)
const userPresetCount = ref(0)
const queueCount = ref(0)
const videoEncoderCount = ref(0)
const audioEncoderCount = ref(0)
const encoderCount = computed(() => videoEncoderCount.value + audioEncoderCount.value)

const tls = computed(() => (status.value['tls'] as Record<string, unknown> | undefined) ?? {})

// 从 ffmpeg -encoders 输出里探测可用的硬件编码器
const hwEncoders = computed(() => {
  const encoders = ffmpegInfo.value?.encoders ?? ''
  const probes: { key: string; label: string }[] = [
    { key: 'h264_nvenc', label: 'NVIDIA NVENC' },
    { key: 'h264_qsv', label: 'Intel QSV' },
    { key: 'h264_vaapi', label: 'VAAPI' },
    { key: 'h264_amf', label: 'AMD AMF' },
    { key: 'h264_videotoolbox', label: 'VideoToolbox' },
  ]
  return probes.filter(probe => new RegExp(`\\b${probe.key}\\b`).test(encoders)).map(probe => probe.label)
})

const ffmpegVersion = computed(() => (ffmpegInfo.value?.version ?? '').split('\n')[0] || '未检测到 ffmpeg')

function apply(item: { 名称: string; 数据: PresetData }) {
  emit('apply', item.数据, item.名称)
}

onMounted(() => {
  api.presets.builtin().then(list => { builtin.value = list }).catch(() => {})
  api.presets.list().then(list => { userPresetCount.value = list.user.length }).catch(() => {})
  api.queue.list().then(list => { queueCount.value = list.length }).catch(() => {})
  api.status().then(data => { status.value = data }).catch(() => {})
  api.encoderDb().then(db => {
    videoEncoderCount.value = Object.keys((db.videoEncoders ?? {}) as Record<string, unknown>).length
    audioEncoderCount.value = Object.keys((db.audioEncoders ?? {}) as Record<string, unknown>).length
  }).catch(() => {})
  fetch('/api/ffmpeg/info').then(r => r.json()).then(data => { ffmpegInfo.value = data }).catch(() => {})
})
</script>

<template>
  <div>
    <!-- 欢迎横幅 -->
    <div class="panel-box panel" style="margin-bottom: 10px; display: flex; align-items: center; gap: 12px">
      <div class="home-logo">3F<br />UI</div>
      <div style="flex: 1; min-width: 0">
        <div style="font-size: 17px; color: var(--text)">FFmpegFreeUI · Linux 网页版</div>
        <div class="muted" style="margin-top: 3px">
          面向 NAS 的批量转码服务：容器内自带 <span class="txt-blue">ffmpeg</span> / <span class="txt-blue">ffprobe</span>，浏览器直接操作，预设文件与 Windows 版 3FUI 双向兼容
        </div>
      </div>
      <div style="text-align: right; flex: none">
        <div class="muted">媒体根目录 {{ (status['mediaRoot'] as string) ?? '/media' }}</div>
        <div style="margin-top: 5px">
          <span class="badge" :class="status['ffmpeg'] ? 'done' : 'error'">ffmpeg {{ (status['ffmpeg'] as string) ?? '未检测' }}</span>
          <span class="badge" :class="tls.active ? 'done' : 'pending'" style="margin-left: 6px">
            HTTPS {{ tls.active ? `已启用 ${tls.port}` : '未启用' }}
          </span>
        </div>
      </div>
    </div>

    <div class="grid-3">
      <!-- 左：快速上手 -->
      <div class="panel-box panel" style="min-height: 300px; overflow: auto">
        <div class="overview-head" style="padding: 4px 0 10px">快速上手（默认流程）</div>
        <div class="home-step">
          <b class="txt-blue">1 · 准备文件</b>
          <span>在「准备文件」里浏览挂载的媒体目录（默认 <code class="mono">/media</code>），勾选要转码的文件；支持整目录递归加入。</span>
        </div>
        <div class="home-step">
          <b class="txt-blue">2 · 选编码器</b>
          <span>「参数面板 → 视频参数 | 编码器」选一个：本机核显硬编选 <b>hevc_vaapi</b>（快），要极限压缩率选 <b>libx265</b>（CPU 慢但小）。</span>
        </div>
        <div class="home-step">
          <b class="txt-blue">3 · 定质量（只需设一次）</b>
          <span>「视频参数 | 质量」控制方式选 <b>CQP</b>（VAAPI 用 <code class="mono">-qp</code>）或 <b>CRF</b>（x265/x264 用 <code class="mono">-crf</code>），值 24~28：越小越清晰、越大越小。</span>
        </div>
        <div class="home-step">
          <b class="txt-blue">4 · 选容器并加入队列</b>
          <span>「输出文件设置」选输出容器（<b>mkv 推荐</b>，什么都能装），点「加入编码队列」即自动开始，队列页看进度与日志。</span>
        </div>
        <div class="section-divider" />
        <div class="home-step">
          <b class="txt-green">默认就安全</b>
          <span>其余一律不动：<b>全部音轨复制保留、全部字幕保留、元数据/章节/附件保留</b>（刮削不受影响），分辨率/帧率/色彩/滤镜全部跟随源视频。要调这些时再去对应页面。</span>
        </div>
        <div class="home-step">
          <b class="txt-gold">提示</b>
          <span>「参数面板 → 参数总览」实时显示当前参数生成的 ffmpeg 命令行；内置预设可一键载入，预设文件与 Windows 版 3FUI 双向兼容。</span>
        </div>
      </div>

      <!-- 中：环境状态 -->
      <div class="panel-box panel" style="min-height: 300px; overflow: auto">
        <div class="overview-head" style="padding: 4px 0 10px">环境状态</div>
        <div class="home-kv"><span>ffmpeg 版本</span><b class="mono">{{ (status['ffmpeg'] as string) ?? '—' }}</b></div>
        <div class="home-kv"><span>ffprobe</span><b class="mono">{{ (status['ffprobe'] as string) ?? '—' }}</b></div>
        <div class="home-kv"><span>HTTPS</span>
          <b :class="tls.active ? 'txt-green' : 'txt-gold'">
            {{ tls.active ? `已启用（端口 ${tls.port}${tls.subject ? '，' + tls.subject : ''}）` : '未启用（在「软件设置」上传证书即可）' }}
          </b>
        </div>
        <div class="home-kv"><span>媒体根目录</span><b class="mono">{{ (status['mediaRoot'] as string) ?? '/media' }}</b></div>
        <div class="home-kv"><span>数据目录</span><b class="mono">{{ (status['dataDir'] as string) ?? '/data' }}</b></div>
        <div class="home-kv"><span>硬件编码器</span>
          <b :class="hwEncoders.length ? 'txt-green' : 'txt-gold'">
            {{ hwEncoders.length ? hwEncoders.join(' / ') + ' 可用' : '仅 CPU 软编（未检测到硬件编码器）' }}
          </b>
        </div>
        <div class="section-divider" />
        <div class="home-kv"><span>内置预设</span><b class="mono">{{ builtin.length }} 个</b></div>
        <div class="home-kv"><span>我的预设</span><b class="mono">{{ userPresetCount }} 个</b></div>
        <div class="home-kv"><span>队列任务</span><b class="mono">{{ queueCount }} 个</b></div>
        <div class="home-kv"><span>可用编码器</span><b class="mono">{{ encoderCount }} 个（视频 {{ videoEncoderCount }} / 音频 {{ audioEncoderCount }}）</b></div>
        <div class="section-divider" />
        <div class="muted" style="line-height: 1.8">
          当前 ffmpeg 由镜像内置（Debian 官方构建，含 x264/x265/SVT-AV1/VP9 等）。如需自定义 ffmpeg，
          可在「软件设置 → 替代进程文件名」指定宿主机包装脚本路径。
        </div>
        <div class="log-view" style="margin-top: 8px; max-height: 96px">{{ ffmpegVersion }}</div>
      </div>

      <!-- 右：内置预设推荐 -->
      <div class="panel-box panel" style="min-height: 300px; display: flex; flex-direction: column">
        <div class="overview-head" style="padding: 4px 0 10px">内置预设推荐</div>
        <div class="muted" style="margin-bottom: 6px; flex: none">点击条目即可载入到「参数面板」</div>
        <div style="flex: 1; min-height: 0; overflow: auto">
          <div v-for="item in builtin" :key="item.名称" class="preset-card" @click="apply(item)">
            <div>{{ item.名称 }}</div>
            <div class="muted" style="font-size: 12px; line-height: 1.4">{{ (item.数据['预设备注'] as string) ?? '' }}</div>
          </div>
          <div v-if="builtin.length === 0" class="empty">暂无内置预设</div>
        </div>
      </div>
    </div>
  </div>
</template>
