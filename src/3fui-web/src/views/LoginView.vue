<script setup lang="ts">
import { ref } from 'vue'
import { api } from '../api'
import { useAuth } from '../store'

const auth = useAuth()
const username = ref('')
const password = ref('')
const error = ref('')
const busy = ref(false)

async function submit() {
  if (!username.value.trim() || !password.value) {
    error.value = '请输入用户名和密码'
    return
  }
  busy.value = true
  error.value = ''
  try {
    await api.auth.login(username.value.trim(), password.value)
    await auth.refresh()
  } catch (err) {
    error.value = String(err instanceof Error ? err.message : err)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="login-mask">
    <div class="login-box">
      <div class="login-title">FFmpegFreeUI</div>
      <div class="login-sub">Linux 网页版 · 需要登录</div>
      <div class="field" style="margin-top: 18px">
        <label>用户名</label>
        <input v-model="username" type="text" autocomplete="username" @keydown.enter="submit" />
      </div>
      <div class="field" style="margin-top: 10px">
        <label>密码</label>
        <input v-model="password" type="password" autocomplete="current-password" @keydown.enter="submit" />
      </div>
      <div v-if="error" class="txt-red" style="margin-top: 10px; font-size: 13px">{{ error }}</div>
      <button class="primary" style="width: 100%; margin-top: 16px; height: 36px" :disabled="busy" @click="submit">
        {{ busy ? '登录中…' : '登录' }}
      </button>
      <div class="muted" style="margin-top: 12px; font-size: 12px">连续错误 15 次将锁定 3 分钟</div>
    </div>
  </div>
</template>
