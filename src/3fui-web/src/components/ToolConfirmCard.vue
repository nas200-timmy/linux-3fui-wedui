<script setup lang="ts">
// 写操作确认卡：模型请求执行写操作时，先让用户点「允许」/「拒绝」。
// 上游只靠提示词要求模型先征得同意（Agent技能资料库_v6.vb:178/231），这里是硬确认。
import { computed } from 'vue'
import { describeToolArgs, toolLabel, type AgentToolDef } from '../agentTools'

const props = defineProps<{ tool: AgentToolDef; args: Record<string, unknown> }>()
const emit = defineEmits<{ allow: []; deny: [] }>()

const argsText = computed(() => describeToolArgs(props.args))
const isEmptyArgs = computed(() => Object.keys(props.args).length === 0)
</script>

<template>
  <div class="tool-confirm">
    <div class="tool-confirm-head">
      <span class="tool-confirm-badge">待确认</span>
      <span class="tool-confirm-title">Agent 请求执行写操作：{{ toolLabel(props.tool.name) }}</span>
      <span class="mono muted">{{ props.tool.name }}</span>
    </div>
    <div class="tool-confirm-body">
      <div v-if="!isEmptyArgs" class="tool-confirm-block">
        <div class="tool-card-block-title">将要执行的内容</div>
        <pre class="mono">{{ argsText }}</pre>
      </div>
      <div v-else class="muted">该操作不带参数。</div>
      <div class="muted" style="font-size: 12px">
        允许后立即执行并把结果回灌给模型；拒绝则告诉模型「用户拒绝了该操作」，它会换个做法。
      </div>
    </div>
    <div class="flex" style="justify-content: flex-end; gap: 6px">
      <button class="small" @click="emit('deny')">拒绝</button>
      <button class="small primary" @click="emit('allow')">允许</button>
    </div>
  </div>
</template>
