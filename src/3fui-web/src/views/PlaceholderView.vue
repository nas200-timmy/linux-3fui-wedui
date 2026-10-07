<script setup lang="ts">
defineProps<{ title: string; desc?: string }>()
</script>

<template>
  <div>
    <div class="page-header">
      <h2>{{ title }}</h2>
    </div>
    <div class="glass panel">
      <div class="panel-title">{{ title }}</div>
      <div class="muted" style="line-height: 2">{{ desc ?? '此页面暂未移植到网页版。' }}</div>
    </div>
  </div>
</template>
