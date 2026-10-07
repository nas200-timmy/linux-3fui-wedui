<script setup lang="ts">
import { onMounted, onUnmounted } from 'vue'

/** 原版二级窗口（Form_v6_参数面板_XXX）的网页版：标题行 32px / #303030，内容区 #181818 / 内边距 20 */
const props = withDefaults(defineProps<{ title: string; hint?: string; width?: number }>(), {
  hint: '',
  width: 584,
})
const emit = defineEmits<{ close: [] }>()

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') emit('close')
}

onMounted(() => window.addEventListener('keydown', onKeydown))
onUnmounted(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <div class="param-dialog-mask" @click.self="emit('close')">
    <div class="param-dialog" :style="{ width: `${props.width}px` }">
      <div class="param-dialog-head">
        <span class="param-dialog-title">{{ props.title }}</span>
        <button class="param-dialog-close" @click="emit('close')">✕</button>
      </div>
      <div class="param-dialog-body">
        <div v-if="props.hint" class="param-dialog-hint">{{ props.hint }}</div>
        <slot />
      </div>
    </div>
  </div>
</template>
