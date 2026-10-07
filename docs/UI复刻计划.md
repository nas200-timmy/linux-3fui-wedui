# linux-3fui-2 · 下拉框（LakeUI ModernComboBox）复刻计划

> 落盘副本：`linux-3fui-2/docs/UI复刻计划.md`（构建阶段写入，供上下文压缩后恢复）

## 一、目标

把网页版所有下拉框从浏览器原生 `<select>` 换成自绘组件，对齐原版 LakeUI `ModernComboBox` 的外观、交互与「编码介绍说明」浮窗；并把参数面板的排版节奏（标题 + 同行灰色说明、控件固定宽度）对齐原版。

用户已确认两项口径：**全站统一切换**、**补上原版缺失的逐项说明**。

## 二、实测基准（权威，勿再凭猜测）

来源：原开发机上的原版 UI 截图（`整体ui/` 1–13.PNG） + `参考图片ui/` 逐像素测量（PIL 取样）＋ VB 源码设计器数值。

**触发框**

| 项 | 值 |
|---|---|
| 高度 | 32 |
| 宽度（按用途固定，实测） | 类型 100 · 分类 200 · 具体编码 160 · -preset/-profile:v/-tune 150 · -pix_fmt/预先转换 175 · 音频编码器 250 · 质量控制方式 298/参数名 148/质量值 98 · 元数据·章节·附件 150×3 · -gpu/-threads 100 |
| 底色 | `#373737`（= rgba(220,220,220,0.157) 叠 #181818，实测 55,55,55） |
| 悬停底 | rgba(220,220,220,0.235) ≈ `#3C3C3C` |
| 圆角 | 10（Designer `BorderRadius=10`；实测顶行两侧缩进 8px 吻合） |
| 边框 | 无（`BorderSize=0`） |
| 文字 | Silver `#C0C0C0` 13px，左内边距 10 |
| 占位符 WaterText | 空值时显示 `-preset`/`-profile:v`/`-tune`/`类型`… 色 `#858585`（WaterTextForeColor = 120,255,255,255） |
| 箭头 | 等边三角 **15×13**、Silver `#C0C0C0`，落在右侧边长＝控件高(32px)的方形区内居中，距右 9px；悬停不变色；无分隔线 |
| 控件间距 | 10–11px |

**下拉面板**

- 底 `#242424`；边框 1px Gray `#808080`；内边距 10；项高 **30**；项文字 Silver
- 选中项：底 rgba(220,220,220,0.157) + **文字纯白**
- 悬停项：底 rgba(220,220,220,0.078)，高亮从旧项**滑动**到新项 200ms
- 最多可见 **8** 项（默认）／分类 15（图片 10）／具体编码 20；溢出滚动条宽 10、滑块 `#8C8C8C`、悬停 `#C8C8C8`
- 展开／收起动画 **150ms**，缓动 `1-(1-t)³`
- **Overlay 模式**：面板与控件重合，使当前选中项正好覆盖控件本体并居中滚动；越界夹到视口内

**浮窗说明（tooltip）**

- 底 `#323232`(50,50,50)；文字 Silver；边框 1px `#808080`；内边距 15；纯文本多行
- 贴展开面板右侧跟随悬停项；右侧越界翻到左侧
- 具体编码器：**贴左侧**、最大宽 **500**；其余最大宽 350
- 移出后 180ms 关闭；鼠标移入浮窗不消失

**节标题 / 说明**

- 标题 Silver `#C0C0C0` 13px 与同行说明 `#888888`（实测 133）12px **同一基线**，间距 ~10px
- 上一个控件底 → 下一个节标题顶 ≈ 22–25px；节标题底 → 控件顶 ≈ 16px
- 同排控件底对齐

**彩色说明用 CSS 命名色（实测确认）**：紫 `#9370DB` · 绿 `#9ACD32` · 蓝 `#6495ED` · 红 `#CD5C5C` · 金 `#DAA520` · 强调 `#1E90FF`

## 三、现状差距（当前截图 vs 原版逐条）

