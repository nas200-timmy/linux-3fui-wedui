<script setup lang="ts">
// 工具调用卡片：一轮里连续的工具调用合并成一张可折叠卡片
// （对齐上游 Form_v6_Agent_呈现.vb:145-208 的呈现方式：名称/耗时/字数/状态 + 展开看详情）。
import { computed, ref } from 'vue'
import { toolLabel, type ToolCallView } from '../agentTools'

const props = defineProps<{ calls: ToolCallView[] }>()
const open = ref(false)

const totalMs = computed(() => props.calls.reduce((sum, call) => sum + (call.ms ?? 0), 0))
const failedCount = computed(() => props.calls.filter(call => call.ok === false || call.denied === true).length)
const unfinishedCount = computed(() => props.calls.filter(call => call.result === undefined).length)
const summaryNames = computed(() => [...new Set(props.calls.map(call => toolLabel(call.name)))].join('、'))

function pretty(text: string): string {
  try {
    return JSON.stringify(JSON.parse(text), null, 2)
  } catch {
    return text
  }
}

function statusText(call: ToolCallView): string {
  if (call.unauthorized) return '未授权（当前级别不含它）'
  if (call.denied) return '用户拒绝'
  if (call.result === undefined) return '未完成（本轮被中断）'
  if (call.ok === false) return '失败'
  return '完成'
}
</script>

<template>
  <div class="tool-card" :class="{ 'tool-card-failed': failedCount > 0 || unfinishedCount > 0 }">
    <div class="tool-card-head" @click="open = !open">
      <span class="tool-card-title">工具调用 ×{{ props.calls.length }}</span>
      <span class="tool-card-names">{{ summaryNames }}</span>
      <span class="muted">{{ totalMs }} ms</span>
      <span v-if="failedCount > 0" class="txt-red">{{ failedCount }} 个未成功</span>
      <span v-else-if="unfinishedCount > 0" class="txt-red">{{ unfinishedCount }} 个未完成</span>
      <span class="muted" style="margin-left: auto">{{ open ? '收起' : '展开' }}</span>
    </div>
    <div v-if="open" class="tool-card-body">
      <div v-for="call in props.calls" :key="call.id" class="tool-card-item">
        <div class="tool-card-item-head">
          <span class="mono">{{ call.name }}</span>
          <span class="muted">{{ toolLabel(call.name) }}</span>
          <span class="muted" style="margin-left: auto">{{ statusText(call) }}<template v-if="call.ms !== undefined"> · {{ call.ms }} ms</template></span>
        </div>
        <div class="tool-card-block">
          <div class="tool-card-block-title">参数</div>
          <pre class="mono">{{ pretty(call.arguments) }}</pre>
        </div>
        <div v-if="call.result" class="tool-card-block">
          <div class="tool-card-block-title">结果（{{ call.result.length }} 字符）</div>
          <pre class="mono">{{ pretty(call.result) }}</pre>
        </div>
      </div>
    </div>
  </div>
</template>
