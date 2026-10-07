<!-- 自绘下拉框：对齐原版 LakeUI ModernComboBox
     触发框 32 高 / 圆角 10 / 底 #373737 / 右侧 15×13 银色三角
     展开面板与说明浮窗用 Teleport 挂到 body 并 fixed 定位——原版同样用独立浮窗，避免被面板滚动容器裁剪 -->
<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'

export interface ComboOption {
  value: string
  label: string
  hint?: string
}

const props = withDefaults(defineProps<{
  modelValue: string
  options: ComboOption[]
  watermark?: string
  width?: number | string
  editable?: boolean
  maxItems?: number
  tooltipSide?: 'left' | 'right'
  tooltipMaxWidth?: number
  disabled?: boolean
}>(), {
  watermark: '',
  width: 150,
  editable: false,
  maxItems: 8,
  tooltipSide: 'right',
  tooltipMaxWidth: 350,
  disabled: false,
})

const emit = defineEmits<{
  (event: 'update:modelValue', value: string): void
}>()

const ITEM_H = 30
const PANEL_PAD = 10
const PANEL_BORDER = 1
const HINT_GAP = 8
const EDGE = 6

const root = ref<HTMLDivElement | null>(null)
const list = ref<HTMLDivElement | null>(null)
const hintEl = ref<HTMLDivElement | null>(null)
const open = ref(false)
const hover = ref(-1)
const filter = ref('')
const draft = ref('')
const hintShown = ref(false)
const box = ref({ left: 0, top: 0, width: 150 })
const hintBox = ref({ left: 0, top: 0 })

const triggerWidth = computed(() => (typeof props.width === 'number' ? `${props.width}px` : props.width))

const selectedLabel = computed(() => {
  const hit = props.options.find(o => o.value === props.modelValue)
  return hit ? hit.label : props.modelValue
})

const shown = computed(() => {
  const q = filter.value.trim().toLowerCase()
  if (q === '') return props.options
  return props.options.filter(o =>
    o.label.toLowerCase().includes(q) || o.value.toLowerCase().includes(q))
})

const visibleCount = computed(() => Math.min(Math.max(shown.value.length, 1), props.maxItems))
const panelHeight = computed(() => visibleCount.value * ITEM_H + PANEL_PAD * 2 + PANEL_BORDER * 2)
const selectedIndex = computed(() => shown.value.findIndex(o => o.value === props.modelValue))
const hintText = computed(() => (open.value && hover.value >= 0 ? shown.value[hover.value]?.hint ?? '' : ''))

watch(() => props.modelValue, () => { draft.value = selectedLabel.value }, { immediate: true })
watch(() => selectedLabel.value, () => { if (!open.value) draft.value = selectedLabel.value })

function scrollTo(index: number) {
  const el = list.value
  if (!el || index < 0) return
  const top = index * ITEM_H
  if (top < el.scrollTop) el.scrollTop = top
  else if (top + ITEM_H > el.scrollTop + el.clientHeight) el.scrollTop = top + ITEM_H - el.clientHeight
}

async function place() {
  const trigger = root.value
  if (!trigger) return
  const rect = trigger.getBoundingClientRect()
  const anchor = Math.max(selectedIndex.value, 0)
  // 原版 Overlay：当前选中项与控件竖直中心对齐，越界夹到视口内
  let top = rect.top + (rect.height - ITEM_H) / 2 - PANEL_BORDER - PANEL_PAD - anchor * ITEM_H
  top = Math.min(Math.max(top, EDGE), Math.max(EDGE, window.innerHeight - panelHeight.value - EDGE))
  box.value = { left: rect.left, top, width: rect.width }
  await nextTick()
  scrollTo(anchor)
  placeHint()
}

function placeHint() {
  const text = hintText.value
  if (text === '' || !list.value) {
    hintShown.value = false
    return
  }
  const lines = text.split('\n')
  const longest = lines.reduce((max, line) => Math.max(max, line.length), 0)
  const width = Math.min(props.tooltipMaxWidth, longest * 12 + 34)
  const height = lines.length * 19 + 32
  const itemTop = box.value.top + PANEL_BORDER + PANEL_PAD + hover.value * ITEM_H - list.value.scrollTop
  const right = box.value.left + box.value.width + HINT_GAP
  let left = props.tooltipSide === 'left' ? box.value.left - HINT_GAP - width : right
  if (left + width > window.innerWidth - EDGE) left = box.value.left - HINT_GAP - width
  if (left < EDGE) left = right
  let top = Math.max(itemTop, EDGE)
  if (top + height > window.innerHeight - EDGE) top = Math.max(EDGE, window.innerHeight - height - EDGE)
  hintBox.value = { left, top }
  hintShown.value = true
}