1. 宽度：`.field-control > select { width:100%; max-width:260px }` 一律拉伸 → 原版固定 100/150/160/200…
2. 箭头：两个 5px CSS 渐变三角拼成 → 原版单个 15×13 银色实心三角
3. 圆角：`--radius-sm: 4px` → 原版 10
4. 空值：显示「未选择」文本 → 原版空框 + 灰占位符
5. 无浮窗说明：分类/具体编码/音频编码器/参数下拉都没有逐项说明（`dynamicOptions` 里的 `hint` 字段已存在但从未使用）
6. 原生 select 无法定制面板底色、项高、高亮、滚动条、Overlay
7. 排版：「标题一行 + 说明下一行」→ 原版「标题 + 说明同行」
8. 网格：3 列流式 grid 留大片空白（编码器选择下有一整行空）→ 原版紧凑固定宽度流式排布
9. 「图片质量值」恒显示 → 原版仅图片编码器显示
10. 性能选项：label/说明/控件三行堆叠 → 原版「[控件] [ⓘ] 说明」单行
11. 自造中转标题「编码器选择 / 编码器参数」→ 原版无（直接 `视频编码器` / `编码预设` / `配置文件` / `场景优化` / `性能选项`）
12. 具体编码选项 label 为 `libx265（libx265）` 带括号后缀 → 原版只显示 `libx265`

## 四、实施步骤

### S0 计划落盘
把本计划写到 `linux-3fui-2/docs/UI复刻计划.md`（该目录当前为空，新建）。

### S1 后端补浮窗数据（只追加，不改结构）
- `src/3fui-core/视频编码器数据库_v6.vb`：新增 `Public Shared Function 合成编码器提示文本(编码器) As String`，逐字移植原版 `FFmpegFreeUI/…/Form_v6_参数面板_视频编码器.vb:146-216` 的合成逻辑（描述 → 命令 → 可用性 → 视觉体积均衡点 → 无损模式 → 图片质量 → 进阶质量参数 → 其他特殊参数 → 必要/默认附加参数；段落用 `vbCrLf & vbCrLf`）。
- `src/3fui-server/Program.cs:162` `/api/encoder-db`：为每个视频编码器追加 `下拉提示文本` 字段（音频编码器**已**有该字段，直接用）。
- 只加字段，既有字段名与 `videoCategories` 结构不动。

### S2 新增自绘组件 `src/3fui-web/src/components/ModernComboBox.vue`
- Props：`modelValue: string`、`options: {value,label,hint?}[]`、`watermark?`、`width?`、`editable?`、`maxItems?`、`tooltipSide?: 'left'|'right'`、`tooltipMaxWidth?`、`disabled?`；Emits `update:modelValue`
- 面板 `<Teleport to="body">` + `position: fixed`，避开参数面板滚动容器裁剪；按 Overlay 规则定位并夹在视口内
- 交互：点击展开/收起；`↑↓` 移动高亮；`Enter` 选中；`Esc` 关闭；`Home/End`；`Tab` 关闭并交出焦点；点击外部关闭；`resize`／祖先滚动时关闭
- 不可编辑时拦截字符输入（Ctrl+C 仍可用）；可编辑时按输入过滤并允许任意值
- 空 label 或空值时渲染灰色占位（优先 `watermark`）
- 悬停项且该行 `hint` 非空 → 浮窗
- 纯 Vue3 + CSS，**不引入任何第三方库**；用 `<script setup lang="ts">`（避开上次 vue-tsc 卡死的 `defineComponent` 泛型写法）

### S3 FormField 接入 + 排版令牌
- `components/FormField.vue`：`select` 分支换成 `<ModernComboBox>`；`watermark` 取 `field.placeholder ?? field.type.placeholder ?? field.label`
- `schema.ts`：`FieldType.select` 增 `placeholder?`/`width?`/`maxItems?`/`editable?`；`FieldDef` 增 `width?`
- `style.css`：新增 `.sec-head`（标题+同行说明）与节间距；`.field-hint` 改为同行；参数面板改固定宽度流式排布（窄屏仍可换行）；新增 `--radius-control: 10px`
- `views/PresetView.vue` `dynamicOptions()`：
  - 首项空值 label `未选择` → `''`（交由 watermark 显示）
  - 具体编码 label 去掉 `（命令行名）` 后缀
  - 给分类 / 具体编码 / 音频编码器 补 `hint`（音频取 `下拉提示文本`）
  - `-preset`/`-profile:v`/`-tune` 补 `hint`：`值范围说明；默认 X；可选：…`
  - 色彩管理 8 个下拉的逐值说明按 VB `Form_v6_参数面板_色彩管理.vb:7-101` 原文搬到新常量表 `src/3fui-web/src/dropdown-hints.ts`

