<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { api, copyToClipboard, errorText, type PresetData } from '../api'
import { GROUPS, SUB_NAV, findGroup, newPreset, getByPath, setByPath, 默认音频编码器, type FieldDef, type FieldOption, type SectionDef } from '../schema'
import FormField from '../components/FormField.vue'
import ModernComboBox from '../components/ModernComboBox.vue'
import ParamDialog from '../components/ParamDialog.vue'
import DirPickerDialog from '../components/DirPickerDialog.vue'
import SliderCard from '../components/SliderCard.vue'
import ExtrasPanel from '../components/ExtrasPanel.vue'
import StreamPickerDialog from '../components/StreamPickerDialog.vue'
import CustomParamsPanel from '../components/CustomParamsPanel.vue'
import { useCurrentPreset, usePendingFiles, useToast, 入队兼容确认 } from '../store'

/** 后端 /api/encoder-db 返回的编码器行（只声明用到的字段） */
interface EncoderParamRow {
  参数名?: string
  值列表?: string[]
  默认值?: string
  值范围说明?: string
  提示文本?: string
}
interface VideoEncoderRow {
  名称: string
  分类名称?: string
  类型?: string
  下拉提示文本?: string
  编码预设?: EncoderParamRow
  配置文件?: EncoderParamRow
  场景优化?: EncoderParamRow
  像素格式?: EncoderParamRow
  图片质量?: { 参数名?: string }
}
interface AudioEncoderRow {
  私有ID: string
  显示名称: string
  是否禁用?: boolean
  下拉提示文本?: string
}

const toast = useToast()
// 预设与二级页选择都在全局 store：切走再回来不丢（原版「不要自动重置页面」），
// 「准备文件」页的一键入队也读这份当前预设
const currentPresetStore = useCurrentPreset()
const { preset, saveName, subPage: current } = storeToRefs(currentPresetStore)
const builtinPresets = ref<{ 名称: string; 数据: PresetData }[]>([])
const userPresets = ref<string[]>([])
const encoderDb = ref<Record<string, unknown> | null>(null)
const busy = ref(false)
const previewText = ref('')
const previewLoading = ref(false)
const extraSaveOutput = ref(false)

// 二级窗口（原版 Form_v6_参数面板_XXX）：openDialogId 为空表示没有打开
const openDialogId = ref('')
// 「XX总开关」勾选框：默认不勾（只有预设里本来就有值才显示为已勾选），用户没调过就一定是空的
const dialogToggles = ref<Record<string, boolean>>({})

/** 字段是否有值——总开关的默认勾选状态跟着预设内容走，而不是无条件勾上 */
function 字段有值(value: unknown): boolean {
  if (value === null || value === undefined || value === false) return false
  if (Array.isArray(value)) return value.length > 0
  if (typeof value === 'object') {
    const 对象 = value as Record<string, unknown>
    if ('已设置' in 对象) return 对象.已设置 === true
    return Object.values(对象).some(字段有值)
  }
  if (typeof value === 'number') return value !== 0
  return String(value).trim() !== ''
}

const 窗口已有设置 = computed<Record<string, boolean>>(() => {
  const map: Record<string, boolean> = {}
  for (const group of GROUPS) {
    for (const dialog of group.dialogs ?? []) {
      map[dialog.id] = dialog.fields.some(field => 字段有值(getByPath(preset.value as Record<string, unknown>, field.path)))
    }
  }
  return map
})

function dialogChecked(id: string): boolean {
  const 显式 = dialogToggles.value[id]
  return 显式 === undefined ? (窗口已有设置.value[id] ?? false) : 显式
}

/** 关掉总开关就是真的不用这套滤镜：按字段类型清空，保证生成命令里不会出现 */
function 清空字段(field: FieldDef) {
  const target = preset.value as Record<string, unknown>
  switch (field.type.kind) {
    case 'bool': setByPath(target, field.path, false); break
    case 'number': setByPath(target, field.path, 0); break
    case 'stringlist': setByPath(target, field.path, []); break
    case 'color': setByPath(target, field.path, { 已设置: false, A: 255, R: 0, G: 0, B: 0 }); break
    default: setByPath(target, field.path, '')
  }
}

function setDialogToggle(id: string, checked: boolean) {
  dialogToggles.value = { ...dialogToggles.value, [id]: checked }
  if (checked) return
  const dialog = GROUPS.flatMap(group => group.dialogs ?? []).find(item => item.id === id)
  for (const field of dialog?.fields ?? []) 清空字段(field)
}

// ── 目录选择框（原版 MCB_输出位置 的「浏览 ...」）──
// 目录选择字段 为正在选择的字段路径（空表示没打开），目录选择起始值 为打开时的当前值
const 目录选择字段 = ref('')
const 目录选择起始值 = ref('')

