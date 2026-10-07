<script setup lang="ts">
// Agent 的「模型管理」弹窗：一个窗口里完成 选厂商（models.dev 目录）→ 自动填端点 → 填密钥 → 扫描/选模型 → 保存。
// 选厂商是弹窗内部的第二步（搜索 + 分组列表），不再叠第二层浮窗，Esc/点遮罩只对最外层生效。
import { computed, ref } from 'vue'
import ParamDialog from './ParamDialog.vue'
import ModernComboBox, { type ComboOption } from './ModernComboBox.vue'
import { groupProviders, providerBaseUrl, providerDocUrl, type ProviderGroupId } from '../providers'
import type { CatalogProvider } from '../api'

const props = defineProps<{
  providers: CatalogProvider[]
  selectedProviderId: string
  /** 已选厂商的展示名 / base URL / 文档链接；未选时为空串 */
  providerLabel: string
  providerBase: string
  providerDoc: string
  stale: boolean
  fetchedAt: string
  catalogLoading: boolean
  modelOptions: ComboOption[]
  /** 当前模型在目录里的描述（限价/上下文等），没有则空串 */
  activeModelText: string
  scanLoading: boolean
  scanMessage: string
  modelSourceText: string
  hasApiKey: boolean
  apiKeySaving: boolean
  confirmClearKey: boolean
}>()

const emit = defineEmits<{
  close: []
  pick: [provider: CatalogProvider]
  refreshCatalog: []
  scan: []
  saveKey: []
  clearKey: []
  save: []
}>()

const endpoint = defineModel<string>('endpoint', { required: true })
const model = defineModel<string>('model', { required: true })
const apiKeyInput = defineModel<string>('apiKey', { required: true })
const reasoningEffort = defineModel<string>('reasoningEffort', { required: true })

/** 'main' 表单页 / 'pick' 选厂商页 */
const step = ref<'main' | 'pick'>('main')

const query = ref('')
const expanded = ref<Set<ProviderGroupId>>(new Set<ProviderGroupId>(['cn', 'global']))
const searching = computed(() => query.value.trim() !== '')

function matches(provider: CatalogProvider, keyword: string): boolean {
  return provider.name.toLowerCase().includes(keyword)
    || provider.id.toLowerCase().includes(keyword)
    || (provider.api ?? '').toLowerCase().includes(keyword)
}

const visibleGroups = computed(() => {
  const keyword = query.value.trim().toLowerCase()
  return groupProviders(props.providers)
    .map(group => ({ ...group, items: keyword === '' ? group.items : group.items.filter(provider => matches(provider, keyword)) }))
    .filter(group => group.items.length > 0)
})

function isExpanded(id: ProviderGroupId): boolean {
  return searching.value || expanded.value.has(id)
}

function toggleGroup(id: ProviderGroupId) {
  if (searching.value) return
  const next = new Set(expanded.value)
  if (next.has(id)) next.delete(id)
  else next.add(id)
  expanded.value = next
}

const fetchedText = computed(() => {
  const time = new Date(props.fetchedAt)
  return props.fetchedAt === '' || Number.isNaN(time.getTime()) ? '' : `，更新于 ${time.toLocaleString()}`
})

function pick(provider: CatalogProvider) {
  step.value = 'main'
  emit('pick', provider)
}
</script>

