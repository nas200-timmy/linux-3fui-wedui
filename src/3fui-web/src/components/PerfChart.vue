<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'

export interface PerfChartSeries {
  data: number[]
  color: string
  label: string
  fill?: boolean
}

const props = withDefaults(defineProps<{
  series: PerfChartSeries[]
  height?: number
  // 百分比类图表固定 0-100；留空按峰值自适应
  yMax?: number
  from?: number
  to?: number
  yFormatter?: (value: number) => string
}>(), { height: 240 })

const canvas = ref<HTMLCanvasElement | null>(null)
let observer: ResizeObserver | null = null

function fmtY(value: number): string {
  if (props.yFormatter) return props.yFormatter(value)
  return value >= 1000 ? `${(value / 1000).toFixed(1)}k` : value.toFixed(0)
}

function draw() {
  const el = canvas.value
  if (!el) return
  const ctx = el.getContext('2d')
  if (!ctx) return
  const cssW = el.clientWidth || 600
  const cssH = props.height
  const dpr = window.devicePixelRatio || 1
  if (el.width !== Math.round(cssW * dpr) || el.height !== Math.round(cssH * dpr)) {
    el.width = Math.round(cssW * dpr)
    el.height = Math.round(cssH * dpr)
  }
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
  ctx.clearRect(0, 0, cssW, cssH)

  const padL = 8
  const padR = 56
  const padT = 10
  const padB = 18
  const plotW = Math.max(10, cssW - padL - padR)
  const plotH = Math.max(10, cssH - padT - padB)
  const maxLen = Math.max(0, ...props.series.map(s => s.data.length))
  if (maxLen < 2) {
    ctx.fillStyle = '#6a6a6a'
    ctx.font = '12px sans-serif'
    ctx.fillText('等待数据…', padL, cssH / 2)
    return
  }

  const peak = Math.max(1e-6, ...props.series.flatMap(s => s.data))
  const yTop = props.yMax ?? Math.ceil(peak * 1.15)
  const x = (i: number) => padL + (i / (maxLen - 1)) * plotW
  const y = (v: number) => padT + plotH - (Math.min(v, yTop) / yTop) * plotH

  // 网格 + Y 轴刻度
  ctx.strokeStyle = '#262626'
  ctx.fillStyle = '#6a6a6a'
  ctx.font = '10px sans-serif'
  ctx.lineWidth = 1
  const rows = 4
  for (let r = 0; r <= rows; r++) {
    const gy = padT + (plotH * r) / rows
    ctx.beginPath()
    ctx.moveTo(padL, gy)
    ctx.lineTo(padL + plotW, gy)
    ctx.stroke()
    ctx.fillText(fmtY(yTop * (1 - r / rows)), padL + plotW + 6, gy + 3)
  }
  // X 轴时间刻度（from/to 为 Unix 毫秒）
  if (props.from && props.to && props.to > props.from) {
    const ticks = 4
    for (let t = 0; t <= ticks; t++) {
      const ratio = t / ticks
      const date = new Date(props.from + (props.to - props.from) * ratio)
      const label = `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`
      ctx.fillText(label, Math.min(padL + plotW * ratio, padL + plotW - 28), cssH - 5)
    }
  }

  // 序列：面积填充 + 折线 + 末端当前值
  for (const s of props.series) {
    if (s.data.length < 2) continue
    const offset = maxLen - s.data.length
    if (s.fill) {
      const gradient = ctx.createLinearGradient(0, padT, 0, padT + plotH)
      gradient.addColorStop(0, `${s.color}55`)
      gradient.addColorStop(1, `${s.color}08`)
      ctx.beginPath()
      ctx.moveTo(x(offset), padT + plotH)
      s.data.forEach((v, i) => ctx.lineTo(x(offset + i), y(v)))
      ctx.lineTo(x(offset + s.data.length - 1), padT + plotH)
      ctx.closePath()
      ctx.fillStyle = gradient
      ctx.fill()
    }
    ctx.beginPath()
    s.data.forEach((v, i) => {
      const px = x(offset + i)
      const py = y(v)
      if (i === 0) ctx.moveTo(px, py)
      else ctx.lineTo(px, py)
    })
    ctx.strokeStyle = s.color
    ctx.lineWidth = 1.5
    ctx.stroke()
    const last = s.data[s.data.length - 1]
    ctx.fillStyle = s.color
    ctx.fillText(fmtY(last), padL + plotW + 6, y(last) + 3)
  }
}

onMounted(() => {
  observer = new ResizeObserver(draw)
  if (canvas.value) observer.observe(canvas.value)
  draw()
})
onBeforeUnmount(() => {
  observer?.disconnect()
  observer = null
})
watch(() => [props.series, props.yMax, props.from, props.to], draw, { deep: true })
</script>

<template>
  <canvas ref="canvas" :style="{ width: '100%', height: height + 'px', display: 'block' }" />
</template>
