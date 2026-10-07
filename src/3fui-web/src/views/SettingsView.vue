<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, type TlsStatus, errorText } from '../api'
import { useAuth, useToast } from '../store'
import ModernComboBox from '../components/ModernComboBox.vue'

const toast = useToast()
const auth = useAuth()
const settings = ref<Record<string, unknown>>({})

// 登录认证配置
const authUsername = ref('')
const authPassword = ref('')
const authBusy = ref(false)

async function saveAuth(enable: boolean) {
  authBusy.value = true
  try {
    await api.auth.configure(enable, authUsername.value.trim() || undefined, authPassword.value || undefined)
    authPassword.value = ''
    await auth.refresh()
    toast.push('ok', enable ? '登录认证已启用' : '登录认证已停用')
  } catch (error) {
    toast.push('err', errorText(error))
  } finally {
    authBusy.value = false
  }
}

async function logout() {
  await api.auth.logout().catch(() => {})
  await auth.refresh()
}
const tls = ref<TlsStatus | null>(null)
const certFile = ref<File | null>(null)
const keyFile = ref<File | null>(null)
const certText = ref('')
const keyText = ref('')
const uploadMode = ref<'file' | 'text'>('file')
const perf = ref<Record<string, unknown>>({})
const ffmpegInfo = ref<{ version: string; encoders: string; filters: string } | null>(null)

// 布尔设置（checkbox）
const BOOL_SETTINGS: { key: string; label: string }[] = [
  { key: '是否监听端口', label: '远程调用（UDP，与 Windows 版协议一致）' },
  { key: '转译模式', label: '转译模式（配合 替代进程文件名 调用包装脚本）' },
]

// 整数枚举设置：原版是下拉框，不是勾选框（勾选会把 true 写进 Integer 字段导致保存 400，
// 且 0=自动开始 与「勾选=开启」的直觉相反）
const ENUM_SETTINGS: { key: string; label: string; hint: string; options: { value: string; label: string }[] }[] = [
  {
    key: '自动开始任务选项', label: '自动开始任务', hint: '是否自动开始任务',
    options: [{ value: '0', label: '自动开始任务' }, { value: '1', label: '手动开始任务' }],
  },
  {
    key: '任务失败自动删除输出文件', label: '删除报废输出', hint: '任务失败或手动停止时是否删除报废输出文件',
    options: [{ value: '0', label: '保留输出文件' }, { value: '1', label: '删除输出文件' }],
  },
  {
    key: '提示音选项', label: '启用提示音', hint: '是否启用提示音',
    options: [{ value: '0', label: '启用' }, { value: '1', label: '禁用' }],
  },
]

// 下拉值是字符串，读写时与后端的 Integer 互转
function enumGet(key: string): string {
  return String(settings.value[key] ?? '0')
}
function enumSet(key: string, value: string) {
  settings.value[key] = Number(value) || 0
}

const TEXT_SETTINGS: { key: string; label: string; placeholder: string }[] = [
  { key: '监听的端口', label: '远程调用端口', placeholder: '10591' },
  { key: '替代进程文件名', label: '替代进程文件名（自定义 ffmpeg 路径）', placeholder: '留空使用系统 ffmpeg' },
  { key: '覆盖参数传递', label: '覆盖参数传递', placeholder: '如 "/path/run.sh <args>"' },
  { key: '工作目录', label: '工作目录', placeholder: 'ffmpeg 执行目录（留空默认）' },
  { key: '指定处理器核心', label: '指定处理器核心', placeholder: '如 0,1,2,3' },
  { key: '自动同时运行任务数量选项', label: '自动同时运行任务数量', placeholder: '0 = 自动（核心数）' },
]

function load() {
  api.settings.get().then(data => { settings.value = data })
  api.tls.status().then(data => { tls.value = data })
  api.perf().then(data => { perf.value = data })
  api.encoderDb()
  fetch('/api/ffmpeg/info').then(response => response.json()).then(data => { ffmpegInfo.value = data })
}

function saveSettings() {
  api.settings.put(settings.value).then(() => toast.push('ok', '设置已保存')).catch(error => toast.push('err', errorText(error)))
}

function onNumber(key: string, event: Event) {
  settings.value[key] = Number((event.target as HTMLInputElement).value) || 0
}

