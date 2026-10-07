// Agent 工具层（浏览器侧）：目录类型、tool_calls 分片累加、浏览器范围工具的执行器。
//
// 分工：服务端 `/api/agent/tools` 下发**按权限级别过滤过**的目录，并执行 queue/预设/硬件这类
// 服务端工具；参数面板与准备文件的状态只活在浏览器里，所以那几个工具在这里执行。
// 循环本身在 AgentView.vue，本文件只提供"纯函数 + 单个工具的执行"。
import { api, type AgentToolDef, type PresetData, type ToolCatalog } from './api'
import { GROUPS, newPreset, getByPath, setByPath, 默认音频编码器, type FieldDef, type FieldOption } from './schema'
import { 检查容器兼容性 } from './store'

export type { AgentToolDef, ToolCatalog }

/** 工具名 → 中文动作名（确认卡与工具卡片上用） */
export const TOOL_LABELS: Record<string, string> = {
  get_parameter_panel_state: '读取参数面板',
  get_parameter_field_info: '查询参数字段',
  apply_parameter_panel_patch: '修改参数面板',
  get_queue_summary: '读取编码队列',
  get_queue_task_logs: '读取任务日志',
  control_queue_tasks: '控制队列任务',
  patch_queue_task_presets: '修改队列任务预设',
  list_parameter_presets: '列出预设',
  read_parameter_preset: '读取预设',
  save_parameter_preset: '保存预设文件',
  get_system_hardware: '读取硬件与环境信息',
  probe_media_file: '探测媒体文件',
  browse_media_directory: '浏览媒体目录',
  get_prepare_files: '读取准备文件列表',
  set_prepare_files: '修改准备文件列表',
  submit_prepare_files_to_queue: '把准备文件加入队列',
  apply_parameter_preset: '应用预设到面板',
}

export function toolLabel(name: string): string {
  return TOOL_LABELS[name] ?? name
}

export interface ToolCall {
  id: string
  name: string
  arguments: string
}

/** 展示用的工具调用（在 ToolCall 上挂执行结果；发回上游前会剥掉这些字段） */
export interface ToolCallView extends ToolCall {
  result?: string
  ms?: number
  ok?: boolean
  /** 用户点了拒绝 */
  denied?: boolean
  /** 不在当前级别目录里（未授权，未执行） */
  unauthorized?: boolean
}

/** 转成 OpenAI tools 数组（原样透传给上游；服务端只负责转发）。 */
export function toOpenAiTools(catalog: AgentToolDef[]) {
  return catalog.map(tool => ({
    type: 'function',
    function: { name: tool.name, description: tool.description, parameters: tool.parameters },
  }))
}

/**
 * tool_calls 分片累加器：很多厂商把 function.arguments 拆成多个 chunk 发，
 * 必须按 index 拼（对齐上游 Agent端点客户端_v6.vb:772-812 AccumulateChatMessageDelta）。
 */
export function createToolCallAccumulator() {
  const slots = new Map<number, { id: string; name: string; args: string }>()
  return {
    push(list: unknown) {
      if (!Array.isArray(list)) return
      for (const raw of list) {
        if (typeof raw !== 'object' || raw === null) continue
        const item = raw as { index?: unknown; id?: unknown; function?: { name?: unknown; arguments?: unknown } }
        const index = typeof item.index === 'number' ? item.index : 0
        const slot = slots.get(index) ?? { id: '', name: '', args: '' }
        if (typeof item.id === 'string' && item.id) slot.id = item.id
        if (typeof item.function?.name === 'string' && item.function.name) slot.name = item.function.name
        if (typeof item.function?.arguments === 'string') slot.args += item.function.arguments
        slots.set(index, slot)
      }
    },
    /** 收尾：按 index 排序，缺 id 补一个（对齐上游 AddAccumulatedToolCalls）。 */
    result(): ToolCall[] {
      return [...slots.entries()]
        .sort((a, b) => a[0] - b[0])
        .filter(([, slot]) => slot.name !== '')
        .map(([index, slot]) => ({
          id: slot.id || `call_${index}_${Date.now().toString(36)}`,
          name: slot.name,
          arguments: slot.args.trim() === '' ? '{}' : slot.args,
        }))
    },
    get size() {
      return slots.size
    },
  }
}

