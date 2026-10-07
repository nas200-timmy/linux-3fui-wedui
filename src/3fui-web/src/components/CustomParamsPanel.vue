<script setup lang="ts">
// 自定义参数：自定义参数说明 / 流自定义参数 / 在位置插入参数 / 完全自己写（原版顶部 4 标签页）。
// 字段路径与单页平铺版完全一致，纯版式重排。
import { computed, ref } from 'vue'
import { storeToRefs } from 'pinia'
import CodeTextarea from './CodeTextarea.vue'
import { getByPath, setByPath } from '../schema'
import { useCurrentPreset } from '../store'

const TABS = ['自定义参数说明', '流自定义参数', '在位置插入参数', '完全自己写']
const tab = ref(1)

const { preset } = storeToRefs(useCurrentPreset())
const model = computed(() => preset.value as Record<string, unknown>)

function valueOf(path: string): string {
  return String(getByPath(model.value, path) ?? '')
}
function setValue(path: string, value: string) {
  setByPath(model.value, path, value)
}

/** 占位符说明（原版「自定义参数说明」页）：值直接给核心做整串替换 */
const TOKENS: { token: string; desc: string }[] = [
  { token: '<InputFile>', desc: '表示输入文件完整路径' },
  { token: '<InputFileWithOutExtension>', desc: '表示不包含后缀的输入文件路径，注意点号也是算在后缀里的' },
  { token: '<InputFilePath>', desc: '表示输入文件所在文件夹路径' },
  { token: '<InputFileName>', desc: '表示输入文件名，不包含其路径' },
  { token: '<InputFileNameWithOutExtension>', desc: '表示不包含后缀的输入文件名，也不包含其路径' },
]
const OUTPUT_TOKENS: { token: string; desc: string }[] = [
  { token: '<OutputFile>', desc: '表示输出文件完整路径（完全自己写模式专用）' },
  { token: '<OutputFileWithOutExtension>', desc: '表示不包含后缀的输出文件路径' },
]

const fullRef = ref<InstanceType<typeof CodeTextarea> | null>(null)
function insertToken(token: string) {
  fullRef.value?.insert(token)
}
</script>

<template>
  <div class="custom-params">
    <div class="tab-bar">
      <button v-for="(label, i) in TABS" :key="label" class="tab-item" :class="{ active: tab === i }" @click="tab = i">{{ label }}</button>
    </div>

    <!-- ══ 自定义参数说明 ══ -->
    <template v-if="tab === 0">
      <div class="token-doc">
        <div v-for="item in TOKENS" :key="item.token" class="token-row">
          <code class="token-code">{{ item.token }}</code>
          <span class="muted">{{ item.desc }}</span>
        </div>
        <div v-for="item in OUTPUT_TOKENS" :key="item.token" class="token-row">
          <code class="token-code">{{ item.token }}</code>
          <span class="muted">{{ item.desc }}</span>
        </div>
        <div class="section-hint" style="margin-top: 10px">以上字符串都有对应的尖括号转义版本（&lt;…&gt; 写法），可用于滤镜参数。</div>
        <div class="section-title" style="margin-top: 14px">其他</div>
        <div class="section-hint">实际上这些字符串写在其他任何地方都能生效，不过没必要那样做就是了。</div>
      </div>
    </template>

    <!-- ══ 流自定义参数 ══ -->
    <template v-else-if="tab === 1">
      <div class="section-head">
        <span class="section-title">视频流参数</span>
        <span class="section-hint">拼接在已生成部分的末尾，图片参数也是用这个；如果要写滤镜请用滤镜排序功能，否则逻辑会冲突</span>
      </div>
      <CodeTextarea :model-value="valueOf('自定义参数_视频参数')" :rows="5" placeholder="如 -maxrate 5M -bufsize 10M" @update:model-value="setValue('自定义参数_视频参数', $event)" />
      <div class="section-head" style="margin-top: 14px">
        <span class="section-title">音频流参数</span>
        <span class="section-hint">拼接在已生成部分的末尾</span>
      </div>
      <CodeTextarea :model-value="valueOf('自定义参数_音频参数')" :rows="3" placeholder="如 -ac 2 -ar 48000" @update:model-value="setValue('自定义参数_音频参数', $event)" />
      <div class="section-head" style="margin-top: 14px">
        <span class="section-title">自定义滤镜（旧字段）</span>
        <span class="section-hint">旧版字段，保存时会被迁移到「滤镜排序」系统，一般无需填写</span>
      </div>
      <CodeTextarea :model-value="valueOf('自定义参数_视频滤镜')" :rows="2" placeholder="自定义视频滤镜" @update:model-value="setValue('自定义参数_视频滤镜', $event)" />
      <CodeTextarea :model-value="valueOf('自定义参数_音频滤镜')" :rows="2" placeholder="自定义音频滤镜" style="margin-top: 6px" @update:model-value="setValue('自定义参数_音频滤镜', $event)" />
      <div class="section-hint" style="margin-top: 12px">字幕？请使用自带相关功能，或者使用在位置插入功能。</div>
    </template>

    <!-- ══ 在位置插入参数 ══ -->
    <template v-else-if="tab === 2">
      <div class="section-head">
        <span class="section-title">开头参数</span>
        <span class="section-hint">拼接在输入文件之前（ffmpeg 之后，-i 之前）</span>
      </div>
      <CodeTextarea :model-value="valueOf('自定义参数_开头参数')" :rows="2" @update:model-value="setValue('自定义参数_开头参数', $event)" />
      <div class="section-head" style="margin-top: 14px">
        <span class="section-title">之前参数</span>
        <span class="section-hint">拼接在第一个 -i 的文件之后，通常用来导入更多文件</span>
      </div>
      <CodeTextarea :model-value="valueOf('自定义参数_之前参数')" :rows="2" @update:model-value="setValue('自定义参数_之前参数', $event)" />
      <div class="section-head" style="margin-top: 14px">
        <span class="section-title">之后参数</span>
        <span class="section-hint">拼接在输出文件之前，在前面所有参数之后</span>
      </div>
      <CodeTextarea :model-value="valueOf('自定义参数_之后参数')" :rows="2" @update:model-value="setValue('自定义参数_之后参数', $event)" />
      <div class="section-head" style="margin-top: 14px">
        <span class="section-title">最后参数</span>
        <span class="section-hint">拼接在输出文件之后，也就是最末尾的位置</span>
      </div>
      <CodeTextarea :model-value="valueOf('自定义参数_最后参数')" :rows="2" @update:model-value="setValue('自定义参数_最后参数', $event)" />
    </template>

    <!-- ══ 完全自己写 ══ -->
    <template v-else>
      <div class="txt-red" style="font-size: 14px">完全自己写　使用此模式时，其他所有参数全都不生效！</div>
      <div class="section-hint" style="margin: 6px 0">不要包含开头的 ffmpeg，这里是直接给其余的参数；不会自动写引号，注意区分大小写，可以直接用插入功能</div>
      <div class="flex" style="margin-bottom: 8px">
        <button class="param-btn" @click="insertToken('<InputFile>')">插入输入文件</button>
        <button class="param-btn" @click="insertToken('<OutputFile>')">插入输出文件</button>
      </div>
      <CodeTextarea
        ref="fullRef"
        :model-value="valueOf('自定义参数_完全自己写')"
        :rows="10"
        placeholder="-i &lt;InputFile&gt; -c:v libx265 -crf 26 &lt;OutputFile&gt;"
        @update:model-value="setValue('自定义参数_完全自己写', $event)"
      />
    </template>
  </div>
</template>