<template>
  <ParamDialog title="模型管理" :width="680" @close="emit('close')">
    <!-- 第一步：表单 -->
    <div v-if="step === 'main'" class="model-manager">
      <div class="model-manager-summary">
        当前：{{ providerLabel === '' ? '未选厂商' : providerLabel }} · {{ model.trim() === '' ? '未填模型' : model.trim() }}
        · {{ hasApiKey ? '密钥已配置' : '密钥未配置' }}
      </div>

      <div class="field-item">
        <div class="field-label">厂商</div>
        <div class="field-control flex" style="gap: 6px; flex-wrap: wrap">
          <button class="small" @click="step = 'pick'">{{ selectedProviderId === '' ? '选择厂商' : '更换厂商' }}</button>
          <span class="muted" style="font-size: 12px">
            {{ providerLabel === '' ? (providers.length > 0 ? `目录收录 ${providers.length} 家，国内知名优先` : '目录加载中…') : providerLabel }}
          </span>
          <button class="small plain" :disabled="catalogLoading" @click="emit('refreshCatalog')">{{ catalogLoading ? '刷新中…' : '刷新目录' }}</button>
        </div>
      </div>

      <div class="field-item" style="margin-top: 6px">
        <div class="field-label">端点地址</div>
        <div class="field-control">
          <input v-model="endpoint" type="text" placeholder="如 https://api.deepseek.com/v1" />
          <div class="muted" style="font-size: 12px; margin-top: 4px; line-height: 1.6">
            <template v-if="providerBase">已按「{{ providerLabel }}」自动填写。本地 Ollama / LM Studio 改成 http://127.0.0.1:11434/v1 这类地址即可。</template>
            <template v-else>选厂商会自动填端点；也可以直接手填任意 OpenAI 兼容地址。</template>
            <a v-if="providerDoc" class="agent-doc-link" :href="providerDoc" target="_blank" rel="noopener noreferrer">文档 / 申请密钥</a>
          </div>
        </div>
      </div>

      <div class="field-item" style="margin-top: 6px">
        <div class="field-label">API Key</div>
        <div class="field-control flex" style="gap: 6px; flex-wrap: wrap">
          <input
            v-model="apiKeyInput"
            type="password"
            autocomplete="off"
            style="flex: 1; min-width: 150px"
            :placeholder="hasApiKey ? '已配置，留空不修改' : '粘贴该厂商的 API Key'"
          />
          <button class="small primary" :disabled="apiKeySaving" @click="emit('saveKey')">保存密钥</button>
          <button v-if="hasApiKey" class="small danger" :disabled="apiKeySaving" @click="emit('clearKey')">
            {{ confirmClearKey ? '确认清除？' : '清除密钥' }}
          </button>
        </div>
        <div class="muted" style="font-size: 12px; margin-top: 4px">
          {{ hasApiKey ? '服务端已保存密钥，网页不回显明文。' : '尚未配置密钥，填入后保存在服务端 Settings.json。' }}
        </div>
      </div>

      <div class="field-item" style="margin-top: 6px">
        <div class="field-label">模型</div>
        <div class="field-control flex" style="gap: 6px; flex-wrap: wrap">
          <ModernComboBox v-model="model" :options="modelOptions" watermark="模型 id" :width="230" editable />
          <button class="small" :disabled="scanLoading" @click="emit('scan')">{{ scanLoading ? '扫描中…' : '扫描端点模型' }}</button>
        </div>
        <div class="muted" style="font-size: 12px; margin-top: 4px; line-height: 1.6">
          {{ modelSourceText === '' ? '点「扫描端点模型」从端点拉取该 Key 下真实可用的模型。' : `候选来源：${modelSourceText}` }}
          <template v-if="activeModelText">· {{ activeModelText }}</template>
        </div>
      </div>

      <div class="field-item" style="margin-top: 6px">
        <div class="field-label">推理级别</div>
        <div class="field-control"><input v-model="reasoningEffort" type="text" placeholder="low / medium / high" /></div>
      </div>

      <div v-if="scanMessage" class="muted" style="margin-top: 6px; font-size: 12px">{{ scanMessage }}</div>
      <button class="small primary" style="margin-top: 10px" @click="emit('save')">保存</button>
    </div>

    <!-- 第二步：选厂商（models.dev 目录，按「国内知名 → 国外知名 → 其他」分组） -->
    <div v-else class="provider-picker">
      <div class="provider-picker-bar">
        <button class="small" @click="step = 'main'">← 返回</button>
        <input v-model="query" type="text" placeholder="搜索厂商名 / id / 接口地址，例如 deepseek、智谱、qwen、openrouter" />
        <span class="muted">{{ providers.length }} 家厂商</span>
      </div>

      <div class="provider-picker-list">
        <template v-for="group in visibleGroups" :key="group.id">
          <div class="provider-picker-group" @click="toggleGroup(group.id)">
            <span class="provider-picker-group-title">{{ group.label }}（{{ group.items.length }}）</span>
            <span class="provider-picker-group-toggle">{{ isExpanded(group.id) ? '收起' : '展开' }}</span>
          </div>
          <template v-if="isExpanded(group.id)">
            <div
              v-for="provider in group.items"
              :key="provider.id"
              class="provider-picker-item"
              :class="{ active: provider.id === selectedProviderId }"
              @click="pick(provider)"
            >
              <span class="provider-picker-name">{{ provider.name }}</span>
              <span class="provider-picker-id mono">{{ provider.id }}</span>
              <span class="muted provider-picker-count">{{ provider.models }} 个模型</span>
              <span class="provider-picker-url mono" :class="{ 'txt-red': !providerBaseUrl(provider) }">
                {{ providerBaseUrl(provider) || '需手填地址' }}
              </span>
              <a
                v-if="providerDocUrl(provider)"
                class="provider-picker-doc"
                :href="providerDocUrl(provider)"
                target="_blank"
                rel="noopener noreferrer"
                @click.stop
              >文档</a>
            </div>
          </template>
        </template>
        <div v-if="visibleGroups.length === 0" class="provider-picker-empty">没有匹配的厂商</div>
      </div>

      <div class="muted" style="font-size: 12px; line-height: 1.6">
        厂商目录来自 models.dev（开源模型数据库）{{ fetchedText }}<span v-if="stale">（正在用缓存目录，后台刷新中）</span>。
        「需手填地址」的厂商（Azure / Bedrock / Vertex 等）没有公开的兼容地址，只给文档链接。
      </div>
    </div>
  </ParamDialog>
</template>
