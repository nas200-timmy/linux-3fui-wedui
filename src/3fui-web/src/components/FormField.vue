<script setup lang="ts">
import { computed, ref } from 'vue'
import ModernComboBox from './ModernComboBox.vue'
import ColorPicker from './ColorPicker.vue'
import type { FieldDef } from '../schema'
import { getByPath, setByPath } from '../schema'

const props = defineProps<{ field: FieldDef; preset: Record<string, unknown> }>()
const emit = defineEmits<{ 'open-dialog': [id: string]; browse: [path: string] }>()

/** 灰色说明：可为固定文本，也可随预设内容变化（原版二级窗口里随滤镜选择变化的参数名） */
const hintText = computed(() => {
  const hint = props.field.hint
  if (typeof hint === 'function') return hint(props.preset)
  return hint ?? ''
})
const hintToneClass = computed(() => (props.field.hintTone ? `txt-${props.field.hintTone}` : ''))

/** label 彩色关键词：highlight 子串按 labelTone 着色（原版流控制页「视频参数/音频参数/字幕」） */
const labelParts = computed<{ text: string; hit: boolean }[]>(() => {
  const label = props.field.label
  const highlight = props.field.highlight
  if (!label || !highlight || !props.field.labelTone) return [{ text: label, hit: false }]
  const index = label.indexOf(highlight)
  if (index < 0) return [{ text: label, hit: false }]
  return [
    { text: label.slice(0, index), hit: false },
    { text: highlight, hit: true },
    { text: label.slice(index + highlight.length), hit: false },
  ].filter(part => part.text !== '')
})
/** inline 与 sideLabel 都是「控件在左、字段名与说明在右」，区别只在是否独占整行 */
const sideLayout = computed(() => Boolean(props.field.inline || props.field.sideLabel))

const value = computed({
  get: () => {
    const raw = getByPath(props.preset, props.field.path)
    if (props.field.type.kind === 'bool') return Boolean(raw)
    if (props.field.type.kind === 'number') return Number(raw ?? 0)
    return (raw ?? '') as string | string[]
  },
  set: (next: unknown) => {
    setByPath(props.preset, props.field.path, next)
  },
})

// 编辑期间用草稿值，焦点离开才提交——否则受控重组会在输入逗号的瞬间把逗号吞掉（无法手输多项）
const stringListFocused = ref(false)
const stringListDraft = ref('')

function onStringListFocus(event: Event) {
  stringListFocused.value = true
  stringListDraft.value = (event.target as HTMLInputElement).value
}

function onStringListInput(event: Event) {
  stringListDraft.value = (event.target as HTMLInputElement).value
}

function onStringListBlur(event: Event) {
  const text = (event.target as HTMLInputElement).value
  setByPath(props.preset, props.field.path, text.split(',').map(s => s.trim()).filter(s => s !== ''))
  stringListFocused.value = false
}

const stringListText = computed(() => (value.value as string[]).join(', '))
const stringListInputValue = computed(() => (stringListFocused.value ? stringListDraft.value : stringListText.value))
const numberValue = computed({
  get: () => value.value as number,
  set: (next: unknown) => {
    setByPath(props.preset, props.field.path, Number(next) || 0)
  },
})

const selectType = computed(() => (props.field.type.kind === 'select' ? props.field.type : null))
const selectOptions = computed(() => selectType.value?.options ?? [])
const selectWidth = computed(() => selectType.value?.width ?? props.field.width ?? 150)
const selectWatermark = computed(() => selectType.value?.placeholder ?? props.field.placeholder ?? props.field.label)
const selectText = computed(() => (value.value as string))
const widthStyle = computed(() =>
  props.field.width === undefined
    ? undefined
    : { width: typeof props.field.width === 'number' ? `${props.field.width}px` : props.field.width },
)

function onSelect(next: string) {
  setByPath(props.preset, props.field.path, next)
}
</script>

<template>
  <div class="field-item" :class="{ full: field.full, bare: field.bare, inline: field.inline, 'side-label': field.sideLabel }">
    <div v-if="!sideLayout && (field.label || hintText || field.info)" class="field-head">
      <span v-if="field.label" class="field-label"><template v-for="(part, i) in labelParts" :key="i"><span v-if="part.hit" :class="`txt-${field.labelTone}`">{{ part.text }}</span><template v-else>{{ part.text }}</template></template></span>
      <span v-if="hintText" class="field-hint" :class="hintToneClass">{{ hintText }}</span>
      <span v-if="field.info" class="info-dot">i<span class="info-tip">{{ field.info }}</span></span>
    </div>

    <div class="field-control">
      <button
        v-if="field.button && !field.button.after"
        class="param-btn"
        @click="emit('open-dialog', field.button.dialog)"
      >{{ field.button.text }}</button>
      <input
        v-if="field.type.kind === 'text'"
        v-model="(value as string)"
        type="text"
        :style="widthStyle"
        :placeholder="field.placeholder ?? field.type.placeholder ?? ''"
      />
      <textarea
        v-else-if="field.type.kind === 'textarea'"
        v-model="(value as string)"
        :rows="field.type.rows ?? 2"
        :placeholder="field.type.placeholder ?? ''"
      />
      <ModernComboBox
        v-else-if="selectType"
        :model-value="selectText"
        :options="selectOptions"
        :watermark="selectWatermark"
        :width="selectWidth"
        :editable="selectType.editable ?? false"
        :max-items="selectType.maxItems ?? 8"
        :tooltip-side="selectType.tooltipSide ?? 'right'"
        :tooltip-max-width="selectType.tooltipMaxWidth ?? 350"
        @update:model-value="onSelect"
      />
      <label v-else-if="field.type.kind === 'bool'" class="checkbox-row">
        <input v-model="(value as boolean)" type="checkbox" />
        <span>{{ (value as boolean) ? '启用' : '禁用' }}</span>
      </label>
      <input
        v-else-if="field.type.kind === 'number'"
        v-model="numberValue"
        type="number"
        step="any"
      />
      <input
        v-else-if="field.type.kind === 'stringlist'"
        :value="stringListInputValue"
        type="text"
        placeholder="逗号分隔"
        @focus="onStringListFocus"
        @input="onStringListInput"
        @blur="onStringListBlur"
      />
      <ColorPicker v-else-if="field.type.kind === 'color'" v-model="(value as unknown as Record<string, unknown>)" />
      <button
        v-if="field.browse === 'dir'"
        class="small"
        @click="emit('browse', field.path)"
      >浏览…</button>
      <button
        v-if="field.button && field.button.after"
        class="param-btn"
        @click="emit('open-dialog', field.button.dialog)"
      >{{ field.button.text }}</button>
    </div>

    <template v-if="sideLayout">
      <span v-if="field.label" class="field-label"><template v-for="(part, i) in labelParts" :key="i"><span v-if="part.hit" :class="`txt-${field.labelTone}`">{{ part.text }}</span><template v-else>{{ part.text }}</template></template></span>
      <span v-if="hintText" class="field-hint" :class="hintToneClass">{{ hintText }}</span>
      <span v-if="field.info" class="info-dot">i<span class="info-tip">{{ field.info }}</span></span>
    </template>
  </div>
</template>

