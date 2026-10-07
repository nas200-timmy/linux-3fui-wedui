<script setup lang="ts">
// 带行号的多行编辑器（原版自定义参数页的大编辑框）：行号列与编辑区滚动同步。
import { computed, nextTick, ref } from 'vue'

const props = withDefaults(defineProps<{ modelValue: string; rows?: number; placeholder?: string }>(), { rows: 6 })
const emit = defineEmits<{ 'update:modelValue': [value: string] }>()

const lineCount = computed(() => Math.max(1, props.modelValue.split('\n').length))
const gutter = ref<HTMLElement | null>(null)
const input = ref<HTMLTextAreaElement | null>(null)

function onScroll(event: Event) {
  if (gutter.value) gutter.value.scrollTop = (event.target as HTMLTextAreaElement).scrollTop
}

function onInput(event: Event) {
  emit('update:modelValue', (event.target as HTMLTextAreaElement).value)
}

/** 在光标处插入文本（原版「插入输入文件 / 插入输出文件」按钮） */
function insert(text: string) {
  const el = input.value
  if (!el) return
  const start = el.selectionStart ?? el.value.length
  const end = el.selectionEnd ?? start
  emit('update:modelValue', el.value.slice(0, start) + text + el.value.slice(end))
  nextTick(() => {
    el.focus()
    const pos = start + text.length
    el.setSelectionRange(pos, pos)
  })
}

defineExpose({ insert })
</script>

<template>
  <div class="code-textarea">
    <div ref="gutter" class="code-textarea-gutter">
      <div v-for="n in lineCount" :key="n">{{ n }}</div>
    </div>
    <textarea
      ref="input"
      class="code-textarea-input mono"
      :value="modelValue"
      :rows="rows"
      :placeholder="placeholder"
      spellcheck="false"
      @input="onInput"
      @scroll="onScroll"
    />
  </div>
</template>
