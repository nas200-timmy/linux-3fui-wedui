<script setup lang="ts">
// 附加内容：元数据 / 章节 / 附件 / 预设备注（原版「附加内容」页的顶部标签页结构）。
// 数据直接读写 store 当前预设的既有字段（元数据_要写入的信息 / 附件_要写入的附件 / 章节_*），核心零改动。
import { computed, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import ModernComboBox from './ModernComboBox.vue'
import DirPickerDialog from './DirPickerDialog.vue'
import ParamDialog from './ParamDialog.vue'
import { ATTACH_TYPE, CHAPTER_SOURCE, META_PRESETS } from '../schema'
import { useCurrentPreset, useToast } from '../store'

const TABS = ['元数据', '章节', '附件', '预设备注']
const tab = ref(0)

const toast = useToast()
const { preset } = storeToRefs(useCurrentPreset())
const model = computed(() => preset.value as Record<string, unknown>)

// ── 通用：导出 / 导入 ──
function downloadJson(filename: string, data: unknown) {
  const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = filename
  anchor.click()
  URL.revokeObjectURL(url)
}

function readJsonFile(file: File): Promise<unknown> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => {
      try {
        resolve(JSON.parse(String(reader.result)))
      } catch {
        reject(new Error('不是有效的 JSON 文件'))
      }
    }
    reader.onerror = () => reject(new Error('文件读取失败'))
    reader.readAsText(file)
  })
}

// ── 元数据：字段 | 值 表格 ──
interface MetaItem { 字段: string; 值: string }

const metaList = computed({
  get: () => (model.value['元数据_要写入的信息'] as MetaItem[] | undefined) ?? [],
  set: (value: MetaItem[]) => { model.value['元数据_要写入的信息'] = value },
})
const metaChecked = ref<boolean[]>([])
watch(() => metaList.value.length, length => {
  if (metaChecked.value.length !== length) metaChecked.value = metaList.value.map(() => false)
}, { immediate: true })

const metaPick = ref('')
watch(metaPick, value => {
  if (!value) return
  metaList.value = [...metaList.value, { 字段: value, 值: '' }]
  metaPick.value = ''
})

function removeMetaRows() {
  const next = metaList.value.filter((_, i) => !metaChecked.value[i])
  const removed = metaList.value.length - next.length
  if (!removed) return toast.push('err', '请先勾选要删除的行')
  metaList.value = next
  toast.push('ok', `已删除 ${removed} 行`)
}

function clearMeta() {
  if (metaList.value.length === 0) return
  metaList.value = []
  toast.push('ok', '已清空元数据')
}

const metaImportInput = ref<HTMLInputElement | null>(null)
async function importMeta(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  try {
    const data = await readJsonFile(file)
    if (!Array.isArray(data)) throw new Error('期望一个 JSON 数组')
    const items = (data as unknown[]).map(item => {
      const row = item as Record<string, unknown>
      if (typeof row !== 'object' || row === null || typeof row['字段'] !== 'string') throw new Error('行结构应为 { 字段, 值 }')
      return { 字段: row['字段'], 值: typeof row['值'] === 'string' ? row['值'] : String(row['值'] ?? '') }
    })
    metaList.value = [...metaList.value, ...items]
    toast.push('ok', `已导入 ${items.length} 行元数据`)
  } catch (error) {
    toast.push('err', `导入失败：${String(error instanceof Error ? error.message : error)}`)
  }
}

// ── 章节 ──
const chapterSource = computed(() => String(model.value['章节_来源'] ?? ''))
const chapterPicker = ref(false)
const chapterHelp = ref(false)

function applyChapterPath(path: string) {
  model.value['章节_文件路径'] = path
  chapterPicker.value = false
}

// ── 附件：类型 | 文件路径 表格 ──
interface AttachItem { 类型: string; 文件路径: string }

const attachList = computed({
  get: () => (model.value['附件_要写入的附件'] as AttachItem[] | undefined) ?? [],
  set: (value: AttachItem[]) => { model.value['附件_要写入的附件'] = value },
})
const attachChecked = ref<boolean[]>([])
watch(() => attachList.value.length, length => {
  if (attachChecked.value.length !== length) attachChecked.value = attachList.value.map(() => false)
}, { immediate: true })

const attachPick = ref('')
watch(attachPick, value => {
  if (!value) return
  attachList.value = [...attachList.value, { 类型: value, 文件路径: '' }]
  attachPick.value = ''
})

const attachPickerIndex = ref(-1)
function openAttachPicker(index: number) {
  attachPickerIndex.value = index
}