// ── 参数面板字段索引（从 schema 的 GROUPS 建立，只建一次）──

interface FieldIndexEntry {
  path: string
  label: string
  kind: string
  options: FieldOption[]
  page: string
  section: string
  hint: string
  placeholder: string
}

let fieldIndexCache: FieldIndexEntry[] | null = null

export function fieldIndex(): FieldIndexEntry[] {
  if (fieldIndexCache) return fieldIndexCache
  const entries: FieldIndexEntry[] = []
  for (const page of GROUPS) {
    for (const section of page.sections) {
      for (const field of section.fields as FieldDef[]) {
        const type = field.type
        entries.push({
          path: field.path,
          label: field.label,
          kind: type.kind,
          options: type.kind === 'select' ? type.options : [],
          page: page.title,
          section: section.title ?? '',
          hint: field.info ?? (typeof field.hint === 'string' ? field.hint : ''),
          placeholder: 'placeholder' in type && typeof type.placeholder === 'string' ? type.placeholder : '',
        })
      }
    }
    for (const section of page.sections) {
      for (const slider of section.sliders ?? []) {
        entries.push({
          path: slider.valuePath,
          label: slider.label,
          kind: 'slider-value',
          options: [],
          page: page.title,
          section: section.title ?? '',
          hint: `量程 ${slider.min}~${slider.max}${slider.unit ? ` ${slider.unit}` : ''}，默认串 ${slider.def}；门控开关是 ${slider.enablePath}（必须为 true 核心才会生成对应滤镜）`,
          placeholder: '',
        })
        entries.push({
          path: slider.enablePath,
          label: `${slider.label}（滑杆门控开关）`,
          kind: 'bool',
          options: [],
          page: page.title,
          section: section.title ?? '',
          hint: '核心只在为 true 时生成对应滤镜/滤镜链；改滑杆数值不会自动打开它',
          placeholder: '',
        })
      }
    }
  }
  fieldIndexCache = entries
  return entries
}

/** 精确取字段（先按完整 path，再按末段名匹配）。 */
export function findField(path: string): FieldIndexEntry | null {
  const index = fieldIndex()
  const exact = index.find(item => item.path === path)
  if (exact) return exact
  const tail = path.split('.').pop() ?? path
  return index.find(item => (item.path.split('.').pop() ?? item.path) === tail) ?? null
}

function describeField(entry: FieldIndexEntry, preset: PresetData, defaults: PresetData): Record<string, unknown> {
  const current = getByPath(preset as Record<string, unknown>, entry.path)
  const fallback = getByPath(defaults as Record<string, unknown>, entry.path)
  return {
    字段名: entry.path,
    标签: entry.label,
    所属页面: entry.section ? `${entry.page} / ${entry.section}` : entry.page,
    类型: entry.kind,
    当前值: current ?? '',
    默认值: fallback ?? '',
    候选值: entry.options.length > 0 ? entry.options.map(option => option.value) : undefined,
    说明: [entry.placeholder, entry.hint].filter(Boolean).join(' · ') || undefined,
  }
}

function queryFields(keyword: string, preset: PresetData, includeCurrent: boolean): Record<string, unknown>[] {
  const index = fieldIndex()
  const hits = index.filter(item =>
    item.path.includes(keyword) || item.label.includes(keyword) || item.page.includes(keyword))
  const defaults = newPreset()
  return hits.slice(0, 40).map(entry => {
    const described = describeField(entry, preset, defaults)
    if (!includeCurrent) {
      delete described['当前值']
      delete described['默认值']
    }
    if (described['候选值'] === undefined) delete described['候选值']
    if (described['说明'] === undefined) delete described['说明']
    return described
  })
}