### S4 编码器页对齐原版
- 去掉自造节标题，按原版层级排布
- 三连下拉宽 100 / 200 / 160，间距 10
- 「图片质量值」仅当所选编码器 `图片质量.参数名` 非空时显示（后端 `预设命令行编码_v6.vb:61` 同样只在有参数名时生效，行为一致）
- 性能选项改「[控件 100px] [ⓘ] 说明」单行；`-gpu` 的 ⓘ 悬停说明取 VB 原文（`此选项仅适用于 CPU 解码 + NV 编码的组合…`）

### S5 全站替换其余原生 select
- `views/AgentView.vue:244/249/253` 底部三个（本地联网 / 系统访问 / 推理级别）
- `views/FilesView.vue:126` 排序框
- `views/PresetView.vue:310` 预设选择框（含内置/用户分组）

### S6 构建 → 部署 → 逐页截图比对
- 构建前打回滚 tag：`docker tag linux-3fui-2:latest linux-3fui-2:prev`
- `docker build -t linux-3fui-2:latest .` → `docker compose up -d --force-recreate`
- 用 `/tmp/l3fui-shot.html` 包装页 + 无头 Chrome 逐页截图，与原版对照：参数总览 / 预设管理 / 输出文件设置 / 解码设置 / 编码器 / 画面帧 / 质量 / 色彩管理 / 音频参数 / 流控制 / Agent / 功能设定
- **必须单独截「下拉框展开态」与「浮窗」**：包装页加 `open=` 参数模拟点击展开后再截

### S7 回归验证
- 接口：`/api/status`、`/api/encoder-db`（含新字段）、`/api/presets`、`/api/preset/preview`、`/api/probe`
- 功能：类型→分类→具体编码→参数列表三级级联正确；选编码器后 `-preset`/`-profile:v`/`-tune` 选项与默认值正确；空值能回到占位
- 预设往返：保存 → 导出 → 载入，值没被改写成 `'未选择'`
- 队列：加一个真实任务跑通，确认参数生成未受影响

## 五、预检查：会不会导致困扰？

| # | 风险 | 对策 |
|---|---|---|
| 1 | **「未选择」语义变更**：9 处依赖该 label | 空值仍是 `''`，与后端 `""` 一致，`.3fui` 兼容性不变；仅显示层改为占位符 |
| 2 | 自绘面板定位：参数面板是可滚动容器，原生 select 的定位由浏览器负责 | 面板挂 body + fixed 定位；展开时与滚动/resize 时关闭；Overlay 越界夹取 |
| 3 | vue-tsc 卡死/类型爆炸（上次踩坑） | 统一 `<script setup lang="ts">` + `defineProps<{}>()`，不用 `defineComponent` 泛型 |
| 4 | Tab 键与可访问性：自绘会丢原生行为 | 显式实现 Tab 交出焦点；补 `role="combobox"` / `aria-expanded` / `aria-selected` 基本属性 |
| 5 | 窄屏：原版 1216×739 固定窗，固定宽控件会溢出 | 容器 `overflow-x: auto` + 允许换行，宽屏优先保真 |
| 6 | 波及原本项目 | `linux-3fui/` 一个字节不动；`3fui-core` 只追加函数与响应字段 |
| 7 | 构建耗时：宿主机无 node，前端在容器内编译 | 构建前打 `:prev` tag，可一键回滚 |
| 8 | 依赖膨胀 | 零新增依赖，纯 Vue3 + CSS |

## 六、验收标准

- 触发框：高 32 / 圆角 10 / 底 `#373737` / Silver 13px / 灰占位符 / 15×13 银色三角右距 9px
- 面板：底 `#242424` / 边框 1px / 项高 30 / 内边距 10 / 选中与悬停底色正确 / 最多 8 项（按用途 10/15/20）/ 与控件 Overlay 重合
- 浮窗：底 `#323232` / Silver / 边框 1px / 内边距 15 / 具体编码贴左且最大宽 500 / 移出 180ms 关闭
- 分类、具体编码、音频编码器、`-preset`/`-profile:v`/`-tune` 悬停均有对应说明文本
- 编码器页与原版截图逐项对齐（宽度 100/200/160/150、标题+同行说明、性能选项单行、图片质量值条件显示）
- 全站无裸露的原生 `<select>`
- 接口与编码队列功能回归通过

## 七、不做的事