function openList() {
  if (props.disabled || open.value) return
  open.value = true
  filter.value = ''
  draft.value = selectedLabel.value
  hover.value = selectedIndex.value >= 0 ? selectedIndex.value : (props.options.length > 0 ? 0 : -1)
  place()
  document.addEventListener('pointerdown', onOutside, true)
  window.addEventListener('resize', close)
  window.addEventListener('scroll', onScroll, true)
}

function close() {
  if (!open.value) return
  open.value = false
  hintShown.value = false
  filter.value = ''
  document.removeEventListener('pointerdown', onOutside, true)
  window.removeEventListener('resize', close)
  window.removeEventListener('scroll', onScroll, true)
}

function onOutside(event: PointerEvent) {
  const target = event.target as Node
  if (root.value?.contains(target) || list.value?.contains(target) || hintEl.value?.contains(target)) return
  close()
}

function onScroll(event: Event) {
  const target = event.target as Node
  // 下拉面板自身滚动（滚动到选中项）不关闭面板
  if (list.value?.contains(target) || hintEl.value?.contains(target)) return
  close()
}

function setHover(index: number) {
  hover.value = index
  scrollTo(index)
  placeHint()
}

function move(step: number) {
  const count = shown.value.length
  if (count === 0) return
  setHover(hover.value < 0 ? 0 : (hover.value + step + count) % count)
}

function commit(index: number) {
  const option = shown.value[index]
  if (!option) return
  emit('update:modelValue', option.value)
  close()
  nextTick(() => root.value?.focus())
}

function onInput(event: Event) {
  const text = (event.target as HTMLInputElement).value
  draft.value = text
  filter.value = text
  emit('update:modelValue', text)
  hover.value = 0
  nextTick(() => {
    place()
    setHover(0)
  })
}

function onKeydown(event: KeyboardEvent) {
  if (props.disabled) return
  if (!open.value) {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp' || event.key === 'Enter' || event.key === ' ') {
      event.preventDefault()
      openList()
    }
    return
  }
  switch (event.key) {
    case 'ArrowDown': event.preventDefault(); move(1); break
    case 'ArrowUp': event.preventDefault(); move(-1); break
    case 'Home': event.preventDefault(); setHover(0); break
    case 'End': event.preventDefault(); setHover(shown.value.length - 1); break
    case 'Enter': event.preventDefault(); commit(hover.value); break
    case 'Escape': event.preventDefault(); close(); break
    case 'Tab': close(); break
    default:
      if (!props.editable && (event.key.length === 1 || event.key === 'Backspace' || event.key === 'Delete') && !event.ctrlKey && !event.metaKey) {
        event.preventDefault()
      }
      break
  }
}

function onBlur() {
  if (props.editable && open.value) close()
}

onBeforeUnmount(close)
</script>

<template>
  <div
    ref="root"
    class="mcb"
    :class="{ 'mcb-open': open, 'mcb-disabled': disabled, 'mcb-empty': selectedLabel === '', 'mcb-editable': editable }"
    :style="{ width: triggerWidth }"
    role="combobox"
    :aria-expanded="open"
    :aria-disabled="disabled"
    tabindex="0"
    @click="editable ? (open ? close() : null) : (open ? close() : openList())"
    @keydown="onKeydown"
  >
    <input
      v-if="editable"
      class="mcb-input"
      :value="draft"
      :placeholder="watermark"
      :disabled="disabled"
      @input="onInput"
      @focus="openList"
      @blur="onBlur"
    />
    <span v-else class="mcb-text">{{ selectedLabel === '' ? watermark : selectedLabel }}</span>
    <span class="mcb-arrow" aria-hidden="true" />
  </div>

  <Teleport to="body">
    <div
      v-if="open"
      ref="list"
      class="mcb-panel"
      :style="{ left: box.left + 'px', top: box.top + 'px', width: box.width + 'px', height: panelHeight + 'px' }"
    >
      <div
        v-for="(option, index) in shown"
        :key="option.value + '#' + index"
        class="mcb-item"
        :class="{ 'mcb-item-sel': index === selectedIndex, 'mcb-item-hover': index === hover && index !== selectedIndex }"
        @pointerenter="setHover(index)"
        @click.stop="commit(index)"
      >
        <span class="mcb-item-text">{{ option.label === '' ? (watermark || option.value) : option.label }}</span>
      </div>
      <div v-if="shown.length === 0" class="mcb-item mcb-item-none">（无可选项）</div>
    </div>
  </Teleport>
  <Teleport to="body">
    <div
      v-if="hintShown"
      ref="hintEl"
      class="mcb-hint"
      :style="{ left: hintBox.left + 'px', top: hintBox.top + 'px', maxWidth: tooltipMaxWidth + 'px' }"
    >{{ hintText }}</div>
  </Teleport>
</template>
