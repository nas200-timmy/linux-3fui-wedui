<script setup lang="ts">
// 竖向滑杆卡片：原版简易调色 / 响度标准化的图形化组件。
// 安全语义：拖拽不自动勾选启用（核心双保险门控：排序条目看启用 bool，滤镜构造看 启用 AndAlso 值非空）；
// 仅拖拽时写值；双击复位写回核心同款默认串。
import { computed, ref } from 'vue'
import type { SliderDef } from '../schema'
import { getByPath, setByPath } from '../schema'

const props = defineProps<{ slider: SliderDef; preset: Record<string, unknown> }>()

const enabled = computed(() => Boolean(getByPath(props.preset, props.slider.enablePath)))
const defNum = computed(() => parseFloat(props.slider.def))

const num = computed(() => {
  const raw = getByPath(props.preset, props.slider.valuePath)
  if (raw === undefined || raw === null || String(raw).trim() === '') return defNum.value
  const parsed = parseFloat(String(raw))
  if (Number.isNaN(parsed)) return defNum.value
  return Math.min(props.slider.max, Math.max(props.slider.min, parsed))
})

const badgeText = computed(() => String(parseFloat(num.value.toFixed(props.slider.decimals))))

/** 值 → 轨道顶部偏移比例（0=顶=max，1=底=min） */
const frac = computed(() => (props.slider.max - num.value) / (props.slider.max - props.slider.min))

const track = ref<HTMLElement | null>(null)

function write(value: number) {
  const clamped = Math.min(props.slider.max, Math.max(props.slider.min, value))
  const snapped = Math.round((clamped - props.slider.min) / props.slider.step) * props.slider.step + props.slider.min
  const fixed = parseFloat(snapped.toFixed(props.slider.decimals + 2))
  setByPath(props.preset, props.slider.valuePath, String(parseFloat(fixed.toFixed(props.slider.decimals))))
}

function valueFromEvent(event: PointerEvent): number {
  const el = track.value
  if (!el) return num.value
  const rect = el.getBoundingClientRect()
  const ratio = Math.min(1, Math.max(0, (event.clientY - rect.top) / rect.height))
  return props.slider.max - ratio * (props.slider.max - props.slider.min)
}

let dragging = false

function onPointerDown(event: PointerEvent) {
  dragging = true
  ;(event.target as HTMLElement).setPointerCapture(event.pointerId)
  write(valueFromEvent(event))
}

function onPointerMove(event: PointerEvent) {
  if (dragging) write(valueFromEvent(event))
}

function onPointerUp() {
  dragging = false
}

/** 双击复位：写回核心同款默认串（与滤镜排序页移除条目时核心的重置值一致） */
function onReset() {
  setByPath(props.preset, props.slider.valuePath, props.slider.def)
}

function toggle(event: Event) {
  setByPath(props.preset, props.slider.enablePath, (event.target as HTMLInputElement).checked)
}

const marks = computed(() => props.slider.marks ?? [])
const showEdgeLabels = computed(() => marks.value.length === 0)
</script>

<template>
  <div class="slider-card" :class="{ disabled: !enabled }">
    <div class="slider-head">
      <span v-if="showEdgeLabels && slider.topLabel" class="slider-edge muted">{{ slider.topLabel }}</span>
      <span v-else class="slider-edge muted" />
      <span class="slider-badge" :class="`txt-${slider.tone}`">{{ badgeText }}</span>
    </div>

    <div class="slider-body">
      <div v-if="marks.length" class="slider-marks">
        <span
          v-for="(mark, i) in marks"
          :key="i"
          class="slider-mark"
          :class="mark.tone ? `txt-${mark.tone}` : ''"
          :style="{ top: `${((slider.max - mark.value) / (slider.max - slider.min)) * 100}%` }"
        >{{ mark.label }}</span>
      </div>
      <div
        ref="track"
        class="slider-track"
        @pointerdown="onPointerDown"
        @pointermove="onPointerMove"
        @pointerup="onPointerUp"
        @dblclick="onReset"
      >
        <div class="slider-fill" :style="{ height: `${frac * 100}%` }" />
        <div class="slider-thumb" :class="`badge-${slider.tone}`" :style="{ top: `${frac * 100}%` }" />
      </div>
      <div class="slider-gutter" />
    </div>

    <div class="slider-foot">
      <span v-if="showEdgeLabels && slider.bottomLabel" class="slider-edge muted">{{ slider.bottomLabel }}</span>
    </div>

    <label class="slider-enable">
      <input type="checkbox" :checked="enabled" @change="toggle" />
      <span>{{ slider.label }}<template v-if="slider.unit">（{{ slider.unit }}）</template></span>
    </label>
  </div>
</template>