function applyAttachPath(path: string) {
  const index = attachPickerIndex.value
  if (index >= 0 && attachList.value[index]) attachList.value[index] = { ...attachList.value[index], 文件路径: path }
  attachPickerIndex.value = -1
}

function removeAttachRows() {
  const next = attachList.value.filter((_, i) => !attachChecked.value[i])
  const removed = attachList.value.length - next.length
  if (!removed) return toast.push('err', '请先勾选要删除的行')
  attachList.value = next
  toast.push('ok', `已删除 ${removed} 行`)
}

function clearAttach() {
  if (attachList.value.length === 0) return
  attachList.value = []
  toast.push('ok', '已清空附件')
}

const attachImportInput = ref<HTMLInputElement | null>(null)
async function importAttach(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  try {
    const data = await readJsonFile(file)
    if (!Array.isArray(data)) throw new Error('期望一个 JSON 数组')
    const items = (data as unknown[]).map(item => {
      const row = item as Record<string, unknown>
      if (typeof row !== 'object' || row === null || typeof row['类型'] !== 'string') throw new Error('行结构应为 { 类型, 文件路径 }')
      return { 类型: row['类型'], 文件路径: typeof row['文件路径'] === 'string' ? row['文件路径'] : String(row['文件路径'] ?? '') }
    })
    attachList.value = [...attachList.value, ...items]
    toast.push('ok', `已导入 ${items.length} 行附件`)
  } catch (error) {
    toast.push('err', `导入失败：${String(error instanceof Error ? error.message : error)}`)
  }
}
</script>