// ── 参数面板概览：关键项 + 与默认值不同的项 ──

const 关键字段 = [
  '输出容器',
  '视频参数_编码器_具体编码',
  '视频参数_质量控制_控制方式',
  '视频参数_质量控制_值',
  '音频参数_编码器_代号',
  '输出_自动命名选项',
]

export function panelOverview(preset: PresetData): Record<string, unknown> {
  const defaults = newPreset() as Record<string, unknown>
  const current = preset as Record<string, unknown>
  const key: Record<string, unknown> = {}
  for (const path of 关键字段) {
    const value = getByPath(current, path)
    if (value !== undefined && value !== null && String(value) !== '') key[path] = value
  }

  const changed: Record<string, unknown> = {}
  let changedCount = 0
  for (const entry of fieldIndex()) {
    const value = getByPath(current, entry.path)
    const fallback = getByPath(defaults, entry.path)
    if (JSON.stringify(value ?? null) === JSON.stringify(fallback ?? null)) continue
    changedCount++
    if (changedCount <= 40) changed[entry.path] = value
  }

  const lists: Record<string, unknown> = {}
  for (const path of ['滤镜排序系统', '自定义参数_视频参数', '自定义参数_音频参数', '自定义参数_高级参数']) {
    const value = getByPath(current, path)
    if (Array.isArray(value)) lists[path] = `共 ${value.length} 条`
  }

  return {
    关键参数: key,
    与默认值不同的字段: changed,
    不同的字段总数: changedCount,
    列表类字段: lists,
    说明: changedCount > 40 ? `只列出前 40 个不同的字段（共 ${changedCount} 个）` : undefined,
  }
}

// ── 改参数面板（纯函数：传入 preset 副本，返回生效/未变/失败三类字段名）──

export interface PatchOutcome {
  applied: string[]
  unchanged: string[]
  failed: string[]
  missing: string[]
}

export function applyPanelPatch(preset: PresetData, changes: Record<string, unknown>): PatchOutcome {
  const target = preset as Record<string, unknown>
  const outcome: PatchOutcome = { applied: [], unchanged: [], failed: [], missing: [] }
  const allowedExtras = new Set(['预设备注'])
  for (const [key, value] of Object.entries(changes)) {
    if (!key) continue
    const entry = findField(key)
    if (!entry && !allowedExtras.has(key)) {
      outcome.missing.push(key)
      continue
    }
    const path = entry?.path ?? key
    const before = getByPath(target, path)
    try {
      setByPath(target, path, value)
    } catch {
      outcome.failed.push(key)
      continue
    }
    const after = getByPath(target, path)
    if (JSON.stringify(before ?? null) === JSON.stringify(after ?? null)) outcome.unchanged.push(key)
    else outcome.applied.push(path)
  }
  return outcome
}

// ── 浏览器侧执行上下文（由 AgentView 提供，store 与 UI 的副作用都在那边）──

export interface BrowserToolContext {
  getPreset(): PresetData
  /** 就地替换面板预设（保留预设名等其他状态） */
  setPreset(next: PresetData): void
  /** 载入预设（等价于界面「应用预设」，会一并更新预设名） */
  replacePreset(data: PresetData, name: string): void
  getPendingFiles(): string[]
  setPendingFiles(paths: string[]): void
  /** 用当前面板 + 待处理文件入队，返回可读结果；失败时抛错 */
  enqueuePreparedFiles(): Promise<string>
}

export class ToolDenied extends Error {
  constructor() {
    super('用户拒绝了该操作')
  }
}

function parseArgs(call: ToolCall): Record<string, unknown> {
  if (call.arguments.trim() === '') return {}
  const parsed = JSON.parse(call.arguments) as unknown
  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
    throw new Error('工具参数必须是 JSON 对象')
  }
  return parsed as Record<string, unknown>
}