/** 切换页面时关掉还开着的二级窗口 / 目录选择框 */
watch(current, () => { openDialogId.value = ''; 目录选择字段.value = '' })

function 打开目录选择(path: string) {
  目录选择字段.value = path
  目录选择起始值.value = String(getByPath(preset.value as Record<string, unknown>, path) ?? '')
}

function 应用目录选择(chosen: string) {
  setByPath(preset.value as Record<string, unknown>, 目录选择字段.value, chosen)
  目录选择字段.value = ''
  toast.push('ok', `已选择目录：${chosen}`)
}

/** 对话框标题取字段名（如「输出位置（本机）」），以后别的路径字段加 browse 也不用改这里 */
const 目录选择标题 = computed(() => {
  const path = 目录选择字段.value
  for (const group of GROUPS) {
    for (const section of group.sections) {
      const hit = section.fields.find(field => field.path === path)
      if (hit) return `选择目录：${hit.label}`
    }
    for (const dialog of group.dialogs ?? []) {
      const hit = dialog.fields.find(field => field.path === path)
      if (hit) return `选择目录：${hit.label}`
    }
  }
  return '选择目录'
})

// 待处理文件（全局 store，「准备文件」页面维护）
const pendingStore = usePendingFiles()
const pendingFiles = computed(() => pendingStore.files)

const page = computed(() => findGroup(current.value))

/** 预设来源下拉：内置 + 我的预设合并（原版分「预设来源」+ 列表两段，网页版合并为一个下拉） */
const presetChoices = computed(() => [
  ...builtinPresets.value.map(item => ({ value: `内置::${item.名称}`, label: item.名称 })),
  ...userPresets.value.map(name => ({ value: `用户::${name}`, label: name })),
])

// ── 供「起始页面」载入内置预设 ──
function loadExternal(data: PresetData, name = '') {
  currentPresetStore.replace(data, name)
  toast.push('ok', `已载入预设${name ? `「${name}」` : ''}`)
}
defineExpose({ loadExternal })

// ── 编码器数据库驱动的动态下拉 ──
// 首项为空标签，由控件渲染成灰色占位符（原版 WaterText），与原版 Items[0] = "" 一致
function dynamicOptions(path: string): FieldOption[] | null {
  const db = encoderDb.value
  if (!db) return null
  const NONE: FieldOption = { value: '', label: '' }
  // 参数下拉的浮窗说明：参数名 + 值范围 + 默认值 + 可选列表（同理原版 tooltip，全项共用）
  const 值项 = (数据?: { 值列表?: string[]; 提示文本?: string }): FieldOption[] =>
    (数据?.值列表 ?? []).map(v => ({ value: v, label: v, hint: 数据?.提示文本 }))

  if (path === '视频参数_编码器_分类名称') {
    const categories = (db.videoCategories ?? []) as { 类型: number | string; 名称: string; 描述: string }[]
    const type = String(preset.value['视频参数_编码器_类型'] ?? '')
    const list = !type ? categories : categories.filter(c => String(c.类型) === type)
    return [NONE, ...list.map(c => ({ value: c.名称, label: c.名称, hint: c.描述 }))]
  }
  if (path === '视频参数_编码器_具体编码') {
    const encoders = (db.videoEncoders ?? {}) as Record<string, VideoEncoderRow>
    const category = preset.value['视频参数_编码器_分类名称'] as string
    const list = Object.values(encoders).filter(e => e.分类名称 === category)
    return [NONE, ...list.map(e => ({ value: e.名称, label: e.名称, hint: e.下拉提示文本 }))]
  }
  const selected = (db.videoEncoders ?? {}) as Record<string, VideoEncoderRow>
  const 当前编码器 = selected[String(preset.value['视频参数_编码器_具体编码'] ?? '')]
  if (path === '视频参数_编码器_编码预设') return [NONE, ...值项(当前编码器?.编码预设)]
  if (path === '视频参数_编码器_配置文件') return [NONE, ...值项(当前编码器?.配置文件)]
  if (path === '视频参数_编码器_场景优化') return [NONE, ...值项(当前编码器?.场景优化)]
  if (path === '视频参数_编码器_像素格式' || path === '视频参数_色彩管理_像素格式') return [NONE, ...值项(当前编码器?.像素格式)]
  if (path === '音频参数_编码器_代号') {
    const encoders = (db.audioEncoders ?? []) as AudioEncoderRow[]
    const list = encoders.filter(e => !e.是否禁用)
    return [NONE, ...list.map(e => ({ value: e.私有ID, label: e.显示名称, hint: e.下拉提示文本 }))]
  }
  return null
}

/** 原版：图片质量值仅在图片编码器且有质量参数时显示 */
const 显示图片质量值 = computed(() => {
  const db = encoderDb.value
  if (!db) return false
  const rows = (db.videoEncoders ?? {}) as Record<string, VideoEncoderRow>
  const 当前 = rows[String(preset.value['视频参数_编码器_具体编码'] ?? '')]
  return Boolean(当前?.图片质量?.参数名)
})

