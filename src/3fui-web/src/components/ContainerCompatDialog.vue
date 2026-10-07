<script setup lang="ts">
import { useCompatDialog, useToast } from '../store'

const dialog = useCompatDialog()
const toast = useToast()

function chooseMkv() {
  dialog.choose('mkv')
  toast.push('ok', '已改用 MKV 容器（全部保留）')
}
</script>

<template>
  <div v-if="dialog.visible" class="modal-mask" @click.self="dialog.choose('cancel')">
    <div class="modal" style="width: min(560px, 92vw)">
      <div class="modal-header">
        <span>输出容器 · 兼容确认（{{ dialog.容器 }}）</span>
        <button class="small" @click="dialog.choose('cancel')">取消</button>
      </div>
      <div class="modal-body">
        <div style="margin-bottom: 10px">
          当前「全保留」策略里有 {{ dialog.冲突.length }} 项是这个容器装不下的，直接编码 ffmpeg 会报错：
        </div>
        <div v-for="item in dialog.冲突" :key="item.项目" style="margin-bottom: 8px; line-height: 1.6">
          <b class="txt-gold">{{ item.项目 }}</b>
          <span class="muted"> — {{ item.说明 }}</span>
        </div>
        <div class="muted" style="margin-top: 10px; font-size: 12px">
          MKV 什么都能装（全音轨 / 字幕 / 元数据 / 章节 / 附件），NAS 播放无压力。
        </div>
        <div class="flex" style="margin-top: 14px">
          <button class="primary" style="flex: 1" @click="chooseMkv">改用 MKV 继续（推荐，全部保留）</button>
        </div>
        <div class="flex" style="margin-top: 8px">
          <button class="small" style="flex: 1" @click="dialog.choose('drop')">
            仍用 {{ dialog.容器 }}，丢弃上述内容
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