async function installTls() {
  try {
    if (uploadMode.value === 'file') {
      if (!certFile.value || !keyFile.value) return toast.push('err', '请选择证书与私钥文件')
      const result = await api.tls.installFiles(certFile.value, keyFile.value)
      toast.push('ok', `HTTPS 已启用（端口 ${result.port}）`)
    } else {
      if (!certText.value.trim() || !keyText.value.trim()) return toast.push('err', '请粘贴证书与私钥内容')
      const result = await api.tls.installPem(certText.value, keyText.value)
      toast.push('ok', `HTTPS 已启用（端口 ${result.port}）`)
    }
    load()
  } catch (error) {
    toast.push('err', errorText(error))
  }
}

function removeTls() {
  api.tls.remove().then(() => {
    toast.push('ok', 'HTTPS 已停用')
    load()
  }).catch(error => toast.push('err', errorText(error)))
}

onMounted(load)
</script>

<template>
  <div>
    <div class="page-header">
      <h2>设置</h2>
      <span class="desc">所有设置保存于数据目录 Settings.json，与 Windows 版字段名兼容</span>
      <span class="spacer" />
      <button class="small primary" @click="saveSettings">保存设置</button>
    </div>

    <div class="grid-2">
      <div>
        <div class="glass panel" style="margin-bottom: 12px">
          <div class="panel-title">功能设置</div>
          <div v-for="item in ENUM_SETTINGS" :key="item.key" class="field-row" style="display: flex; align-items: center; gap: 10px; margin-bottom: 8px">
            <ModernComboBox
              :model-value="enumGet(item.key)"
              :options="item.options"
              :width="220"
              @update:model-value="enumSet(item.key, $event)"
            />
            <span class="muted" style="font-size: 12px">{{ item.hint }}</span>
          </div>
          <label v-for="item in BOOL_SETTINGS" :key="item.key" class="checkbox-row">
            <input v-model="(settings[item.key] as boolean)" type="checkbox" />
            <span>{{ item.label }}</span>
          </label>
          <div v-for="item in TEXT_SETTINGS" :key="item.key" class="field-row">
            <label>{{ item.label }}</label>
            <input
              v-if="item.key === '自动同时运行任务数量选项' || item.key === '监听的端口'"
              type="number"
              :value="Number(settings[item.key] ?? 0)"
              :placeholder="item.placeholder"
              @input="onNumber(item.key, $event)"
            />
            <input v-else v-model="(settings[item.key] as string)" type="text" :placeholder="item.placeholder" />
          </div>
        </div>

        <div class="glass panel" style="margin-bottom: 12px">
          <div class="panel-title">ffmpeg 环境</div>
          <div v-if="ffmpegInfo" class="log-view" style="max-height: 130px">
            {{ (ffmpegInfo.version ?? '').split('\n').slice(0, 5).join('\n') }}
          </div>
          <div v-else class="muted">未检测到 ffmpeg 信息</div>
          <div class="muted" style="margin-top: 8px">
            提示：Docker 镜像内置 ffmpeg 静态构建；如需硬件加速（VAAPI/QSV/NVENC），请按 compose 示例挂载设备或使用带对应编解码器的 ffmpeg。
          </div>
        </div>

        <div class="glass panel">
          <div class="panel-title">系统监控（Linux /proc）</div>
          <div class="grid-2">
            <div class="stat-card" v-if="(perf['cpu'] as Record<string, unknown>)?.usagePercent !== undefined">
              <span class="value">{{ (perf['cpu'] as Record<string, unknown>)['usagePercent'] }}%</span>
              <span class="label">CPU（{{ (perf['cpu'] as Record<string, unknown>)['cores'] }} 核）</span>
            </div>
            <div class="stat-card" v-if="(perf['memory'] as Record<string, unknown>)?.usagePercent !== undefined">
              <span class="value">{{ (perf['memory'] as Record<string, unknown>)['usagePercent'] }}%</span>
              <span class="label">内存 {{ (perf['memory'] as Record<string, unknown>)['usedMb'] }} / {{ (perf['memory'] as Record<string, unknown>)['totalMb'] }} MB</span>
            </div>
            <div class="stat-card" v-if="(perf['loadavg'] as Record<string, unknown>)?.one">
              <span class="value">{{ (perf['loadavg'] as Record<string, unknown>)['one'] }}</span>
              <span class="label">负载（1/5/15 分钟）</span>
            </div>
            <div class="stat-card" v-if="(perf['gpu'] as unknown[] | undefined)?.length">
              <span class="value">{{ ((perf['gpu'] as Record<string, unknown>[])[0])['utilization'] }}%</span>
              <span class="label">GPU {{ ((perf['gpu'] as Record<string, unknown>[])[0])['name'] }}</span>
            </div>
          </div>
        </div>
      </div>

      <div>
        <div class="glass panel" style="margin-bottom: 12px">
          <div class="panel-title">声明式 HTTPS（上传证书立即生效）</div>
          <div v-if="tls?.active" class="flex" style="margin-bottom: 10px">
            <span class="badge done">HTTPS 已启用 · 端口 {{ tls.port }}</span>
            <span class="muted mono" style="font-size: 12px">{{ tls.subject }}</span>
          </div>
          <div v-else class="flex" style="margin-bottom: 10px">
            <span class="badge pending">HTTPS 未启用</span>
            <span class="muted">上传 PEM 证书与私钥后，{{ tls?.port ?? 8443 }} 端口立即可用</span>
          </div>
          <div v-if="tls?.notAfter" class="muted" style="margin-bottom: 8px">有效期至：{{ tls.notAfter }}</div>
          <div class="flex" style="margin-bottom: 8px">
            <button class="small" :class="{ primary: uploadMode === 'file' }" @click="uploadMode = 'file'">文件上传</button>
            <button class="small" :class="{ primary: uploadMode === 'text' }" @click="uploadMode = 'text'">粘贴内容</button>
          </div>
          <template v-if="uploadMode === 'file'">
            <div class="field"><label>证书（cert.pem / fullchain.pem）</label><input type="file" accept=".pem,.crt,.cer" @change="certFile = ($event.target as HTMLInputElement).files?.[0] ?? null" /></div>
            <div class="field" style="margin-top: 8px"><label>私钥（key.pem）</label><input type="file" accept=".pem,.key" @change="keyFile = ($event.target as HTMLInputElement).files?.[0] ?? null" /></div>
          </template>
          <template v-else>
            <div class="field"><label>证书内容（PEM）</label><textarea v-model="certText" rows="5" placeholder="-----BEGIN CERTIFICATE-----" /></div>
            <div class="field" style="margin-top: 8px"><label>私钥内容（PEM）</label><textarea v-model="keyText" rows="5" placeholder="-----BEGIN PRIVATE KEY-----" /></div>
          </template>
          <div class="flex" style="margin-top: 12px">
            <button class="primary" @click="installTls">启用 / 更新 HTTPS</button>
            <button v-if="tls?.active && !tls.envConfigured" class="danger" @click="removeTls">停用 HTTPS</button>
          </div>
          <div class="muted" style="margin-top: 10px">
            说明：启用后 HTTP 端口自动 301 跳转 HTTPS；证书保存于数据目录 certs/，容器重启后依然生效。
            也可以声明式挂载：TLS_CERT_PATH / TLS_KEY_PATH 环境变量指向证书文件。
          </div>
        </div>

        <div class="glass panel" style="margin-bottom: 12px">
          <div class="panel-title">登录认证（公网访问强烈建议启用）</div>
          <div class="flex" style="margin-bottom: 8px">
            <span class="badge" :class="auth.enabled ? 'done' : 'pending'">{{ auth.enabled ? '已启用' : '未启用' }}</span>
            <span class="muted" style="font-size: 12px">启用后打开网页需要先登录；错误 15 次锁定 3 分钟</span>
          </div>
          <div class="field-row">
            <label>用户名</label>
            <input v-model="authUsername" type="text" placeholder="登录用户名" autocomplete="off" />
          </div>
          <div class="field-row" style="margin-top: 8px">
            <label>密码</label>
            <input v-model="authPassword" type="password" :placeholder="auth.enabled ? '留空 = 不修改密码' : '至少 6 位'" autocomplete="new-password" />
          </div>
          <div class="flex" style="margin-top: 10px">
            <button class="primary small" :disabled="authBusy" @click="saveAuth(true)">{{ auth.enabled ? '保存修改' : '启用登录认证' }}</button>
            <button v-if="auth.enabled" class="danger small" :disabled="authBusy" @click="saveAuth(false)">停用</button>
            <button v-if="auth.enabled" class="small" @click="logout">退出登录</button>
          </div>
        </div>

        <div class="glass panel">
          <div class="panel-title">远程调用（局域网自动化）</div>
          <div class="muted" style="margin-bottom: 8px">
            与 Windows 版 3FUI 相同的 UDP 协议：<code class="mono">-i 文件 -3fui_file 预设名</code> 或 <code class="mono">-ffmpeg 参数…</code>。
            默认端口 10591，也可用 <code class="mono">-3fuiVideoHelperInPointTime / -3fuiVideoHelperOutPointTime</code> 传剪辑区间。
          </div>
          <div class="field-row">
            <label>本地地址</label>
            <span class="mono muted">{{ settings['监听的端口'] ?? '10591' }}</span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