function visibleFields(section: SectionDef): FieldDef[] {
  return section.fields.filter(field => (field.showKey === 'imageQuality' ? 显示图片质量值.value : true))
}

function dynamicField(field: FieldDef): FieldDef {
  const options = dynamicOptions(field.path)
  if (!options || field.type.kind !== 'select') return field
  return { ...field, type: { ...field.type, options } }
}

// 切换编码器时清掉对新编码器不再合法的专属参数——例如 x265 的 slower 残留给 VAAPI，
// 会让 ffmpeg 报 "Error setting option compression_level to value slower"（核心侧也有同款校验兜底）。
// 级联：改「类型/分类名称」后旧的具体编码可能不在新列表里，残留值照样参与命令生成，需一并校验。
watch(
  () => [preset.value['视频参数_编码器_类型'], preset.value['视频参数_编码器_分类名称'], preset.value['视频参数_编码器_具体编码']],
  () => {
    const target = preset.value as Record<string, unknown>
    for (const path of ['视频参数_编码器_具体编码', '视频参数_编码器_编码预设', '视频参数_编码器_配置文件', '视频参数_编码器_场景优化', '视频参数_色彩管理_像素格式']) {
      const options = dynamicOptions(path)
      if (!options) continue
      const current = String(getByPath(target, path) ?? '')
      if (current !== '' && !options.some(o => o.value === current)) setByPath(target, path, '')
    }
  },
)

// ── 参数总览（重点1）：警告 + 命令行模板 ──
// 行规对齐原版 预设面板总览_v6.显示参数总览：警告红色置顶；值为空的行不显示；全空显示「未设置参数」
function buildOverviewLines(p: Record<string, unknown>): { text: string; tone?: string }[] {
  const lines: { text: string; tone?: string }[] = []
  const warn = (text: string) => lines.push({ text, tone: 'red' })
  const plain = (text: string) => lines.push({ text })
  const val = (key: string) => String(p[key] ?? '').trim()

  if (!val('输出容器')) warn('警告：没有指定输出后缀/输出容器，常规输出文件无法生成正确扩展名')
  if (!val('视频参数_编码器_具体编码')) warn('警告：没有选择视频编码器')
  if (!val('视频参数_比特率_控制方式')) warn('警告：没有设置全局质量控制方式（CRF/VBR/CQP/CBR）')

  if (val('输出容器')) plain(`输出容器：${val('输出容器')}`)
  // 原版：自动命名为默认（附加_递增时间戳）时不显示该行
  if (val('输出_自动命名选项') && val('输出_自动命名选项') !== '附加_递增时间戳') plain(`自动命名方式：${val('输出_自动命名选项')}`)
  if (val('输出命名_开头文本')) plain(`开头文本：${val('输出命名_开头文本')}`)
  if (val('输出命名_替代文本')) plain(`替代文件名：${val('输出命名_替代文本')}`)
  if (val('输出命名_结尾文本')) plain(`结尾文本：${val('输出命名_结尾文本')}`)
  if (val('输出目录')) plain(`输出目录：${val('输出目录')}`)

  if (val('解码参数_解码器')) plain(`解码器：${val('解码参数_解码器')}`)
  if (val('解码参数_CPU解码线程数')) plain(`CPU 解码线程数：${val('解码参数_CPU解码线程数')}`)
  if (val('解码参数_解码数据格式')) plain(`解码数据格式：${val('解码参数_解码数据格式')}`)

  if (val('视频参数_编码器_分类名称')) plain(`视频编码类别：${val('视频参数_编码器_分类名称')}`)
  if (val('视频参数_编码器_具体编码')) plain(`视频编码器：${val('视频参数_编码器_具体编码')}`)
  if (val('视频参数_编码器_编码预设')) plain(`编码预设：${val('视频参数_编码器_编码预设')}`)
  if (val('视频参数_编码器_配置文件')) plain(`配置文件：${val('视频参数_编码器_配置文件')}`)
  if (val('视频参数_编码器_场景优化')) plain(`场景优化：${val('视频参数_编码器_场景优化')}`)

  if (val('视频参数_分辨率')) plain(`分辨率：${val('视频参数_分辨率')}`)
  if (val('视频参数_帧速率')) plain(`帧速率：${val('视频参数_帧速率')}`)
  if (val('视频参数_比特率_控制方式')) {
    plain(`质量控制：${val('视频参数_比特率_控制方式')}   参数名：${val('视频参数_质量控制_参数名') || '—'}   值：${val('视频参数_质量控制_值') || '—'}`)
  }
  const 比特率 = [val('视频参数_比特率_基础'), val('视频参数_比特率_最低值'), val('视频参数_比特率_最高值'), val('视频参数_比特率_缓冲区')]
  if (比特率.some(Boolean)) plain(`基础比特率：${比特率[0] || '—'}   最低：${比特率[1] || '—'}   最高：${比特率[2] || '—'}   缓冲区：${比特率[3] || '—'}`)

  if (val('视频参数_色彩管理_像素格式')) plain(`像素格式：${val('视频参数_色彩管理_像素格式')}`)

  const 音频 = val('音频参数_编码器_代号')
  if (音频) plain(`音频编码器：${音频 === 默认音频编码器 ? '复制流' : 音频}${val('音频参数_比特率') ? `   比特率：${val('音频参数_比特率')}` : ''}`)

  // 保留策略（默认全开）：让用户确认音轨/字幕/元数据不会丢
  const 保留: string[] = []
  if (p['流控制_启用保留其他音频流'] === true) 保留.push('全部音轨')
  if (p['流控制_启用保留其他字幕流'] === true) 保留.push('全部字幕')
  if (val('流控制_元数据选项') === '保留元数据') 保留.push('元数据')
  if (val('流控制_章节选项') === '保留章节') 保留.push('章节')
  if (val('流控制_附件选项') === '保留附件') 保留.push('附件')
  if (保留.length > 0) plain(`保留：${保留.join(' · ')}`)

  if (val('剪辑区间_方法')) plain(`剪辑方法：${val('剪辑区间_方法')}   入点：${val('剪辑区间_入点') || '—'}   出点：${val('剪辑区间_出点') || '—'}`)
  if (val('自定义参数_视频参数')) plain(`自定义视频参数：${val('自定义参数_视频参数')}`)
  if (val('自定义参数_音频参数')) plain(`自定义音频参数：${val('自定义参数_音频参数')}`)
  const 滤镜数 = (p['滤镜排序系统'] as unknown[] | undefined)?.length ?? 0
  if (滤镜数 > 0) plain(`滤镜排序：${滤镜数} 项`)

  if (lines.length === 0) plain('未设置参数')
  return lines
}