- 不模拟 WinForms/DirectX 的亚像素抗锯齿与 ClearType 差异
- 不做像素级 1:1（原版固定 1216×739 窗，网页自适应）
- 不实现 IME 组合态渲染细节
- **参数面板顶部工具行保留**（新建预设/预设管理/导出/保存到服务器/添加到队列）——原版没有，是网页版必需入口

---

# 第 4 轮：操作体验对齐（流程层复刻）

布局已对齐后，用户反馈「使用体验奇怪、没有原版省心」。对照原版 VB 源码逐项核查交互逻辑后，
确认差距在**操作流程**而非像素。本轮改动：

## 流程修复（对应原版行为）

| 项 | 原版行为 | 网页版修复 |
|---|---|---|
| 二级页记忆 | 默认「不要自动重置页面」，切走再回来页面不动 | PresetView 改 v-show 常驻；二级页选择存入全局 store + localStorage |
| 加入编码队列 | 准备文件页一键：当前面板快照 + 全部文件入队 + 清空 + 跳队列页 | FilesView 按钮直接调 addTasks，缺编码器/容器时拦截并跳参数面板 |
| 预设预览/应用 | 单击=仅预览（总览+命令行），双击/「读取」=应用 | 预设管理改三栏：列表 / 参数总览预览 / 命令行模板预览 |
| 自动开始任务 | 默认自动开始，切换设置同步改写未处理任务 | 后端本就默认自动；PUT /api/settings 联动 应用自动开始任务设置 |
| 队列快捷键 | Enter=开始 Delete=移除 空格=暂停/恢复 | 已补（输入框聚焦时不抢键） |
| 双击任务行 | 已完成→定位输出；其他→打开日志 | 已补（定位=复制路径+toast，网页无法开文件管理器） |
| 定位按钮 | 仅单选生效 | 多选时提示「仅支持单选」 |

## 默认值修复

- **`输出_自动命名选项` 默认 `附加_递增时间戳`**（原版枚举默认值）。此前网页版默认空串 →
  后端按「不使用自动命名」→ 输出文件与输入文件同名 → ffmpeg 报错。这是「入队即错误」的根因之一。
- 7 个视频类内置预设补 `音频参数_编码器_代号 = "audio.copy"`（复制流），否则丢音轨；
  图片类（AVIF/JPEG XL/ICO）与 M4A 不动。
- 参数总览行规对齐原版：空值行不显示、默认项不显示、全空显示「未设置参数」、警告红色置顶。
- 命令行模板占位符改回原版 `<输入文件>` / `<输出文件>`。

## 设置页修复

- `自动开始任务选项` / `任务失败自动删除输出文件` / `提示音选项` 是 Integer 枚举，
  此前错用 checkbox（语义颠倒且写 true 进 Integer 字段会 400）。改为下拉框，与原版一致。

## 缓存

- localStorage 键升到 `linux-3fui-preset-v3`（含二级页记忆；旧缓存默认值规则已变，直接作废）。

---

# 第 5 轮：队列可用性 + 公网登录认证

## 队列页修复

- **日志对话框修复（根因）**：后端 `/api/queue/tasks/{id}` 直接序列化任务对象，
  日志缓存是私有字段拿不到 → 前端永远空。改为显式调用 `获取日志快照数据()` 输出；
  预设任务的命令行字段恒为空，接口里用预设数据现场生成。前端对话框打开期间 1.5s 轮询滚动。
- **任务管理菜单可点**：原是纯标签，现为下拉菜单（全选 / 选中错误任务 / 取消选择 / 上移 / 下移），对齐原版菜单项。
- **复制命令行**：navigator.clipboard 在非安全上下文不可用，加 `execCommand` 兜底；
  预设任务列表行命令行为空时先从详情接口取生成版再复制。
- **排队序列**：行首序列徽标（当前=绿 / 下一个=黄 / 后续=#N）；底部实时输出条
  显示选中任务或当前运行任务的 ffmpeg 最新一行；无运行任务时显示「下一个任务」。

## 性能监控页

- btop / Windows 任务管理器风格：CPU 总占用 / 用户 / 系统 / IO 等待四条渐变色条形 +
  60 采样点历史曲线（SVG polyline）；内存与 Swap 条形 + 曲线；GPU 利用率 / 显存 / 温度条形；
  条形 >70% 黄、>90% 红。

## 登录认证（可选，默认关闭）

- 设置页「登录认证」面板：启用/停用/改密码/退出登录。启用后整页只剩登录框。
- 后端 `AuthService.cs`：配置存 `数据目录/Auth.json`，密码 PBKDF2-SHA256(100k) + 随机盐；
  会话为内存 token（HttpOnly + SameSite=Strict Cookie，7 天，HTTPS 下 Secure）。