function asString(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

function asStringList(value: unknown): string[] {
  if (!Array.isArray(value)) return []
  return value.filter((item): item is string => typeof item === 'string' && item.trim() !== '').map(item => item.trim())
}

/** 工具参数在确认卡/卡片里最多展示多少字符 */
const ARGS_LIMIT = 4000

/** 执行一个浏览器侧工具（写操作的确认由调用方 AgentView 统一做，这里只管执行）。 */
export async function runBrowserTool(call: ToolCall, tool: AgentToolDef, ctx: BrowserToolContext): Promise<string> {
  const args = parseArgs(call)

  switch (tool.name) {
    case 'get_parameter_panel_state': {
      const wantOverview = args['include_overview'] !== false
      const wantPreview = args['include_command_preview'] === true
      const wantJson = args['include_preset_json'] === true
      if (!wantOverview && !wantPreview && !wantJson) {
        return JSON.stringify({ ok: false, error: '三个开关至少要开一个，否则没有可返回的内容' })
      }
      const preset = ctx.getPreset()
      const payload: Record<string, unknown> = {}
      if (wantOverview) payload['概览'] = panelOverview(preset)
      if (wantPreview) {
        try {
          const result = await api.presets.preview(preset, '<输入文件>', '<输出文件>')
          payload['命令行预览'] = result.命令行
        } catch (error) {
          payload['命令行预览'] = `生成失败：${error instanceof Error ? error.message : String(error)}`
        }
      }
      if (wantJson) payload['预设JSON'] = preset
      return JSON.stringify(payload)
    }

    case 'get_parameter_field_info': {
      const fields = asStringList(args['fields'])
      const keyword = asString(args['query']).trim()
      const includeCurrent = args['include_current_values'] !== false
      const preset = ctx.getPreset()
      if (fields.length === 0 && keyword === '') {
        return JSON.stringify({ ok: false, error: '需要给 fields（精确字段名）或 query（关键词）' })
      }
      const items: Record<string, unknown>[] = []
      const notFound: string[] = []
      const defaults = newPreset()
      for (const name of fields) {
        const entry = findField(name)
        if (!entry) {
          notFound.push(name)
          continue
        }
        items.push(describeField(entry, preset, defaults))
      }
      if (keyword !== '') items.push(...queryFields(keyword, preset, includeCurrent))
      return JSON.stringify({
        字段: items,
        未找到: notFound.length > 0 ? notFound : undefined,
        提示: notFound.length > 0 ? '字段名必须是预设数据_v6 的属性名，可用 query 模糊查' : undefined,
      })
    }

    case 'apply_parameter_panel_patch': {
      const preset = ctx.getPreset()
      const next: PresetData = JSON.parse(JSON.stringify(preset)) as PresetData
      const note = asString(args['note'])
      const presetJson = asString(args['preset_json'])
      let outcome: PatchOutcome | null = null

      if (presetJson.trim() !== '') {
        let replacement: unknown
        try {
          replacement = JSON.parse(presetJson)
        } catch {
          return JSON.stringify({ ok: false, error: 'preset_json 不是合法 JSON' })
        }
        if (typeof replacement !== 'object' || replacement === null || Array.isArray(replacement)) {
          return JSON.stringify({ ok: false, error: 'preset_json 必须是对象' })
        }
        const before = JSON.parse(JSON.stringify(preset)) as Record<string, unknown>
        const merged = Object.assign(newPreset(), replacement) as Record<string, unknown>
        outcome = { applied: [], unchanged: [], failed: [], missing: [] }
        for (const key of Object.keys(merged)) {
          if (JSON.stringify(before[key] ?? null) === JSON.stringify(merged[key] ?? null)) continue
          outcome.applied.push(key)
        }
        ctx.setPreset(merged as PresetData)
      } else {
        const changes = args['changes']
        if (typeof changes !== 'object' || changes === null || Array.isArray(changes)) {
          return JSON.stringify({ ok: false, error: '需要提供 changes（对象）或 preset_json' })
        }
        outcome = applyPanelPatch(next, changes as Record<string, unknown>)
        ctx.setPreset(next)
      }

      const result: Record<string, unknown> = {
        ok: true,
        生效字段: outcome.applied,
        未变化的字段: outcome.unchanged,
        未知字段: outcome.missing,
        写入失败: outcome.failed,
        提示: '生效字段才是真正落到面板上的（部分字段受门控/联动影响可能与预期不同）',
      }
      if (note !== '') result['说明'] = note
      return JSON.stringify(result)
    }

    case 'get_prepare_files':
      return JSON.stringify({ 文件数: ctx.getPendingFiles().length, 文件: ctx.getPendingFiles() })

    case 'set_prepare_files': {
      const mode = asString(args['mode']).trim().toLowerCase() || 'append'
      const paths = asStringList(args['paths'])
      const before = ctx.getPendingFiles()
      if (mode === 'clear') ctx.setPendingFiles([])
      else if (mode === 'replace') ctx.setPendingFiles([...new Set(paths)])
      else if (mode === 'append') ctx.setPendingFiles([...new Set([...before, ...paths])])
      else return JSON.stringify({ ok: false, error: 'mode 只能是 append / replace / clear' })
      const after = ctx.getPendingFiles()
      return JSON.stringify({ ok: true, mode, 变更前: before.length, 变更后: after.length, 文件: after })
    }

    case 'submit_prepare_files_to_queue':
      return await ctx.enqueuePreparedFiles()

    case 'apply_parameter_preset': {
      const source = asString(args['source']).trim().toLowerCase()
      const name = asString(args['name']).trim()
      if (name === '') return JSON.stringify({ ok: false, error: '需要 name' })
      if (source === 'builtin') {
        const list = await api.presets.builtin()
        const hit = list.find(item => item.名称 === name)
        if (!hit) return JSON.stringify({ ok: false, error: `内置预设里没有「${name}」` })
        ctx.replacePreset(Object.assign(newPreset(), hit.数据) as PresetData, name)
      } else if (source === 'user' || source === 'community') {
        const data = await api.presets.load(source, name)
        ctx.replacePreset(Object.assign(newPreset(), data) as PresetData, name)
      } else {
        return JSON.stringify({ ok: false, error: 'source 只能是 user / community / builtin' })
      }
      return JSON.stringify({ ok: true, source, name, 说明: '已加载到参数面板，覆盖了原来编辑中的参数' })
    }

    default:
      return JSON.stringify({ ok: false, error: `浏览器侧没有实现工具：${tool.name}` })
  }
}

// ── 入队前预检（与界面同款规则，不弹兼容对话框，而是把问题还给模型）──

export function checkBeforeEnqueue(preset: PresetData, files: string[]): string | null {
  if (files.length === 0) return '「准备文件」列表是空的，先加文件'
  const 视频编码器 = String(preset['视频参数_编码器_具体编码'] ?? '').trim()
  const 音频编码器 = String(preset['音频参数_编码器_代号'] ?? '').trim()
  if (视频编码器 === '' && (音频编码器 === '' || 音频编码器 === 默认音频编码器)) {
    return '还没有选择视频编码器或音频编码器（音频默认的复制流不算），请先用 apply_parameter_panel_patch 设置 视频参数_编码器_具体编码'
  }
  if (String(preset['输出容器'] ?? '').trim() === '') {
    return '还没有指定输出容器（输出容器 字段为空），后端算不出输出文件名'
  }
  const conflicts = 检查容器兼容性(preset)
  if (conflicts.length > 0) {
    const detail = conflicts.map(item => `${item.项目}：${item.说明}`).join('；')
    return `当前容器的装不下这些「全保留」项，入队会失败——${detail}。请改用 MKV（设置 输出容器=mkv）或逐个关掉对应开关后再入队。`
  }
  return null
}

export function describeToolArgs(args: Record<string, unknown>): string {
  const text = JSON.stringify(args, null, 2)
  return text.length > ARGS_LIMIT ? `${text.slice(0, ARGS_LIMIT)}…` : text
}