const overviewLines = computed(() => buildOverviewLines(preset.value as Record<string, unknown>))

const overviewCopyText = computed(() => overviewLines.value.map(l => l.text).join('\n'))

async function refreshPreview() {
  previewLoading.value = true
  try {
    // 占位符与原版一致：<输入文件> / <输出文件>（预设命令行核心_v6）
    const result = await api.presets.preview(preset.value, '<输入文件>', '<输出文件>')
    previewText.value = result.命令行 || ''
  } catch (error) {
    previewText.value = `生成命令行失败：${errorText(error)}`
  } finally {
    previewLoading.value = false
  }
}

watch(current, id => { if (id === 'overview') refreshPreview() })
watch(preset, () => { if (current.value === 'overview') refreshPreview() }, { deep: true })

// ── 预设管理 ──
function refreshPresetList() {
  api.presets.list().then(list => { userPresets.value = list.user }).catch(() => {})
}

function resetPreset() {
  currentPresetStore.replace(newPreset())
  toast.push('ok', '已重置为默认参数')
}

function loadPreset(source: string, name: string) {
  if (source === 'builtin') {
    const item = builtinPresets.value.find(p => p.名称 === name)
    if (!item) return toast.push('err', '内置预设不存在')
    currentPresetStore.replace(item.数据, name)
    toast.push('ok', '已读取预设：' + name)
    return
  }
  api.presets.load(source, name).then(data => {
    currentPresetStore.replace(data, name)
    toast.push('ok', '已读取预设：' + name)
  }).catch(error => toast.push('err', errorText(error)))
}

function savePreset() {
  if (!saveName.value.trim()) return toast.push('err', '请先填写预设名称')
  api.presets.save('user', saveName.value.trim(), preset.value)
    .then(() => { toast.push('ok', '预设已保存'); refreshPresetList() })
    .catch(error => toast.push('err', errorText(error)))
}

// ── 预设管理：单击仅预览（中/右栏显示参数总览与命令行），双击或「读取」才应用 ──
// 对齐原版 Form_v6_参数面板_预设管理：SelectedIndexChanged=预览，ItemDoubleClick/读取按钮=应用
const selectedPreset = ref<{ source: 'builtin' | 'user'; name: string; data: PresetData } | null>(null)
const selectedOverview = ref<{ text: string; tone?: string }[]>([])
const selectedCommandLine = ref('')
let previewSeq = 0

