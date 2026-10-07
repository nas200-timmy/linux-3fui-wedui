<script setup lang="ts">
// 编码器切换告知弹窗：任务编码器被服务端自动切换后，请用户选择「永久切换预设」或「仅本次任务」。
// 外壳复用 ParamDialog；App.vue 在有待确认消息时挂载本组件，消息数据来自 useEncoderSwitch 队列。
import { computed, ref } from 'vue'
import ParamDialog from './ParamDialog.vue'
import { useEncoderSwitch, useToast } from '../store'

const store = useEncoderSwitch()
const toast = useToast()
const busy = ref(false)
const msg = computed(() => store.pending.value[0])

async function choose(选择: 'preset' | 'once') {
  const current = msg.value
  if (!current || busy.value) return
  busy.value = true
  try {
    await store.resolve(current, 选择)
    toast.push('ok', 选择 === 'preset' ? '预设已永久切换' : '已按仅本次任务处理')
  } catch (error) {
    // API 失败（如 400）时消息留在队列里，弹窗不关，错误内容交给用户
    toast.push('err', String(error))
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <ParamDialog v-if="msg" title="编码器已自动切换" :width="560" @close="choose('once')">
    <div class="enc-switch">
      <p class="enc-switch-summary">
        任务「{{ msg.任务名称 }}」的编码器已从 <b>{{ msg.原编码器 }}</b> 自动切换为 <b>{{ msg.新编码器 }}</b>（{{ msg.触发方式 }}）
      </p>
      <div class="enc-switch-reason">原因：{{ msg.原因 }}</div>

      <div class="enc-switch-section">
        <div class="enc-switch-title">参数换算</div>
        <ul v-if="msg.换算明细.length" class="enc-switch-list">
          <li v-for="(item, index) in msg.换算明细" :key="index">{{ item }}</li>
        </ul>
        <div v-else class="enc-switch-empty">无</div>
      </div>

      <div class="enc-switch-section">
        <div class="enc-switch-title">丢弃参数</div>
        <ul v-if="msg.丢弃参数.length" class="enc-switch-list">
          <li v-for="(item, index) in msg.丢弃参数" :key="index">{{ item }}</li>
        </ul>
        <div v-else class="enc-switch-empty">无</div>
      </div>

      <p class="enc-switch-note">两个编码器属于同一硬件（Intel 核显），质量参数按相同标度换算，实际输出见任务命令行。</p>

      <div class="modal-footer">
        <button class="small primary" :disabled="busy" @click="choose('preset')">永久切换预设</button>
        <button class="small" :disabled="busy" @click="choose('once')">仅本次任务</button>
      </div>
    </div>
  </ParamDialog>
</template>

<style scoped>
.enc-switch { display: flex; flex-direction: column; gap: 10px; font-size: 13px; color: var(--text); }
.enc-switch-summary { margin: 0; line-height: 1.6; }
.enc-switch-summary b { color: var(--text-strong); font-family: var(--mono); }
.enc-switch-reason {
  padding: 8px 10px; border-radius: var(--radius-control);
  background: #2a2a2a; color: var(--text-dim);
  font-size: 12px; line-height: 1.6; word-break: break-word;
}
.enc-switch-section { display: flex; flex-direction: column; gap: 4px; }
.enc-switch-title { font-size: 12px; color: var(--blue); }
.enc-switch-list {
  margin: 0; padding: 8px 10px 8px 26px;
  border: 1px solid var(--border-control); border-radius: var(--radius-control);
  font-family: var(--mono); font-size: 12px; line-height: 1.7; color: var(--text-dim);
}
.enc-switch-empty { padding: 6px 10px; font-size: 12px; color: var(--text-faint); }
.enc-switch-note { margin: 0; font-size: 12px; color: #888888; line-height: 1.6; }
</style>