- 防爆破：全局 3 分钟窗口内最多 15 次密码错误，窗口到期自动重置；锁定时返回 429 与剩余秒数。
- 中间件只拦 `/api/*` 与 `/ws`（`/api/auth/*` 除外）；静态页面照返，SPA 显示登录页。
- 镜像补 `libmfx-gen1.2`（oneVPL GPU 运行时；实测该机 QSV 仍不可用，VAAPI 正常）。

---

# 第 6 轮：QSV 排查结论 + VAAPI 硬件编码接入

## QSV 诊断（实测，非容器问题）

本机 Intel UHD 730（Alder Lake-S GT1），HuC/GuC 固件正常认证。ffmpeg 7.1.5 + libvpl2 + libmfx-gen1.2：

| 组合 | 结果 |
|---|---|
| hevc_qsv 任意参数（含 low_power=1 + 比特率/CQP） | ❌ 帧提交报 Invalid data，平台级故障（宿主机 ffmpeg 同报错） |
| h264_qsv 无 low_power | ❌ |
| h264_qsv low_power=1 + 比特率 / -q:v CQP | ✅ 可用 |
| h264_qsv low_power=1 + -global_quality（ICQ） | ❌ |
| hevc_vaapi / h264_vaapi（-vaapi_device + format=nv12,hwupload） | ✅ 完全可用 |

结论：QSV 的 HEVC 在这台机器上是驱动/runtime 层故障，无法从应用侧修复；
VAAPI 是唯一稳定可用的 Intel 核显硬编路径（飞牛影视服务走的就是 VAAPI）。

## 修复

- 编码器数据库新增 `hevc_vaapi` / `h264_vaapi`（排分类首位，标注"本机实测可用"），
  含 -qp 质量参数、-compression_level 预设、main/main10 配置、nv12/p010le 像素格式。
- 命令生成适配（linux-3fui 专用，不影响上游逻辑）：
  - `是VAAPI编码器()` 判定 `*_vaapi`；
  - `生成解码参数`：VAAPI 自动注入 `-vaapi_device /dev/dri/renderD128`（可用"指定硬件参数名/参数"覆盖）；
  - 滤镜图：VAAPI 自动在视频链末尾追加 `format=nv12,hwupload`（10 位用 p010le），用户无滤镜也生效。
- 镜像已含 libva2/libva-drm2/intel-media-va-driver/libmfx-gen1.2。

---

# 第 7 轮：「全保留」默认策略 + 小白默认流程

## 默认预设（newPreset）= 只换编码器，其余全保留

面向「极客中的小白」：用户只选 编码器 + 质量 + 容器，其他默认跟随源视频：

- **音频**：编码器默认 `audio.copy`（复制流），且 `流控制_启用保留其他音频流 = true`
  → `-map 0:a? -c:a copy`，**全部音轨**保留，不只是第一条。
- **字幕**：`流控制_启用保留其他字幕流 = true` → `-map 0:s? -c:s copy`，全部字幕保留。
- **元数据/章节/附件**：默认保留 → `-map_metadata 0 -map_chapters 0`（mkv 再加 `-map 0:t? -c:t copy`），刮削不受影响。
- 分辨率/帧率/色彩/滤镜/响度：默认全空 = 跟随源视频，用户主动调才生效。

实测默认命令行：
`ffmpeg -hide_banner -y -i 输入 -map 0:v:0? -map 0:a? -c:a copy -map 0:s? -c:s copy -c:v:0 libx265 -crf:v:0 26 -c:a:0 copy -map_metadata 0 -map_chapters 0 -map 0:t? -c:t copy 输出`

## 配套改动

- **核心滤镜图门控**：图片类型编码（AVIF/JXL 等）强制跳过音频/字幕输出
  （否则「全保留」默认会让 avif 容器报 Invalid argument）。
- **内置预设**：7 个视频类补「全保留」字段（audio.copy + 保留其他音轨/字幕/元数据/章节/附件）；
  M4A（音频提取）与 AVIF/JXL（图片）显式关闭；ICO 走自定义命令行不受影响。