async function selectPreset(source: 'builtin' | 'user', name: string) {
  let data: PresetData | undefined
  if (source === 'builtin') {
    data = builtinPresets.value.find(item => item.名称 === name)?.数据
  } else {
    try {
      data = await api.presets.load('user', name)
    } catch (error) {
      return toast.push('err', errorText(error))
    }
  }
  if (!data) return
  const full = Object.assign(newPreset(), data)
  selectedPreset.value = { source, name, data: full }
  saveName.value = name
  selectedOverview.value = buildOverviewLines(full as Record<string, unknown>)
  const seq = ++previewSeq
  selectedCommandLine.value = '正在生成…'
  api.presets.preview(full, '<输入文件>', '<输出文件>')
    .then(result => { if (seq === previewSeq) selectedCommandLine.value = result.命令行 || '' })
    .catch(error => { if (seq === previewSeq) selectedCommandLine.value = `生成命令行失败：${errorText(error)}` })
}

function applySelectedPreset() {
  const item = selectedPreset.value
  if (!item) return toast.push('err', '请先选择一个预设')
  loadPreset(item.source, item.name)
}

// 顶部下拉的值形如「内置::名称 / 用户::名称」，选中即预览（与点列表项等效，不直接应用）
watch(saveName, name => {
  if (name.startsWith('内置::')) selectPreset('builtin', name.slice(4))
  else if (name.startsWith('用户::')) selectPreset('user', name.slice(4))
})

function removePreset(name: string) {
  api.presets.remove('user', name)
    .then(() => {
      toast.push('ok', `已删除预设「${name}」`)
      if (selectedPreset.value?.source === 'user' && selectedPreset.value.name === name) selectedPreset.value = null
      refreshPresetList()
    })
    .catch(error => toast.push('err', errorText(error)))
}

function exportPreset() {
  const blob = new Blob([JSON.stringify(preset.value, null, 2)], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = `${saveName.value || '预设'}.3fui`
  anchor.click()
  URL.revokeObjectURL(url)
}

function importPreset(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]
  if (!file) return
  api.presets.importFile(file).then(result => {
    toast.push('ok', `已导入预设「${result.name}」`)
    refreshPresetList()
  }).catch(error => toast.push('err', errorText(error)))
  ;(event.target as HTMLInputElement).value = ''
}

// ── 滤镜排序 ──
interface FilterOrderItem { 实例ID: string; 显示名称: string; 是自定义滤镜: boolean; 滤镜标识符?: string; 滤镜目标流类型?: string; 自定义滤镜内容: string }

const filterOrder = computed<FilterOrderItem[]>(() => (preset.value['滤镜排序系统'] as FilterOrderItem[] | undefined) ?? [])

function moveFilter(index: number, delta: number) {
  const list = [...filterOrder.value]
  const next = index + delta
  if (next < 0 || next >= list.length) return
  ;[list[index], list[next]] = [list[next], list[index]]
  preset.value['滤镜排序系统'] = list
}

function removeFilter(index: number) {
  const list = [...filterOrder.value]
  list.splice(index, 1)
  preset.value['滤镜排序系统'] = list
}

function addCustomFilter(音频: boolean) {
  const list = [...filterOrder.value]
  list.push({
    实例ID: Math.random().toString(16).slice(2, 10),
    显示名称: 音频 ? '自定义音频滤镜' : '自定义视频滤镜',
    是自定义滤镜: true,
    滤镜目标流类型: 音频 ? '音频' : '视频',
    自定义滤镜内容: '',
  })
  preset.value['滤镜排序系统'] = list
  toast.push('ok', '已添加自定义滤镜，请在下方填写滤镜内容')
}

function updateFilterContent(index: number, text: string) {
  const list = [...filterOrder.value]
  list[index] = { ...list[index], 自定义滤镜内容: text }
  preset.value['滤镜排序系统'] = list
}

// ── 添加到队列 ──
async function addFilesToQueue() {
  if (pendingFiles.value.length === 0) return toast.push('err', '请先在「准备文件」页面添加文件')
  // 没有编码器就没法生成命令行，提前拦住（原版同款校验：参数总览页的「没有选择视频编码器」警告）
  // 音频默认是「复制流」，那是默认值而不是用户主动选的编码器，不能让它把这道校验顶开
  const 视频编码器 = String(preset.value['视频参数_编码器_具体编码'] ?? '').trim()
  const 音频编码器 = String(preset.value['音频参数_编码器_代号'] ?? '').trim()
  if (视频编码器 === '' && (音频编码器 === '' || 音频编码器 === 默认音频编码器)) {
    current.value = 'vcodec'
    return toast.push('err', '还没有选择视频编码器或音频编码器，请在「视频参数 | 编码器」页选一个')
  }
  // 输出容器为空时后端算不出输出路径，ffmpeg 会以空文件名启动并失败（原版同款警告见参数总览）
  if (String(preset.value['输出容器'] ?? '').trim() === '') {
    current.value = 'output'
    return toast.push('err', '还没有指定输出容器（后缀），请在「输出文件设置」页选择，否则无法生成输出文件名')
  }
  // 「全保留」策略装不下当前容器时，弹窗让用户选：换 MKV / 丢弃不兼容项 / 取消
  if (!(await 入队兼容确认(currentPresetStore))) return
  busy.value = true
  api.queue.addTasks([...pendingFiles.value], preset.value, saveName.value.trim() || undefined)
    .then(tasks => {
      toast.push('ok', `已添加 ${tasks.length} 个任务到队列`)
      pendingStore.clear()
    })
    .catch(error => toast.push('err', errorText(error)))
    .finally(() => { busy.value = false })
}

