<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{ modelValue: Record<string, unknown> }>()
const emit = defineEmits<{ 'update:modelValue': [value: Record<string, unknown>] }>()

const hex = computed(() => {
  const toHex = (v: unknown) => Number(v ?? 0).toString(16).padStart(2, '0')
  return `#${toHex(props.modelValue.R)}${toHex(props.modelValue.G)}${toHex(props.modelValue.B)}`
})

function onToggle(event: Event) {
  emit('update:modelValue', { ...props.modelValue, 已设置: (event.target as HTMLInputElement).checked })
}

function onHex(event: Event) {
  const value = (event.target as HTMLInputElement).value
  const match = /^#?([0-9a-fA-F]{6})$/.exec(value)
  if (!match) return
  const number = parseInt(match[1], 16)
  emit('update:modelValue', { ...props.modelValue, R: (number >> 16) & 255, G: (number >> 8) & 255, B: number & 255 })
}
</script>

<template>
  <div class="flex">
    <input type="checkbox" :checked="Boolean(modelValue.已设置)" @change="onToggle" />
    <input type="color" :value="hex" @input="onHex" style="width:46px;height:28px;padding:0;border:none;background:transparent;cursor:pointer" />
    <input type="text" :value="hex" @input="onHex" class="mono" style="width:88px" />
  </div>
</template>