- **缓存键**升到 `linux-3fui-preset-v4`（旧缓存的空白流控制字段会挡住新默认值）。
- **参数总览**新增「保留：全部音轨 · 全部字幕 · 元数据 · 章节 · 附件」行（按实际开启项显示）。
- **起始页面**改写为 4 步默认流程（准备文件 → 选编码器 → 定质量一次 → 选容器入队），
  并说明「默认就安全」的保留策略。
- docker-compose 健康检查改打 `/api/auth/status`（启用登录认证后 `/api/status` 返回 401 会误报 unhealthy）。

---

# 第 8 轮：编码器切换残留参数治理（vaapi/qsv 失败根因）

## 根因

用户切到 hevc_vaapi 后任务失败：`Error setting option compression_level to value slower`——
「编码预设」字段残留了 libx265 的 slower，而 VAAPI 的 -compression_level 只接受 0~7。
QSV 侧则是 h264_qsv 缺 -low_power（Linux 必需），hevc_qsv 为已知平台级故障。

## 修复

- **核心值列表校验**（预设命令行编码_v6.vb 添加编码器参数受控）：编码预设/配置文件/场景优化/像素格式
  只在值属于当前编码器的值列表时才输出；列表为空=自由文本原样放行。
  x265 的 slower 切到 VAAPI 会被剔除，留在 x265 则正常输出。
- **QSV 自动 low_power**：`*_qsv` 编码器未显式设置时自动补 `-low_power:v:0 1`
  （Linux oneVPL 只有 VDEnc 路径），h264_qsv 开箱可用；hevc_qsv 平台级故障照旧，用 hevc_vaapi。
- **前端兜底**：PresetView watch 具体编码变化，清掉对新编码器不再合法的
  编码预设/配置文件/场景优化/像素格式。
- docker-compose 健康检查改打 /api/auth/status（第 7 轮已做）。

## 验证

- hevc_vaapi + 残留 slower/p010le → compression_level 被剔除，命令干净，真实入队正常编码；
- h264_qsv → 自动带 -low_power:v:0 1；
- libx265 + slower → 正常保留 -preset:v:0 slower。

---

# 第 9 轮：WebSocket 状态指示灯

- store.ts 新增 `useWsStatus`（connected 状态）；`openQueueWebSocket` 加可选 onStatus 回调（onopen/onclose 触发）。
- App.vue 全局 WS（编码器切换告知那条常驻连接）接状态回调；标题栏「Linux 网页版」左侧显示
  状态灯：绿点=已连接（队列实时推送正常），灰点=已断开（3 秒自动重连），悬停有说明。
- QueueView 的页面级 WS 不动（避免离开队列页误报断开）。
- 构建前打了回滚备份 `linux-3fui-2:prev-ui`。

---

# 第 10 轮：入队前容器兼容确认（防呆最后一道）

「全保留」默认策略遇到 MP4/WebM/AVI/FLV/TS 等受限容器时 ffmpeg 会硬报错（附件流、字幕格式、
章节等容器不支持）。在入队前主动检查并弹窗，把选择权交给用户：

- **store.ts**：容器限制表（mp4/m4v/mov、webm、avi、flv、ts、m2ts 各自的不可保留项）+
  `检查容器兼容性()` + `入队兼容确认()`（有冲突弹 Promise 化弹窗，'mkv'=改容器继续，
  'drop'=关掉不兼容的保留开关继续，'cancel'=中止）。
- **ContainerCompatDialog.vue**：列出装不下的项与原因，三个按钮（改用 MKV 继续（推荐）/
  仍用当前容器丢弃上述内容/取消）。
- 两个入队入口（PresetView.addFilesToQueue / FilesView.enqueueAll）在编码器与容器
  非空校验之后、addTasks 之前调用；mkv 或「按输入」容器不弹，零打扰。

---

# 第 11 轮：性能监控页改造（Windows 任务管理器式卡片 + 服务端历史）

动机：原性能页是 btop 式大条形面板，看不出「当前 vs 历史」，历史仅存前端内存 60 点（约 2 分钟），
关页即失。参考 LiteMonitor 与 Windows 任务管理器：总览卡片 → 点击卡片看该硬件当前性能 + 历史曲线。

## 后端（PerfMonitor.cs 重写 + Program.cs）

- **后台采样器**：`PeriodicTimer` 2s 采样（随 ApplicationStarted/Stopping 启停），
  内存环形缓冲 1800 样本 = **1 小时**，`Monitor.TryEnter` 防重叠（nvidia-smi 超时不阻塞）。
