// models.dev 厂商目录的分组与兜底地址。
// models.dev 的 api.json 只有 id/name/api/doc/env/models，没有地区字段，
// 所以按 id 手工分成「国内知名 / 国外知名 / 其他」三组（组内顺序＝数组顺序），未列入的一律进「其他」。
import type { CatalogProvider } from './api'

// 国内知名（含各家海外站与套餐入口，便于按需选用）
const 国内知名 = [
  'deepseek',
  'moonshotai-cn', 'moonshotai', 'kimi-code-plan-cn', 'kimi-code-plan-global',
  'zhipuai', 'zhipuai-coding-plan', 'zai', 'zai-coding-plan',
  'alibaba-cn', 'alibaba', 'alibaba-coding-plan-cn', 'alibaba-coding-plan',
  'alibaba-token-plan-cn', 'alibaba-token-plan',
  'volcengine', 'volcengine-coding-plan',
  'siliconflow-cn', 'siliconflow',
  'minimax-cn', 'minimax', 'minimax-cn-coding-plan', 'minimax-coding-plan',
  'stepfun', 'stepfun-step-plan', 'stepfun-ai', 'stepfun-ai-step-plan',
  'tencent-tokenhub', 'tencent-coding-plan', 'tencent-token-plan',
  'modelscope', 'sensenova', 'xiaomi', 'xiaomi-token-plan-cn', 'longcat',
  'qiniu-ai', 'jiekou', '302ai', 'iflowcn', 'drun', 'scnet-token-plan', 'kuae-cloud-coding-plan',
]

// 国外知名
const 国外知名 = [
  'openai', 'anthropic', 'google', 'google-vertex', 'xai', 'mistral', 'cohere',
  'groq', 'togetherai', 'openrouter', 'deepinfra', 'cerebras', 'perplexity',
  'fireworks-ai', 'novita-ai', 'nvidia', 'huggingface', 'ollama-cloud', 'lmstudio',
  'github-copilot', 'azure', 'amazon-bedrock', 'cloudflare-workers-ai', 'meta', 'ai21', 'upstage',
]

// models.dev 对「用 SDK 默认地址」的厂商省略了 api 字段（openai/groq/xai 等），这里补上常用几家；
// 没有可靠默认值的（azure / bedrock / vertex）刻意留空，让用户手填而不是猜一个错地址。
const 兜底地址: Record<string, string> = {
  openai: 'https://api.openai.com/v1',
  anthropic: 'https://api.anthropic.com/v1',
  google: 'https://generativelanguage.googleapis.com/v1beta/openai',
  xai: 'https://api.x.ai/v1',
  mistral: 'https://api.mistral.ai/v1',
  cohere: 'https://api.cohere.ai/compatibility/v1',
  groq: 'https://api.groq.com/openai/v1',
  togetherai: 'https://api.together.xyz/v1',
  cerebras: 'https://api.cerebras.ai/v1',
  perplexity: 'https://api.perplexity.ai',
  deepinfra: 'https://api.deepinfra.com/v1/openai',
  lmstudio: 'http://127.0.0.1:1234/v1',
}

export type ProviderGroupId = 'cn' | 'global' | 'other'

export interface ProviderGroup {
  id: ProviderGroupId
  label: string
  items: CatalogProvider[]
}

/** 取该厂商可用的 base URL：models.dev 的 api 优先，其次内置兜底，都没有则空串（要手填）。 */
export function providerBaseUrl(provider: CatalogProvider): string {
  return (provider.api ?? '').trim() || 兜底地址[provider.id] || ''
}

/** 文档链接只放行 http(s)，避免第三方 JSON 里的伪协议。 */
export function providerDocUrl(provider: CatalogProvider): string {
  const doc = (provider.doc ?? '').trim()
  return /^https?:\/\//i.test(doc) ? doc : ''
}

export function groupProviders(providers: CatalogProvider[]): ProviderGroup[] {
  const byId = new Map(providers.map(provider => [provider.id, provider]))
  const pick = (ids: string[]) => ids
    .map(id => byId.get(id))
    .filter((provider): provider is CatalogProvider => provider !== undefined)
  const known = new Set([...国内知名, ...国外知名])
  const other = providers
    .filter(provider => !known.has(provider.id))
    .sort((a, b) => b.models - a.models || a.name.localeCompare(b.name, 'zh-Hans-CN'))

  return [
    { id: 'cn', label: '国内知名', items: pick(国内知名) },
    { id: 'global', label: '国外知名', items: pick(国外知名) },
    { id: 'other', label: '其他厂商', items: other },
  ]
}
