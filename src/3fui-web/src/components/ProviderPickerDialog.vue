<script setup lang="ts">
// 厂商选择：models.dev 目录（约 226 家）按「国内知名 → 国外知名 → 其他」分组展示，配搜索框；
// 上游 3FUI 的厂商列表来自赞助者专用的远端 sp-agent-endpoints.json，公开版拿不到，这里用 models.dev 公开替代。
import { computed, ref } from 'vue'
import ParamDialog from './ParamDialog.vue'
import { groupProviders, providerBaseUrl, providerDocUrl, type ProviderGroupId } from '../providers'
import type { CatalogProvider } from '../api'

const props = defineProps<{
  providers: CatalogProvider[]
  selected: string
  stale: boolean
  fetchedAt: string
}>()

const emit = defineEmits<{ close: []; pick: [provider: CatalogProvider] }>()

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
</script>

<template>
  <ParamDialog title="选择模型厂商" :width="780" @close="emit('close')">
    <div class="provider-picker">
      <div class="provider-picker-bar">
        <input v-model="query" type="text" placeholder="搜索厂商名 / id / 接口地址，例如 deepseek、智谱、qwen、openrouter" />
        <span class="muted">{{ props.providers.length }} 家厂商</span>
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
              :class="{ active: provider.id === props.selected }"
              @click="emit('pick', provider)"
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
        厂商目录来自 models.dev（开源模型数据库）{{ fetchedText }}<span v-if="props.stale">（正在用缓存目录，后台刷新中）</span>。
        「需手填地址」的厂商（Azure / Bedrock / Vertex 等）没有公开的兼容地址，只给文档链接。
      </div>
    </div>
  </ParamDialog>
</template>