function copyText(text: string, label: string) {
  copyToClipboard(text).then(ok => toast.push(ok ? 'ok' : 'err', ok ? `${label}已复制` : '复制失败'))
}

function insertAction(action: { text: string; targetPath: string; insert: string }) {
  const target = preset.value as Record<string, unknown>
  const existing = String(getByPath(target, action.targetPath) ?? '')
  setByPath(target, action.targetPath, existing ? `${existing}\n${action.insert}` : action.insert)
  toast.push('ok', '已插入预制条目')
}

onMounted(() => {
  api.presets.builtin().then(list => { builtinPresets.value = list }).catch(() => {})
  refreshPresetList()
  api.encoderDb().then(data => { encoderDb.value = data }).catch(() => {})
  refreshPreview()
})
</script>

<template>
  <div class="preset-layout">
    <!-- 二级纵向菜单：210px / #242424（原版 Form_v6_参数面板 的 ModernTabListControl） -->
    <nav class="subnav">
      <template v-for="(entry, index) in SUB_NAV" :key="index">
        <div v-if="entry.type === 'sep'" class="subnav-sep" />
        <div v-else class="subnav-item" :class="{ active: current === entry.id }" @click="current = entry.id">
          {{ entry.label }}
        </div>
      </template>
    </nav>

    <div class="preset-page">
      <!-- 工具行（所有子页共用） -->
      <div class="page-header">
        <h2>{{ page?.title }}</h2>
        <span class="desc">{{ page?.hint ?? '' }}</span>
        <span class="spacer" />
        <button class="small" @click="resetPreset">新建预设</button>
        <button class="small" @click="current = 'presets'">预设管理</button>
        <button class="small" @click="exportPreset">导出 .3fui</button>
        <button class="small" @click="savePreset">保存到服务器</button>
        <button class="small primary" :disabled="busy || pendingFiles.length === 0" @click="addFilesToQueue">
          {{ pendingFiles.length > 0 ? `添加到队列（${pendingFiles.length} 个文件）` : '添加到队列' }}
        </button>
      </div>

      <!-- ══ 参数总览 ══ -->
      <template v-if="current === 'overview'">
        <div class="overview-wrap">
          <div class="overview-panel">
            <div class="overview-head">参数总览</div>
            <div class="overview-body">
              <div class="overview-gutter">
                <div v-for="(_, i) in overviewLines" :key="i">{{ i + 1 }}</div>
              </div>
              <div class="overview-text">
                <div v-for="(line, i) in overviewLines" :key="i" :class="{ 'txt-red': line.tone === 'red' }">{{ line.text || ' ' }}</div>
              </div>
            </div>
          </div>

          <div class="overview-panel">
            <div class="overview-head">命令行模板</div>
            <div class="overview-body">
              <div class="overview-gutter">
                <div v-for="(_, i) in (previewText ? previewText.split('\n') : [''])" :key="i">{{ i + 1 }}</div>
              </div>
              <div class="overview-text mono">{{ previewLoading ? '正在生成…' : (previewText || '（暂无命令行，请先选择编码器与输出容器）') }}</div>
            </div>
          </div>
        </div>
        <div class="overview-actions">
          <button @click="copyText(overviewCopyText, '参数总览')">复制参数总览</button>
          <button @click="copyText(previewText, '命令行模板')">复制命令行模板</button>
        </div>
      </template>

      <!-- ══ 预设管理 ══ -->
      <template v-else-if="current === 'presets'">
        <div class="panel-box panel">
          <div class="flex-wrap">
            <ModernComboBox
              v-model="saveName"
              :options="presetChoices"
              watermark="选择预设…"
              :width="260"
              :max-items="14"
            />
            <button class="small" @click="applySelectedPreset">读取</button>
            <button class="small" @click="savePreset">保存</button>
            <button class="small" @click="exportPreset">导出</button>
            <label class="btn small" style="cursor: pointer">
              导入
              <input type="file" accept=".3fui,.json" style="display: none" @change="importPreset" />
            </label>
            <button class="small" @click="resetPreset">重置所有</button>
            <label class="checkbox-row" style="margin: 0 0 0 6px">
              <input v-model="extraSaveOutput" type="checkbox" />
              <span>额外保存输出位置</span>
            </label>
          </div>
          <div class="flex" style="margin-top: 8px">
            <input v-model="saveName" type="text" placeholder="这里显示选中的预设项名称，主用于显示完整名称，也可在此直接重命名" style="flex: 1" />
            <button class="small" @click="savePreset">变更名称</button>
          </div>
        </div>

        <!-- 三栏：预设列表 | 参数总览预览 | 命令行模板预览（单击预览，双击/读取应用） -->
        <div class="grid-3" style="margin-top: 10px">
          <div class="panel-box panel" style="min-height: 220px">
            <div class="panel-title">预设列表（双击或点「读取」应用）</div>
            <div style="max-height: 46vh; overflow: auto">
              <div class="muted" style="padding: 4px 6px">开发者内置（{{ builtinPresets.length }}）</div>
              <div
                v-for="item in builtinPresets"
                :key="'b-' + item.名称"
                class="preset-card"
                :class="{ selected: selectedPreset?.source === 'builtin' && selectedPreset?.name === item.名称 }"
                @click="selectPreset('builtin', item.名称)"
                @dblclick="loadPreset('builtin', item.名称)"
              >
                <div>{{ item.名称 }}</div>
              </div>
              <div class="muted" style="padding: 4px 6px; margin-top: 6px">我的预设（{{ userPresets.length }}）</div>
              <div
                v-for="name in userPresets"
                :key="'u-' + name"
                class="preset-card"
                :class="{ selected: selectedPreset?.source === 'user' && selectedPreset?.name === name }"
                @click="selectPreset('user', name)"
                @dblclick="loadPreset('user', name)"
              >
                <div class="flex-between">
                  <span>{{ name }}</span>
                  <button class="small danger" @click.stop="removePreset(name)">删除</button>
                </div>
              </div>
              <div v-if="userPresets.length === 0" class="empty">暂无预设。导入 Windows 版导出的 .3fui 即可使用。</div>
            </div>
          </div>
          <div class="panel-box panel" style="min-height: 220px">
            <div class="panel-title">参数总览{{ selectedPreset ? `：${selectedPreset.name}` : '' }}</div>
            <div v-if="selectedPreset" class="overview-text" style="padding: 8px; max-height: 46vh; overflow: auto">
              <div v-for="(line, i) in selectedOverview" :key="i" :class="{ 'txt-red': line.tone === 'red' }">{{ line.text }}</div>
            </div>
            <div v-else class="empty">单击左侧预设查看参数总览，不会改动当前面板</div>
          </div>
          <div class="panel-box panel" style="min-height: 220px">
            <div class="panel-title">命令行模板</div>
            <div v-if="selectedPreset" class="overview-text mono" style="padding: 8px; max-height: 46vh; overflow: auto; white-space: pre-wrap">{{ selectedCommandLine }}</div>
            <div v-else class="empty">单击左侧预设查看命令行模板</div>
          </div>
        </div>

        <div class="panel-box panel" style="margin-top: 10px">
          <div class="panel-title">预设备注</div>
          <textarea
            :value="(preset['预设备注'] as string) ?? ''"
            rows="2"
            placeholder="这里显示选中的预设项备注，备注会在鼠标移上时显示在侧边"
            @input="preset['预设备注'] = ($event.target as HTMLTextAreaElement).value"
          />
          <button class="small" style="margin-top: 8px" @click="savePreset">变更备注</button>
        </div>
      </template>

      <!-- ══ 滤镜排序 ══ -->
      <template v-else-if="current === 'filterorder'">
        <div class="panel-box panel">
          <div class="panel-title">滤镜排序系统（{{ filterOrder.length }} 项）</div>
          <div class="muted" style="margin-bottom: 8px">
            列表为空时按参数面板的默认顺序生成滤镜图；列表非空时按此处的顺序依次生成。
          </div>
          <table class="list">
            <thead>
              <tr>
                <th style="width: 50px">序号</th>
                <th style="width: 200px">显示名称</th>
                <th style="width: 110px">目标流</th>
                <th>自定义滤镜内容</th>
                <th style="width: 150px">操作</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(item, index) in filterOrder" :key="item.实例ID">
                <td class="mono">{{ index + 1 }}</td>
                <td>{{ item.显示名称 }}</td>
                <td>{{ item.滤镜目标流类型 ?? '视频' }}</td>
                <td>
                  <input
                    v-if="item.是自定义滤镜"
                    :value="item.自定义滤镜内容"
                    type="text"
                    class="mono"
                    style="width: 100%"
                    placeholder="如 hqdn3d=1.5:1.5:6:6"
                    @input="updateFilterContent(index, ($event.target as HTMLInputElement).value)"
                  />
                  <span v-else class="muted">（内置滤镜，由参数面板生成）</span>
                </td>
                <td class="actions">
                  <button class="small" :disabled="index === 0" @click="moveFilter(index, -1)">上移</button>
                  <button class="small" style="margin-left: 4px" :disabled="index === filterOrder.length - 1" @click="moveFilter(index, 1)">下移</button>
                  <button class="small danger" style="margin-left: 4px" @click="removeFilter(index)">删除</button>
                </td>
              </tr>
              <tr v-if="filterOrder.length === 0">
                <td colspan="5" class="empty">暂无排序项，按参数面板默认顺序生成滤镜图</td>
              </tr>
            </tbody>
          </table>
          <div class="flex" style="margin-top: 10px">
            <button class="small" @click="addCustomFilter(false)">添加自定义视频滤镜</button>
            <button class="small" @click="addCustomFilter(true)">添加自定义音频滤镜</button>
          </div>
        </div>
      </template>

      <!-- ══ 附加内容（元数据 / 章节 / 附件 / 预设备注 标签页）══ -->
      <template v-else-if="page?.kind === 'extras'">
        <ExtrasPanel />
      </template>

      <!-- ══ 自定义参数（说明 / 流自定义 / 在位置插入 / 完全自己写 标签页）══ -->
      <template v-else-if="page?.kind === 'custom'">
        <CustomParamsPanel />
      </template>

      <!-- ══ 普通字段页 ══ -->
      <template v-else-if="page">
        <div v-if="page.legend?.length" class="page-legend">
          <span v-for="(part, i) in page.legend" :key="i" :class="part.tone ? `txt-${part.tone}` : ''">{{ part.text }}</span>
        </div>
        <div v-if="page.hints?.length" style="margin-bottom: 6px">
          <div v-for="(line, i) in page.hints" :key="i" class="section-hint" :class="`txt-${line.tone ?? 'muted'}`">{{ line.text }}</div>
        </div>

        <div v-for="(section, sIndex) in page.sections" :key="sIndex" class="param-section">
          <div v-if="section.title || section.hint" class="section-head">
            <span v-if="section.title" class="section-title">{{ section.title }}</span>
            <span v-if="section.hint" class="section-hint">{{ section.hint }}</span>
          </div>
          <div v-for="(line, i) in section.hints ?? []" :key="'hint' + i" class="section-hint section-body-hint" :class="typeof line === 'string' ? '' : line.tone ? `txt-${line.tone}` : ''">{{ typeof line === 'string' ? line : line.text }}</div>
          <button v-if="section.action" class="small" style="margin: 4px 0 8px" @click="insertAction(section.action)">
            {{ section.action.text }}
          </button>
          <div v-if="section.sliders?.length" class="slider-grid">
            <SliderCard
              v-for="slider in section.sliders"
              :key="slider.valuePath"
              :slider="slider"
              :preset="(preset as Record<string, unknown>)"
            />
          </div>
          <div v-else class="field-grid">
            <FormField
              v-for="field in visibleFields(section)"
              :key="field.path"
              :field="dynamicField(field)"
              :preset="(preset as Record<string, unknown>)"
              @open-dialog="openDialogId = $event"
              @browse="打开目录选择"
            />
            <!-- 小节内的二级窗口入口（原版一排 ModernButton，150x32） -->
            <div v-if="section.buttons?.length" class="section-buttons">
              <button
                v-for="item in section.buttons"
                :key="item.dialog"
                class="param-btn"
                @click="openDialogId = item.dialog"
              >{{ item.text }}</button>
            </div>
          </div>
        </div>

        <!-- ══ 二级窗口（原版 Form_v6_参数面板_XXX）══ -->
        <template v-for="dialog in page.dialogs ?? []" :key="dialog.id">
          <ParamDialog
            v-if="openDialogId === dialog.id"
            :title="dialog.title"
            :hint="dialog.hint"
            @close="openDialogId = ''"
          >
            <label v-if="dialog.toggle" class="checkbox-row" style="margin: 0">
              <input
                type="checkbox"
                :checked="dialogChecked(dialog.id)"
                @change="setDialogToggle(dialog.id, ($event.target as HTMLInputElement).checked)"
              />
              <span>{{ dialog.toggle.text }}</span>
            </label>
            <template v-if="dialogChecked(dialog.id)">
              <FormField
                v-for="field in dialog.fields"
                :key="field.path"
                :field="dynamicField(field)"
                :preset="(preset as Record<string, unknown>)"
                @open-dialog="openDialogId = $event"
                @browse="打开目录选择"
              />
            </template>
          </ParamDialog>
        </template>

        <!-- 可视化流选择器（非表单二级窗口，独立组件） -->
        <StreamPickerDialog
          v-if="openDialogId === 'streamPicker'"
          :preset="(preset as Record<string, unknown>)"
          @close="openDialogId = ''"
        />
      </template>
    </div>

    <!-- 路径字段的「浏览…」：图形化目录选择框（可覆盖在任意页面上） -->
    <DirPickerDialog
      v-if="目录选择字段"
      :model-value="目录选择起始值"
      :title="目录选择标题"
      @close="目录选择字段 = ''"
      @pick="应用目录选择"
    />
  </div>
</template>
