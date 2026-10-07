<script setup lang="ts">
// 输出位置等路径字段的图形化目录选择框（原版 MCB_输出位置 的「浏览 ...」的网页版）。
// 外壳复用 ParamDialog（标题行 32 / #303030，内容区 #181818），只列目录不列文件。
import { computed, onMounted, ref } from 'vue'
import ParamDialog from './ParamDialog.vue'
import { api } from '../api'

interface DirEntry { name: string; path: string; isDirectory: boolean; isMedia: boolean }
interface BrowseResult { path?: string; parent?: string | null; root?: string | null; entries?: DirEntry[]; error?: string }

const props = withDefaults(defineProps<{ modelValue: string; title?: string; files?: boolean }>(), {
  title: '选择目录',
  files: false,
})
const emit = defineEmits<{ close: []; pick: [path: string] }>()

/** 与「准备文件」页一致：路径为空时从 /media 开始；root 由后端给出，避免走到媒体根目录之外 */
const 默认根目录 = '/media'
const currentPath = ref('')
const root = ref(默认根目录)
const parent = ref<string | null>(null)
const entries = ref<DirEntry[]>([])
const loading = ref(false)
const pathInput = ref('')
// 提示写在对话框内（全局 toast 只有 App.vue 里那一份列表，子组件 push 不显示）
const error = ref('')

const atRoot = computed(() => !currentPath.value || currentPath.value === root.value)
const canPick = computed(() => currentPath.value !== '' && !loading.value)

function 进入(path?: string) {
  loading.value = true
  error.value = ''
  api.probe.browse(path ?? currentPath.value)
    .then(result => {
      const data = result as BrowseResult
      if (data.error || !data.path) {
        提示错误(data.error ?? '目录读取失败')
        return
      }
      currentPath.value = data.path
      pathInput.value = data.path
      parent.value = data.parent ?? null
      root.value = data.root ?? 默认根目录
      entries.value = (data.entries ?? [])
        .filter(entry => entry.isDirectory || props.files)
        .sort((a, b) => {
          if (a.isDirectory !== b.isDirectory) return a.isDirectory ? -1 : 1
          return a.name.localeCompare(b.name, 'zh-CN')
        })
    })
    .catch(报错转文本)
    .finally(() => { loading.value = false })
}

function 提示错误(文本: string) {
  error.value = 文本
  pathInput.value = currentPath.value
}

function 报错转文本(thrown: unknown) {
  提示错误(String(thrown))
}

function 上级() {
  if (atRoot.value || !parent.value) return
  进入(parent.value)
}

function 跳转() {
  const 目标 = pathInput.value.trim()
  if (目标 === '' || 目标 === currentPath.value) return
  进入(目标)
}

function 选择此目录() {
  if (!canPick.value) return
  emit('pick', currentPath.value)
}

/** 文件模式：点击文件即选中（目录仍是进入） */
function 点击项(entry: DirEntry) {
  if (entry.isDirectory) 进入(entry.path)
  else if (props.files) emit('pick', entry.path)
}

onMounted(() => 进入(props.modelValue.trim() || 默认根目录))
</script>

<template>
  <ParamDialog :title="props.title" :width="560" @close="emit('close')">
    <div class="dir-picker">
      <div class="dir-picker-bar">
        <button class="small" :disabled="atRoot || !parent" @click="上级">↑ 上级目录</button>
        <input
          v-model="pathInput"
          type="text"
          class="mono"
          placeholder="/media/输出目录"
          @keydown.enter="跳转"
        />
        <button class="small" @click="跳转">跳转</button>
      </div>

      <div v-if="error" class="dir-picker-error txt-red">{{ error }}</div>

      <div class="dir-picker-list">
        <div v-if="loading" class="dir-picker-empty">正在读取…</div>
        <template v-else>
          <div
            v-for="entry in entries"
            :key="entry.path"
            class="dir-picker-item"
            :class="{ file: !entry.isDirectory }"
            @click="点击项(entry)"
          >{{ entry.name }}<template v-if="entry.isDirectory">/</template></div>
          <div v-if="entries.length === 0" class="dir-picker-empty">{{ files ? '该目录下没有文件' : '该目录下没有子文件夹' }}</div>
        </template>
      </div>

      <div class="dir-picker-foot">
        <span class="dir-picker-tip">{{ currentPath || '（未选择目录）' }}</span>
        <button v-if="!files" class="small" :disabled="!canPick" @click="选择此目录">选择此目录</button>
        <button class="small" @click="emit('close')">取消</button>
      </div>
    </div>
  </ParamDialog>
</template>

<style scoped>
.dir-picker { display: flex; flex-direction: column; gap: 10px; }
.dir-picker-bar { display: flex; align-items: center; gap: 8px; }
.dir-picker-bar > input {
  flex: 1; min-width: 0; height: 32px;
  border-radius: var(--radius-control);
}
.dir-picker-list {
  max-height: 300px; overflow: auto;
  border: 1px solid var(--border-control); border-radius: var(--radius-control);
}
.dir-picker-item {
  height: 32px; padding: 0 10px; display: flex; align-items: center;
  font-size: 13px; color: var(--text); cursor: pointer; white-space: nowrap;
  overflow: hidden; text-overflow: ellipsis;
}
.dir-picker-item::before { content: '▸'; margin-right: 8px; color: var(--text-faint); }
.dir-picker-item:hover { background: #3c3c3c; color: var(--text-strong); }
.dir-picker-empty { padding: 10px; font-size: 12px; color: #888888; }
.dir-picker-error { font-size: 12px; line-height: 1.5; word-break: break-word; }
.dir-picker-foot { display: flex; align-items: center; gap: 8px; }
.dir-picker-tip {
  flex: 1; min-width: 0; font-size: 12px; color: #888888;
  font-family: var(--mono); overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}
</style>