<template>
  <div class="extras-panel">
    <!-- 顶部标签页（原版：元数据 | 章节 | 附件） -->
    <div class="tab-bar">
      <button v-for="(label, i) in TABS" :key="label" class="tab-item" :class="{ active: tab === i }" @click="tab = i">{{ label }}</button>
    </div>

    <!-- ══ 元数据 ══ -->
    <template v-if="tab === 0">
      <div class="extras-hint">
        <span class="txt-gold">元数据</span>
        <span class="muted">向输出文件中写入自定义元数据，流的元数据请写自定义参数；值支持通配字符串</span>
      </div>
      <div class="flex" style="margin-bottom: 8px; flex-wrap: wrap">
        <ModernComboBox v-model="metaPick" :options="META_PRESETS" watermark="添加预设项" :width="160" :max-items="12" />
        <button class="small" @click="removeMetaRows">删除所选</button>
        <button class="small" @click="clearMeta">全部清空</button>
        <button class="small" @click="downloadJson('元数据.json', metaList)">导出</button>
        <button class="small" @click="metaImportInput?.click()">导入</button>
        <input ref="metaImportInput" type="file" accept=".json" style="display: none" @change="importMeta" />
      </div>
      <table class="list">
        <thead>
          <tr><th style="width: 36px" /><th style="width: 220px">字段</th><th>值</th></tr>
        </thead>
        <tbody>
          <tr v-for="(item, i) in metaList" :key="i" :class="{ checked: metaChecked[i] }">
            <td><input v-model="metaChecked[i]" type="checkbox" /></td>
            <td>
              <ModernComboBox :model-value="item.字段" :options="META_PRESETS" watermark="字段名" :width="200" editable @update:model-value="item.字段 = $event" />
            </td>
            <td><input v-model="item.值" type="text" placeholder="值（支持通配字符串）" style="width: 100%" /></td>
          </tr>
          <tr v-if="metaList.length === 0"><td colspan="3" class="empty">暂无元数据，用上方「添加预设项」或导入添加</td></tr>
        </tbody>
      </table>
    </template>

    <!-- ══ 章节 ══ -->
    <template v-else-if="tab === 1">
      <div class="extras-hint">
        <span class="txt-gold">章节</span>
        <span class="muted">向输出文件中写入自定义章节，请自行编辑并准备好符合标准的章节文本文档</span>
      </div>
      <div class="field-item">
        <div class="field-label">章节来源</div>
        <div class="field-control">
          <ModernComboBox :model-value="chapterSource" :options="CHAPTER_SOURCE" watermark="选择章节来源" :width="200" @update:model-value="model['章节_来源'] = $event" />
        </div>
      </div>
      <div class="field-item" style="margin-top: 8px">
        <div class="field-label">章节文件</div>
        <div class="field-control">
          <input
            :value="String(model['章节_文件路径'] ?? '')"
            type="text"
            placeholder="如 /media/chapters.txt"
            @input="model['章节_文件路径'] = ($event.target as HTMLInputElement).value"
          />
          <button class="small" @click="chapterPicker = true">选择文件</button>
          <button class="small" @click="chapterHelp = true">教程</button>
        </div>
      </div>
      <div class="section-hint" style="margin-top: 8px">
        {{ chapterSource === '媒体文件' ? '从所选媒体文件中复制章节轨（-map_chapters）。' : '文本文档需为 ffmetadata 格式（点「教程」查看示例），选择「媒体文件」则直接复制其章节。' }}
      </div>
    </template>

    <!-- ══ 附件 ══ -->
    <template v-else-if="tab === 2">
      <div class="extras-hint">
        <span class="txt-gold">附件</span>
        <span class="muted">向输出文件中塞入附件，例如图片、字体、文本文档，甚至是封面图</span>
      </div>
      <div class="flex" style="margin-bottom: 8px; flex-wrap: wrap">
        <ModernComboBox v-model="attachPick" :options="ATTACH_TYPE" watermark="添加附件" :width="160" />
        <button class="small" @click="removeAttachRows">删除所选</button>
        <button class="small" @click="clearAttach">全部清空</button>
        <button class="small" @click="downloadJson('附件.json', attachList)">导出</button>
        <button class="small" @click="attachImportInput?.click()">导入</button>
        <input ref="attachImportInput" type="file" accept=".json" style="display: none" @change="importAttach" />
      </div>
      <table class="list">
        <thead>
          <tr><th style="width: 36px" /><th style="width: 180px">类型</th><th>文件路径</th><th style="width: 90px" /></tr>
        </thead>
        <tbody>
          <tr v-for="(item, i) in attachList" :key="i" :class="{ checked: attachChecked[i] }">
            <td><input v-model="attachChecked[i]" type="checkbox" /></td>
            <td>
              <ModernComboBox :model-value="item.类型" :options="ATTACH_TYPE" watermark="类型" :width="160" @update:model-value="item.类型 = $event" />
            </td>
            <td><input v-model="item.文件路径" type="text" placeholder="如 /media/cover.png" style="width: 100%" /></td>
            <td><button class="small" @click="openAttachPicker(i)">浏览…</button></td>
          </tr>
          <tr v-if="attachList.length === 0"><td colspan="4" class="empty">暂无附件，用上方「添加附件」或导入添加</td></tr>
        </tbody>
      </table>
    </template>

    <!-- ══ 预设备注 ══ -->
    <template v-else>
      <div class="extras-hint">
        <span class="txt-gold">预设备注</span>
        <span class="muted">备注会随 .3fui 预设一起保存</span>
      </div>
      <textarea
        :value="String(model['预设备注'] ?? '')"
        rows="3"
        placeholder="这里显示选中的预设项备注，备注会在鼠标移上时显示在侧边"
        @input="model['预设备注'] = ($event.target as HTMLTextAreaElement).value"
      />
    </template>

    <!-- 章节文件选择（文件模式） -->
    <DirPickerDialog
      v-if="chapterPicker"
      :model-value="String(model['章节_文件路径'] ?? '')"
      title="选择章节文件"
      files
      @close="chapterPicker = false"
      @pick="applyChapterPath"
    />

    <!-- 附件行文件选择（文件模式） -->
    <DirPickerDialog
      v-if="attachPickerIndex >= 0"
      :model-value="attachPickerIndex < attachList.length ? attachList[attachPickerIndex].文件路径 : ''"
      title="选择附件文件"
      files
      @close="attachPickerIndex = -1"
      @pick="applyAttachPath"
    />

    <!-- 章节文本文档格式教程 -->
    <ParamDialog v-if="chapterHelp" title="章节文本文档格式（ffmetadata）" @close="chapterHelp = false">
      <div class="muted" style="line-height: 1.9">
        章节来源选「文本文档」时，需要一个 ffmetadata 格式的文本文件，示例如下：
      </div>
      <pre class="mono" style="background: var(--bg-deep); padding: 8px; border-radius: var(--radius); font-size: 12px; line-height: 1.7">;FFMETADATA1
[CHAPTER]
TIMEBASE=1/1000
START=0
END=60000
title=第一章
[CHAPTER]
TIMEBASE=1/1000
START=60000
END=180000
title=第二章</pre>
      <div class="muted" style="line-height: 1.9">
        START/END 单位为毫秒；保存为 .txt 文本文件后在上方选择即可（-map_chapters 注入）。<br />
        章节来源选「媒体文件」则无需编辑，直接选一个有章节的视频/音频文件。
      </div>
    </ParamDialog>
  </div>
</template>