- **新增采集**：磁盘（/proc/diskstats 计数器差分，整盘= /sys/block 过滤，排除 loop/ram）、
  网络（/proc/net/dev 差分，排除 lo）、CPU 每核（/proc/stat per-core 差分）。
- **新端点**：`GET /api/perf/history?minutes=&maxPoints=&cores=`（步长抽稀保留最新点，cores=1 附每核序列）。
- `GET /api/perf` 形状兼容（cpu/memory/loadavg/processes/gpu 键名不变），新增 disk/network/perCore/
  buffersCacheMb；**GPU 字段字符串→数字**（App.vue 标题栏 / SettingsView 插值无感，已复核）。
- 采样语义变化：CPU 各百分比由「开机累计占比」改为「2s 区间差分占比」（任务管理器口径）。

## 前端（PerfView.vue 重写 + 新增 PerfChart.vue）

- **总览态**：自适应卡片网格（CPU/内存/GPU×N/磁盘/网络），卡片=大数字+细进度条+SVG 迷你曲线+副信息，
  点击进详情。
- **详情态**：返回按钮 + 硬件名 + `PerfChart`（canvas，DPR 感知，网格/Y 刻度/X 时间轴/图例/双序列面积图）
  + 窗口切换 10/30/60 分钟（重拉 history）+ 硬件附加区：
  CPU=逻辑处理器每核小图网格（SVG polyline，hist.cores）+用户/系统/IO 构成；
  内存=已用/缓冲缓存/可用构成条+Swap；GPU=每卡大数字；磁盘/网络=每设备/接口速率表。
- 数据流：进页拉 history 播种 → 2s 轮询 /api/perf 追加实时点（不引 WS，QueueRealtime 不动）。
- 底部保留负载 1/5/15 + ffmpeg/ffprobe 进程表（总览与详情均显示）。

## 已知限制（容器视角）

- 历史为内存环形缓冲，**容器重启即清空**（不做持久化/日报统计）。
- /proc/net/dev 是网络命名空间视角 → 只见容器网卡（通常 eth0）。
- GPU 仅 NVIDIA（nvidia-smi）；探测失败后不再调用。
- /proc/diskstats、/proc/stat、/proc/meminfo 非命名空间隔离 → 反映宿主，符合 NAS 监控预期。

## 第 11 轮补丁（9-30）

- 修复每核显示长浮点（2.03020202020…%）：`perCore` 差分结果漏了 R1（总占用/user/system 都有），
  前端每核当前值又按原样拼串 → 服务端每核补 R1（`PerfMonitor.cs` ReadCpu），前端 `coreText()` 再兜一层 toFixed(1)。

---

# 第 12 轮：图形化组件 + 附加内容 + 流控制 + 自定义参数 + Agent（全量）

依据 `../3fui图片/` 14 张原版截图（流控制/元数据×6/自定义参数×4/色彩管理/音频参数/Agent）逐页对照。
**核心结论先行：8 项功能在 3fui-core 全部已移植且与上游逐字节一致，本轮纯前端加 UI（零后端改动、零 newPreset 默认值变化）。**

## 防呆三不变量（本轮全程遵守，已逐条核实）

1. 不改 `newPreset()` 默认值 → localStorage v4 缓存键不升版，「全保留」默认逐字节保持；
   （滑杆替换旧 bool+text 字段后，`newPreset()` 的补默认值遍历同步覆盖 `section.sliders`，
   输出键集合与改动前完全一致。）
2. 不改 3fui-core 一行 → 112 项回归测试不涉及；.3fui 双向兼容天然成立。
3. 不改任何预设字段存储路径 → 第 10 轮容器兼容弹窗、参数总览、命令行生成全部无感。

## WP1 图形化滑杆卡片（SliderCard.vue）

- 简易调色 4 卡（亮度 -1..1 / 对比度 0..2 / 饱和度 0..3 / 伽马 0.1~10，范围照抄上游 Designer.vb）；
  响度标准化 3 卡（目标响度 -36..-8 带 6 行建议刻度 / 动态范围 1..40 / 峰值电平 -5..0）。
- 安全语义（已读核心源码核实）：核心对 eq/loudnorm 双保险门控（排序条目看启用 bool
  `预设滤镜排序_v6.vb:88-89`；滤镜构造看 `启用 AndAlso 值<>""` `预设命令行滤镜_v6.vb:434-437,443-445`）
  → **滑杆不自动勾选启用、仅拖拽写值、双击复位写回核心同款默认串**（"0"/"1"/"-24"，
  与滤镜排序页移除条目时核心的重置值 `预设滤镜排序_v6.vb:362-377` 一致），默认预设命令行不可能出现 eq/loudnorm。

## WP2 附加内容三标签页（ExtrasPanel.vue，kind: 'extras'）

- 元数据 tab：「添加预设项」ModernComboBox（12 项，存映射后英文字段名 title/artist/…，hint 浮窗说明）+
  字段(editable 下拉)|值 表格 + 删除所选/全部清空/导出/导入（JSON，行形状校验）。
- 章节 tab：来源下拉 + 文件路径 + 选择文件（DirPickerDialog 新增 files 文件模式）+ 教程（ffmetadata 格式示例弹窗）。
- 附件 tab：「添加附件」下拉（ATTACH_TYPE 5 类型）+ 类型|文件路径 表格（行内浏览按钮）+ 同套按钮。
- 预设备注移入第 4 个 tab（预设管理页那份保留不动）。
- `元数据_要写入的信息`/`附件_要写入的附件` 本就由 newPreset 初始化、后端消费——之前导入的 Windows 预设能生效但无法编辑，本轮补齐 UI。

## WP3 流控制复刻 + 可视化流选择器（StreamPickerDialog.vue）

- schema 重排：三色关键词 label（FieldDef 新增 highlight/labelTone，FormField 拆分着色）、
  三个 stringlist 补格式 hint、混流同名字幕节 mp4 提示、页底 -map 彩色说明段
  （SectionDef.hints 扩为 string|{text,tone}）、「然后保留其他xx流」对齐原版文案。
- 可视化流选择器：待处理文件（或手动添加）→ /api/probe 解析 → 按 视频/音频/字幕 勾选 →
  写回 `{文件索引}:{v|a|s}:{类型内序号}`（与上游媒体流选择器写回、核心 规范流列表 解析一致），
  打开时回显已选（支持 0:v 整类简写展开）。

## WP4 自定义参数四标签页（CustomParamsPanel.vue + CodeTextarea.vue，kind: 'custom'）

- 自定义参数说明（占位符文档，token 代码片红字）/ 流自定义参数（视频/音频 + 旧滤镜字段）/
  在位置插入参数（开头/之前/之后/最后）/ 完全自己写（红色警告 + 光标处插入 <InputFile>/<OutputFile>）。
- CodeTextarea：行号列滚动同步 + defineExpose insert()。
- 修复参数总览 bug：原读 `自定义参数_视频附加参数`（不存在）→ 改读 `自定义参数_视频参数`，总览两行恢复显示。

## WP5 Agent 补齐（AgentView.vue，纯前端）

- token 计数原版格式 `0% | 0 / 200000`；模型选择改可编辑下拉 + localStorage 历史（linux-3fui-agent-models）；
  对话列表悬停按项删除；「向 AI 发送文件(类)」：文本类读内容（≤512KB）、非文本附文件名、
  待处理文件附路径+probe 摘要，作为【附件】块注入下一条 user 消息（服务端零改动）。

## 文件清单

新增 `components/SliderCard.vue / ExtrasPanel.vue / StreamPickerDialog.vue / CustomParamsPanel.vue / CodeTextarea.vue`；
修改 `schema.ts`（SliderDef/highlight/labelTone/hints 彩色/GroupKind 两新 kind/PAGES 三节重排/newPreset 遍历覆盖 sliders）、
`PresetView.vue`（sliders 渲染、extras/custom 分支、streamPicker 特例、总览 bug）、`FormField.vue`（label 高亮）、
`DirPickerDialog.vue`（files 文件模式）、`AgentView.vue`、`style.css`。

## 部署后验收

1. 默认预设命令行与第 7 轮基线一致（无 eq=/loudnorm=/-metadata 字段=/-attach）；MP4+默认全保留仍弹容器兼容窗。
2. 色彩管理/音频参数拖滑杆 → 勾选启用 → 命令行出现 eq=/loudnorm=；不勾选永不出现。
3. 附加内容添加/删除/导出/导入；旧 .3fui 的元数据表格可见可编辑；命令行 -metadata/-attach 正确。
4. 流控制：可视化流选择器勾选写回 0:v:0 格式，命令行 -map 正确。
5. 自定义参数 4 标签 + 行号同步 + 总览两行显示；Agent token 百分比/模型下拉/按项删/附件发送。

## 部署后验收结果

（待用户部署后回填）
