// 预设编辑器 schema：全覆盖 预设数据_v6 字段（与 Windows 版 .3fui 双向兼容）。
// 页面结构严格对齐原版 Form_v6_参数面板.Designer.vb 的二级菜单（15 个子页 / 6 组带分隔线）。
import type { PresetData } from './api'

export interface FieldOption {
  value: string
  label: string
  /** 下拉项悬停时浮窗显示的说明（原版 ModernComboBox.ItemToolTips） */
  hint?: string
}

export type FieldType =
  | { kind: 'text'; placeholder?: string }
  | { kind: 'textarea'; rows?: number; placeholder?: string }
  | {
      kind: 'select'
      options: FieldOption[]
      /** 空值时显示的灰色占位符（原版 WaterText），默认取 label */
      placeholder?: string
      /** 控件固定宽度（原版按用途固定：100/150/160/175/200/250/300） */
      width?: number | string
      /** 可编辑下拉（原版 Editable = True，允许直接输入） */
      editable?: boolean
      /** 展开后最多可见项数（原版默认 8，分类 15、具体编码 20） */
      maxItems?: number
      /** 浮窗贴在面板哪一侧（原版具体编码为 Left） */
      tooltipSide?: 'left' | 'right'
      /** 浮窗最大宽度（原版默认 350，具体编码 500） */
      tooltipMaxWidth?: number
    }
  | { kind: 'bool' }
  | { kind: 'color' }
  | { kind: 'stringlist'; hint?: string }
  | { kind: 'number' }

/** 语义色（对应 style.css 的 --gold/--green/--blue/--purple/--red） */
export type Tone = 'gold' | 'green' | 'blue' | 'purple' | 'red'

export interface FieldDef {
  path: string
  label: string
  /** label 中高亮的子串（原版彩色关键词，如流控制页的「视频参数」），配合 labelTone 着色 */
  highlight?: string
  /** label 高亮子串的语义色 */
  labelTone?: Tone
  type: FieldType
  full?: boolean // 占满整行
  /** 与标题同行的灰色说明（原版如此）；可为函数，随预设内容变化（原版二级窗口里随滤镜选择变化的参数名） */
  hint?: string | ((preset: Record<string, unknown>) => string)
  /** 灰色说明的语义色（原版 HCL_ 里带颜色的 span，如 Goldenrod / OliveDrab / MediumPurple） */
  hintTone?: Tone
  info?: string  // ⓘ 悬停说明（原版的说明图标）
  inline?: boolean // 控件在左、字段名与灰色说明在右，且独占整行（原版性能选项 / 设置页）
  /** 与 inline 相同，但不独占整行（原版色彩管理：控件 + 右侧字段名并排） */
  sideLabel?: boolean
  bare?: boolean // 不显示自身标题，仅控件（原版级联三连）
  /** 控件旁的按钮（原版「按钮 + 控件」同一行，如 画面裁剪交互 + crop） */
  button?: { text: string; dialog: string; after?: boolean }
  /** 路径字段旁的「浏览…」按钮（原版 MCB_输出位置 下拉里的「浏览 ...」） */
  browse?: 'dir'
  /** 条件显示（需 PresetView 侧配合数据库判断），如原版「仅图片编码器」的图片质量值 */
  showKey?: 'imageQuality'
  width?: number | string
  placeholder?: string
}

/** 竖向滑杆刻度标注（原版响度标准化左侧的建议刻度行） */
export interface SliderMark {
  value: number
  label: string
  tone?: Tone
}

/** 竖向滑杆卡片定义（原版简易调色/响度标准化的图形化组件） */
export interface SliderDef {
  enablePath: string // bool 启用开关（门控：核心只有启用=true 才生成 eq/loudnorm）
  valuePath: string  // string 值（不变文化；空=未设置，显示默认位）
  label: string      // 卡片名（亮度/目标响度…）
  unit?: string      // 单位（LUFS/LU/dBTP）
  min: number
  max: number
  step: number
  /** 核心同款默认串：双击复位写回它（与滤镜排序页移除条目时核心的重置值一致） */
  def: string
  decimals: number
  topLabel?: string
  bottomLabel?: string
  marks?: SliderMark[]
  tone: Tone
}

export interface SectionDef {
  title?: string // 页内小节标题（空则不再分层）
  hint?: string
  /** 追加的多行灰色说明；对象形式可带语义色（流控制页底 -map 彩色说明段） */
  hints?: (string | { text: string; tone?: Tone })[]
  columns?: number // 该小节字段网格的列数（默认 3，如比特率四连用 4）
  /** 竖向滑杆卡片组；存在时本节不渲染普通 fields（值仍写同一批预设字段） */
  sliders?: SliderDef[]
  /** 小节内的快捷按钮（如原版「插入预制条目」），点击后把 preset 文本追加到 targetPath */
  action?: { text: string; targetPath: string; insert: string }
  /** 小节内的一排按钮（原版二级窗口的入口），点击打开 DialogDef.id 对应的对话框 */
  buttons?: { text: string; dialog: string }[]
  fields: FieldDef[]
}

export interface DialogDef {
  id: string
  title: string // 对话框标题（照抄原版二级窗体的 Text）
  /** 顶部的「XX总开关」勾选框（原版 ModernCheckBox；纯 UI，只控制本窗口字段显隐，不写进预设） */
  toggle?: { text: string }
  hint?: string // 标题下方的灰色说明
  fields: FieldDef[] // 字段，默认纵向 inline 排布
}

export type GroupKind = 'fields' | 'overview' | 'presets' | 'filterorder' | 'extras' | 'custom'

export interface GroupDef {
  id: string
  title: string // 二级菜单显示名
  kind: GroupKind
  hint?: string
  /** 页顶多行彩色说明（如"CPU 编码首选 CRF 对应 -crf"） */
  hints?: { text: string; tone?: Tone }[]
  /** 单行彩色图例（原版 HCL_ 控件，如 lib = CPU，nvenc = NVIDIA …） */
  legend?: { text: string; tone?: Tone }[]
  sections: SectionDef[]
  /** 本页的二级窗口（原版 Form_v6_参数面板_XXX） */
  dialogs?: DialogDef[]
}

const opt = (values: Record<string, string>): { value: string; label: string }[] =>
  Object.entries(values).map(([value, label]) => ({ value, label }))

export const CONTAINERS = ['', 'mp4', 'mkv', 'mov', 'webm', 'avi', 'flv', 'ts', 'm2ts', 'm4v', '3gp', 'ogv', 'mxf', 'mp3', 'm4a', 'aac', 'flac', 'wav', 'ogg', 'opus']

export const VIDEO_QUALITY_MODES = opt({
  '': '',
  CRF: '恒定质量 CRF - CPU 编码首选',
  VBR: '动态码率 VBR - GPU 编码首选',
  CQP: '恒定量化 CQP - 不常用 / 不推荐',
  CBR: '恒定速率 CBR - 仅旧场景',
  TPE: '二次编码 TPE - 使用基础码率',
})

// 以下各组的「值」是预设里保存的枚举名（不可改，改了预设不兼容），「显示文本」照抄原版 Items.Add
export const DENOISE = opt({
  '': '',
  hqdn3d: 'hqdn3d - 时空域降噪，适合普通噪声',
  nlmeans: 'nlmeans - 高级降噪，效果更好速度更慢',
  atadenoise: 'atadenoise - 轻量级时间域降噪',
  bm3d: 'bm3d - 高质量降噪，适合严重噪声',
  bilateral_cuda: 'bilateral_cuda - CUDA 加速双边滤波（需要专门编译的 ffmpeg）',
})
export const SHARPEN = opt({ '': '', cas: 'cas - 自适应对比度锐化', unsharp: 'unsharp - 传统反遮罩锐化' })
export const GRAIN = opt({
  '': '',
  noise_全平面动态均匀颗粒: 'noise - 全平面动态均匀颗粒',
  noise_亮度为主动态颗粒: 'noise - 亮度为主动态颗粒',
  noise_柔和平均颗粒: 'noise - 柔和平均颗粒',
  libplacebo_应用片源胶片颗粒元数据: 'libplacebo - 应用片源胶片颗粒元数据',
})
export const BANDING = opt({
  '': '',
  deband_标准去色带: 'deband - 标准去色带',
  deband_强力去色带: 'deband - 强力去色带',
  gradfun_快速渐变平滑: 'gradfun - 快速渐变平滑',
  libplacebo_GPU去色带加颗粒: 'libplacebo - GPU 去色带加颗粒',
})
export const SCAN = opt({
  '': '',
  yadif_单帧输入_自动场序_空间检查: '隔行转逐行 - yadif 单帧输入+自动场序+空间检查',
  yadif_单帧输入_顶场优先_空间检查: '隔行转逐行 - yadif 单帧输入+顶场优先+空间检查',
  yadif_单帧输入_底场优先_空间检查: '隔行转逐行 - yadif 单帧输入+底场优先+空间检查',
  tinterlace_顶场优先: '逐行转隔行 - tinterlace 顶场优先',
  tinterlace_底场优先: '逐行转隔行 - tinterlace 底场优先',
  NTSC_标准IVTC_胶片32Pulldown转逐行: 'NTSC 标准 IVTC 胶片 3:2 pulldown 转逐行',
  NTSC_纯隔行_非胶片转逐行: 'NTSC 纯隔行 非胶片 转逐行',
  NTSC_自动检测Pulldown至25fps: 'NTSC 自动检测 pulldown 模式至 25fps',
  PAL_标准反交错: 'PAL 标准反交错',
  PAL_标准反交错_双倍帧率: 'PAL 标准反交错 双倍帧率',
  PAL_高质量反交错: 'PAL 高质量反交错',
  PAL_高质量反交错_双倍帧率: 'PAL 高质量反交错 双倍帧率',
  yadif_cuda_自动场序: 'CUDA 隔行转逐行 - yadif_cuda 自动场序',
  bwdif_cuda_自动场序: 'CUDA 高质量反交错 - bwdif_cuda 自动场序',
})
export const ROTATE = opt({ '': '', 顺时针旋转90度: '顺时针旋转 90°', 顺时针旋转180度: '顺时针旋转 180°', 顺时针旋转270度: '顺时针旋转 270°', 逆时针旋转90度: '逆时针旋转 90°', 逆时针旋转180度: '逆时针旋转 180°', 逆时针旋转270度: '逆时针旋转 270°' })
export const MIRROR = opt({ '': '', 水平镜像: '水平镜像', 垂直镜像: '垂直镜像' })
export const CUT = opt({
  '': '',
  未知: '未知',
  粗剪: '粗剪（立即响应）',
  精剪从头解码: '精剪（从头解码）',
  精剪空降解码: '精剪（快速响应）',
  Trim滤镜: 'Trim 滤镜',
  掐头去尾: '掐头去尾',
  剔除中间: '剔除中间',
})
export const SUBTITLE_FILTER = opt({ '': '', subtitles: 'subtitles', ass: 'ass' })
// 值保持原样（核心侧按枚举名匹配），显示文本照抄原版 Items.Add
export const SUBTITLE_SOURCE = opt({ '': '', 外部字幕文件: '外部文件', 内嵌的流: '内嵌的流' })
export const SUBTITLE_BORDER = opt({ '': '', 边框_阴影: '边框+阴影', 背景框: '背景框' })
export const SUBTITLE_ALIGN = opt({ '': '', 左下角: '左下角', 底部居中: '底部居中', 右下角: '右下角', 左居中: '左居中', 正中: '正中', 右居中: '右居中', 左上角: '左上角', 顶部居中: '顶部居中', 右上角: '右上角' })
export const SUBTITLE_FORMATS = ['SRT', 'ASS', 'SSA']
export const SUBTITLE_OP = opt({ '': '', 复制流: '复制流', 转为_mov_text: '转为 mov_text', 转为_srt: '转为 srt', 转为_ass: '转为 ass', 转为_ssa: '转为 ssa' })
export const META_OP = opt({ '': '', 保留元数据: '保留元数据', 清除元数据: '清除元数据', 保留更多元数据: '保留更多元数据' })
export const CHAPTER_OP = opt({ '': '', 保留章节: '保留章节', 清除章节: '清除章节' })
export const ATTACH_OP = opt({ '': '', 保留附件: '保留附件', 清除附件: '清除附件' })
export const CHAPTER_SOURCE = opt({ '': '', 文本文档: '文本文档', 媒体文件: '媒体文件' })
export const ATTACH_TYPE = opt({ '': '', 图片: '图片', MP4封面图: 'MP4 封面图', MKV封面图: 'MKV 封面图', 字体文件: '字体文件', 文本文档: '文本文档' })

// 元数据预制项：值 = 预设里存储的英文字段名（核心直接生成 -metadata 字段=值），label = 原版下拉显示名
export const META_PRESETS: FieldOption[] = [
  { value: 'title', label: '标题', hint: '写入 -metadata title=…' },
  { value: 'artist', label: '参与创作的艺术家', hint: '写入 -metadata artist=…' },
  { value: 'album', label: '专辑', hint: '写入 -metadata album=…' },
  { value: 'genre', label: '流派', hint: '写入 -metadata genre=…' },
  { value: 'track', label: '曲目编号', hint: '写入 -metadata track=…' },
  { value: 'disc', label: '碟片编号', hint: '写入 -metadata disc=…' },
  { value: 'date', label: '日期', hint: '写入 -metadata date=…' },
  { value: 'copyright', label: '版权', hint: '写入 -metadata copyright=…' },
  { value: 'comment', label: '备注', hint: '写入 -metadata comment=…' },
  { value: 'description', label: '描述', hint: '写入 -metadata description=…' },
  { value: 'encoder', label: '编码器', hint: '写入 -metadata encoder=…' },
  { value: 'software', label: '软件', hint: '写入 -metadata software=…' },
]
export const AUTO_NAME = opt({
  '': '',
  不使用自动命名: '不使用自动命名',
  附加_递增时间戳: '附加递增时间戳',
  附加_递增数字: '附加递增数字',
  附加_3FUI: '附加 _3FUI',
  常规压片_附加编码器和质量参数: '常规压片：附加编码器和质量参数',
  附加_随机8位数字: '附加随机 8 位数字',
  附加_随机8位字母: '附加随机 8 位字母',
  附加_随机8位数字和字母组合: '附加随机 8 位数字字母',
  附加_随机16位数字: '附加随机 16 位数字',
  附加_随机16位字母: '附加随机 16 位字母',
  附加_随机16位数字和字母组合: '附加随机 16 位数字字母',
  附加_2位结尾序号: '附加 2 位结尾序号',
  附加_3位结尾序号: '附加 3 位结尾序号',
})

// ── 画面帧页 / 二级窗口的下拉项（全部照抄原版 Designer 的 Items.Add）──
export const FRAME_RESOLUTION = opt({
  '': '', '1024x576': '1024x576', '1280x720': '1280x720', '1600x900': '1600x900',
  '1920x1080': '1920x1080', '2560x1440': '2560x1440', '3840x2160': '3840x2160',
})
/** 值直接参与命令生成：scale_cuda 才是 NVIDIA 滤镜，其余都走 scale */
export const FRAME_SCALE_FILTER = opt({ '': '', scale: '默认 CPU scale', scale_cuda: 'NVIDIA 专用 scale_cuda' })
export const FRAME_WIDTH = opt({ '': '', iw: 'iw', 'iw/2': 'iw/2', 'iw*2': 'iw*2' })
export const FRAME_HEIGHT = opt({ '': '', ih: 'ih', 'ih/2': 'ih/2', 'ih*2': 'ih*2' })
export const FRAME_SCALE_ALGO = opt({
  '': '', lanczos: 'lanczos', bilinear: 'bilinear', fast_bilinear: 'fast_bilinear', bicubic: 'bicubic',
  neighbor: 'neighbor', area: 'area', bicublin: 'bicublin', gauss: 'gauss', sinc: 'sinc', spline: 'spline',
})
// 帧率必须写成数组字面量：opt() 用对象字面量，而 JS 规定 '15' / '24' / '120' 这类数字键
// 会按数值升序排到最前，空串就再也不是首项，界面首选项与默认值都会被带偏。
export const FRAME_RATE: FieldOption[] = [
  { value: '', label: '' },
  { value: '15', label: '15' },
  { value: '23.97', label: '23.97' },
  { value: '24', label: '24' },
  { value: '25', label: '25' },
  { value: '30', label: '30' },
  { value: '50', label: '50' },
  { value: '59.94', label: '59.94' },
  { value: '60', label: '60' },
  { value: '90', label: '90' },
  { value: '120', label: '120' },
]
/** 值保持原样即可：核心侧的 标准化帧率模式 同时认 cfr/vfr 与下面这两条文本 */
export const FRAME_RATE_MODE = opt({ '': '', '固定帧率 CFR': '固定帧率 CFR', '动态帧率 VFR': '动态帧率 VFR' })

export const INTERP_MODE = opt({ '': '', 两帧加权平均: '两帧加权平均', 运动补偿插值: '运动补偿插值' })
export const INTERP_ME_MODE = opt({ '': '', 双向运动估计: '双向运动估计', 双侧运动估计: '双侧运动估计' })
export const INTERP_ME = opt({
  '': '', 穷举搜索: '穷举搜索', 三步搜索: '三步搜索', 二维对数搜索: '二维对数搜索', 新三步搜索: '新三步搜索',
  四步搜索: '四步搜索', 菱形搜索: '菱形搜索', 基于Hexagon: '基于 Hexagon',
  增强的预测区域: '增强的预测区域', 不均匀多六边形: '不均匀多六边形',
})
export const INTERP_MC_MODE = opt({ '': '', 重叠块运动补偿: '重叠块运动补偿', 加权obmc: '加权 obmc' })
// 同上：'48' / '60' / '120' 是数字键，会抢到对象最前，这里改用数组字面量
export const NVFRUC_RATE: FieldOption[] = [
  { value: '', label: '' },
  { value: 'source_fps*2', label: 'source_fps*2' },
  { value: '48', label: '48' },
  { value: '60', label: '60' },
  { value: '120', label: '120' },
]
export const NVFRUC_PERF = opt({ '': '', slow: 'slow', medium: 'medium', fast: 'fast' })
export const NVFRUC_GRID = [
  { value: '', label: '' },
  { value: 'auto', label: 'auto' },
  { value: '1', label: '1', hint: '1x1 网格，细节最好' },
  { value: '2', label: '2', hint: '2x2 网格' },
  { value: '4', label: '4', hint: '4x4 网格' },
  { value: '8', label: '8', hint: '8x8 网格，速度最快但细节最少' },
]
export const SUPERSAMPLE_ALGO = opt({
  '': '', none: 'none', oversample: 'oversample', bilinear: 'bilinear', nearest: 'nearest', bicubic: 'bicubic',
  lanczos: 'lanczos', ewa_lanczos: 'ewa_lanczos', ewa_lanczossharp: 'ewa_lanczossharp',
  ewa_lanczos4sharpest: 'ewa_lanczos4sharpest', gaussian: 'gaussian', spline16: 'spline16', spline36: 'spline36',
  spline64: 'spline64', mitchell: 'mitchell', sinc: 'sinc', ginseng: 'ginseng', ewa_jinc: 'ewa_jinc',
  ewa_ginseng: 'ewa_ginseng', ewa_hann: 'ewa_hann', hermite: 'hermite', catmull_rom: 'catmull_rom',
  robidoux: 'robidoux', robidouxsharp: 'robidouxsharp', ewa_robidoux: 'ewa_robidoux',
  ewa_robidouxsharp: 'ewa_robidouxsharp', triangle: 'triangle', ewa_hanning: 'ewa_hanning',
})

// ── 色彩管理页的下拉项（值参与命令生成，显示文本照抄原版）──
export const COLOR_FILTER = opt({ '': '', zscale: 'zscale', libplacebo: 'libplacebo' })
export const COLOR_MATRIX = opt({ '': '', auto: 'auto', bt709: 'bt709', bt2020nc: 'bt2020nc', bt2020c: 'bt2020c', rgb: 'rgb', gbr: 'gbr', bt470bg: 'bt470bg', smpte170m: 'smpte170m', smpte240m: 'smpte240m', fcc: 'fcc', ictcp: 'ictcp', ycgco: 'ycgco', xyz: 'xyz' })
export const COLOR_PRIMARIES = opt({ '': '', auto: 'auto', bt709: 'bt709', bt2020: 'bt2020', smpte428: 'smpte428', smpte431: 'smpte431', smpte432: 'smpte432', film: 'film', bt470m: 'bt470m', bt470bg: 'bt470bg', smpte170m: 'smpte170m', smpte240m: 'smpte240m', 'jedec-p22': 'jedec-p22', ebu3213: 'ebu3213' })
export const COLOR_TRC = opt({ '': '', auto: 'auto', bt709: 'bt709', 'bt2020-10': 'bt2020-10', 'bt2020-12': 'bt2020-12', smpte2084: 'smpte2084', bt470m: 'bt470m', bt470bg: 'bt470bg', log: 'log', log_sqrt: 'log_sqrt', linear: 'linear', bt1361e: 'bt1361e', 'iec61966-2-1': 'iec61966-2-1', 'iec61966-2-4': 'iec61966-2-4', smpte170m: 'smpte170m', smpte240m: 'smpte240m', gamma22: 'gamma22', gamma28: 'gamma28', 'arib-std-b67': 'arib-std-b67' })
export const COLOR_RANGE = opt({ '': '', 'tv 有限 16~235': 'tv 有限 16~235', 'pc 全范围 0~255': 'pc 全范围 0~255' })
export const COLOR_TONEMAP = opt({ '': '', auto: 'auto', clip: 'clip', 'st2094-40': 'st2094-40', 'st2094-10': 'st2094-10', 'bt.2390': 'bt.2390', 'bt.2446a': 'bt.2446a', spline: 'spline', reinhard: 'reinhard', mobius: 'mobius', hable: 'hable', gamma: 'gamma', linear: 'linear' })
/** 核心侧按这三条中文文本判断元数据/滤镜行为，值不可改 */
export const COLOR_MODE = opt({ '': '', 写入元数据并转换: '写入元数据并转换', 仅写入元数据: '仅写入元数据', 仅转换: '仅转换' })

/** 原版二级窗口里随「滤镜选择」变化的参数名（取自各窗体 .vb 的 配置XX参数 调用） */
const 降噪参数名表: Record<string, string[]> = {
  hqdn3d: ['亮度空间强度 luma_spatial 默认 4', '色度空间强度 chroma_spatial 默认 3', '亮度时间强度 luma_tmp 默认 6', '色度时间强度 chroma_tmp 默认 4.5'],
  nlmeans: ['降噪强度 s (strength) 默认 1.0', '参考像素块大小 p (patch size) 默认 7，须奇数', '色度参考像素块大小 pc 默认 0，须奇数', '搜索半径 r (research size) 默认 15'],
  atadenoise: ['亮度静态帧加权 0a 默认 0.02', '亮度动态帧加权 0b 默认 0.04', '色度静态加权 1a 默认 0.02', '色度动态加权 1b 默认 0.04'],
  bm3d: ['噪声强度 sigma 默认 1', '块大小 block 默认 16', '块步长 bstep 默认 4', '相似块数量 group 默认 1'],
  bilateral_cuda: ['空间 sigma sigmaS 默认 0.1', '范围 sigma sigmaR 默认 0.1', '邻域窗口大小 window_size 默认 1'],
}
const 锐化参数名表: Record<string, string[]> = {
  cas: ['锐化强度 strength 默认 0，建议 0.20~0.35', '处理颜色平面 planes 默认 7，1/2/4 分别对应前三个平面'],
  unsharp: ['横向矩阵尺寸 luma_msize_x 默认 5，范围 3~23', '纵向矩阵尺寸 luma_msize_y 默认 5，范围 3~23', '锐化强度 luma_amount 默认 1，范围 -2~5'],
}
const 胶片颗粒参数名表: Record<string, string[]> = {
  noise_全平面动态均匀颗粒: ['颗粒强度 all_strength 默认 6，范围 0~100', '随机种子 all_seed 默认 -1 自动随机'],
  noise_亮度为主动态颗粒: ['亮度颗粒强度 c0_strength 默认 6，范围 0~100', '色度颗粒强度 c1/c2_strength 默认 2，范围 0~100', '随机种子 all_seed 默认 -1 自动随机'],
  noise_柔和平均颗粒: ['柔和颗粒强度 all_strength 默认 5，范围 0~100', '随机种子 all_seed 默认 -1 自动随机'],
  libplacebo_应用片源胶片颗粒元数据: ['应用片源 film grain 元数据 固定 true'],
}
const 平滑断层参数名表: Record<string, string[]> = {
  deband_标准去色带: ['阈值 1thr/2thr/3thr 默认 0.020，范围 0.00003~0.5', '采样范围 range 默认 16，可按画面尺寸提高', '方向 direction 默认 0，范围 -6.28~6.28', '平面耦合 coupling 默认 0，0/1'],
  deband_强力去色带: ['强力阈值 1thr/2thr/3thr 默认 0.035，范围 0.00003~0.5', '强力采样范围 range 默认 32，可按画面尺寸提高', '方向 direction 默认 0，范围 -6.28~6.28', '平面耦合 coupling 默认 1，0/1'],
  gradfun_快速渐变平滑: ['平滑强度 strength 默认 1.2，范围 0.51~64', '渐变拟合半径 radius 默认 16，范围 4~32'],
  libplacebo_GPU去色带加颗粒: ['迭代次数 deband_iterations 默认 1，范围 0~16', '阈值 deband_threshold 默认 4，范围 0~1024', '半径 deband_radius 默认 16，范围 0~1024', '补偿颗粒 deband_grain 默认 6，范围 0~1024'],
}

/** 生成随预设变化的参数名说明（原版是随滤镜选择改 HCL_ 文本） */
function 参数名(表: Record<string, string[]>, 方式路径: string, 序号: number) {
  return (preset: Record<string, unknown>): string =>
    表[String(getByPath(preset, 方式路径) ?? '').trim()]?.[序号 - 1] ?? ''
}

/** 二级菜单结构（与原版 20 项 / 6 组完全一致） */
export type SubNavEntry = { type: 'item'; id: string; label: string } | { type: 'sep' }

/** 各页字段定义（顺序即二级菜单顺序） */
const PAGES: GroupDef[] = [
  {
    id: 'overview',
    title: '参数总览',
    kind: 'overview',
    hint: '按当前参数生成的 ffmpeg 命令行模板与警告提示',
    sections: [],
  },
  {
    id: 'presets',
    title: '预设管理',
    kind: 'presets',
    hint: '先选择预设来源，双击或读取来加载，用户和社区预设选中后按 Delete 删除到回收站',
    sections: [],
  },
  {
    id: 'output',
    title: '输出文件设置',
    kind: 'fields',
    // 原版 Form_v6_参数面板_输出文件设置.Designer.vb 的 HCL_后缀和位置 原文（Silver 色 span 去掉后照抄）
    hint: '后缀和位置   输出目录默认不会保存到预设中，需要额外打开保存开关',
    sections: [
      {
        fields: [
          { path: '输出容器', label: '输出容器', type: { kind: 'select', options: CONTAINERS.map(c => ({ value: c, label: c === '' ? '（按输入/自动）' : c })) }, hint: '留空表示按输入文件或输出路径决定扩展名' },
          { path: '输出_输出文件参数使用方法', label: '输出文件参数', type: { kind: 'select', options: opt({ '': '', 正常使用: '正常使用', 不附加: '不附加', 声明丢弃输出: '声明丢弃输出' }) }, hint: '留空 = 正常使用（把输出文件路径追加到命令行末尾）' },
          { path: '输出_自动命名选项', label: '自动命名', type: { kind: 'select', options: AUTO_NAME } },
          { path: '输出命名_开头文本', label: '命名·开头文本', type: { kind: 'text' } },
          { path: '输出命名_替代文本', label: '命名·替代文本', type: { kind: 'text' } },
          { path: '输出命名_结尾文本', label: '命名·结尾文本', type: { kind: 'text' } },
          { path: '输出命名_保留创建时间', label: '保留创建时间', type: { kind: 'bool' } },
          { path: '输出命名_保留修改时间', label: '保留修改时间', type: { kind: 'bool' } },
          { path: '输出命名_保留访问时间', label: '保留访问时间', type: { kind: 'bool' } },
          { path: '输出目录', label: '输出目录', type: { kind: 'text' }, placeholder: '空 = 输入文件同目录', full: true },
          { path: '输出位置', label: '输出位置（本机）', type: { kind: 'text' }, full: true, width: 420, browse: 'dir', placeholder: '/media/输出目录', hint: '留空则输出到输入文件同目录；选择了目录才会用它' },
          { path: '输出位置_保留子文件夹结构起始点', label: '保留子文件夹结构起始点', type: { kind: 'text' }, full: true },
        ],
      },
    ],
  },
  {
    id: 'decode',
    title: '解码设置',
    kind: 'fields',
    sections: [
      {
        fields: [
          { path: '解码参数_解码器', label: '解码器', type: { kind: 'text' }, placeholder: '如 cuda / dxva2 / 留空自动', hint: '指定硬件解码器，留空由 ffmpeg 自动选择' },
          { path: '解码参数_CPU解码线程数', label: 'CPU 解码线程数', type: { kind: 'text' } },
          { path: '解码参数_解码数据格式', label: '解码数据格式', type: { kind: 'text' } },
          { path: '解码参数_指定硬件的参数名', label: '指定硬件·参数名', type: { kind: 'text' } },
          { path: '解码参数_指定硬件的参数', label: '指定硬件·参数', type: { kind: 'text' } },
        ],
      },
    ],
  },
  {
    id: 'vcodec',
    title: '视频参数 | 编码器',
    kind: 'fields',
    hint: '依次选择类别，再选具体；可编辑设置文件添加自定义',
    legend: [
      { text: 'lib = ' },
      { text: 'CPU', tone: 'purple' },
      { text: '，nvenc = ' },
      { text: 'NVIDIA', tone: 'green' },
      { text: '，qsv = ' },
      { text: 'Intel', tone: 'blue' },
      { text: '，amf = ' },
      { text: 'AMD', tone: 'red' },
    ],
    sections: [
      {
        title: '视频编码器',
        fields: [
          {
            path: '视频参数_编码器_类型',
            label: '类型',
            bare: true,
            type: { kind: 'select', placeholder: '类型', width: 100, options: opt({ '': '', 视频: '视频', 图片: '图片' }) },
          },
          {
            path: '视频参数_编码器_分类名称',
            label: '分类',
            bare: true,
            type: { kind: 'select', placeholder: '分类', width: 200, maxItems: 15, options: [] },
          },
          {
            path: '视频参数_编码器_具体编码',
            label: '具体编码',
            bare: true,
            type: { kind: 'select', placeholder: '具体编码', width: 160, maxItems: 20, tooltipSide: 'left', tooltipMaxWidth: 500, options: [] },
          },
          {
            path: '视频参数_编码器_图片编码器质量值',
            label: '图片质量值',
            showKey: 'imageQuality',
            placeholder: '质量值',
            width: 100,
            type: { kind: 'text' },
          },
        ],
      },
      {
        title: '编码预设',
        hint: '如何平衡压缩度和速度，往上越慢，往下越快',
        fields: [
          {
            path: '视频参数_编码器_编码预设',
            bare: true,
            label: '编码预设',
            placeholder: '-preset',
            type: { kind: 'select', width: 150, options: [] },
          },
        ],
      },
      {
        title: '配置文件',
        hint: '控制要支持怎样的技术规格和功能，一般不用指定',
        fields: [
          {
            path: '视频参数_编码器_配置文件',
            bare: true,
            label: '配置文件',
            placeholder: '-profile:v',
            type: { kind: 'select', width: 150, options: [] },
          },
        ],
      },
      {
        title: '场景优化',
        hint: '对特定需求的专项优化，例如 CPU 编码的颗粒保留或是 GPU 编码的特调模式',
        fields: [
          {
            path: '视频参数_编码器_场景优化',
            bare: true,
            label: '场景优化',
            placeholder: '-tune',
            type: { kind: 'select', width: 150, options: [] },
          },
        ],
      },
      {
        title: '性能选项',
        hint: '通常不需要考虑，也不一定起作用',
        fields: [
          {
            path: '视频参数_编码器_gpu',
            label: '-gpu',
            inline: true,
            width: 100,
            placeholder: '-gpu',
            type: { kind: 'text' },
            hint: '指定 NVIDIA 显卡索引号，其他卡请从系统硬件加速或驱动中设置',
            info: '此选项仅适用于 CPU 解码 + NV 编码的组合，如果使用全流程 NV cuda 链路，无需设置此参数，在解码参数里指定硬件即可',
          },
          {
            path: '视频参数_编码器_threads',
            label: '-threads',
            inline: true,
            width: 100,
            placeholder: '-threads',
            type: { kind: 'text' },
            hint: '指定 CPU 编码线程数，不一定有效，编码器有自己的逻辑',
          },
        ],
      },
    ],
  },
  {
    id: 'frame',
    title: '视频参数 | 画面帧',
    kind: 'fields',
    hint: '分辨率、帧率、画面增强与内容处理',
    sections: [
      {
        title: '分辨率',
        hint: '推荐使用在滤镜中处理的单独缩放',
        fields: [
          {
            path: '视频参数_分辨率', label: '', inline: true,
            hint: '批量任务通常不使用直接指定的方式', hintTone: 'gold', info: '更建议用滤镜去缩放',
            type: { kind: 'select', width: 150, editable: true, placeholder: '直接指定', options: FRAME_RESOLUTION },
          },
          {
            path: '视频参数_分辨率自动计算_缩放滤镜', label: '', inline: true,
            hint: '更推荐使用这里的用滤镜缩放', hintTone: 'green',
            info: '宽度和高度可以只写一个来表示另一个按照原视频比例自动进行缩放',
            type: { kind: 'select', width: 230, placeholder: '指定缩放滤镜', options: FRAME_SCALE_FILTER },
          },
          { path: '视频参数_分辨率自动计算_宽度', label: '', type: { kind: 'select', width: 150, editable: true, placeholder: '宽度缩放', options: FRAME_WIDTH } },
          { path: '视频参数_分辨率自动计算_高度', label: '', type: { kind: 'select', width: 150, editable: true, placeholder: '高度缩放', options: FRAME_HEIGHT } },
          { path: '视频参数_分辨率自动计算_缩放算法', label: '', type: { kind: 'select', width: 150, placeholder: '指定缩放算法', options: FRAME_SCALE_ALGO } },
          {
            path: '视频参数_分辨率_裁剪滤镜参数', label: '', inline: true, width: 150, placeholder: 'crop',
            hint: '默认将裁剪的滤镜放在缩放之前', hintTone: 'purple',
            button: { text: '画面裁剪交互', dialog: 'area' },
            type: { kind: 'text' },
          },
        ],
      },
      {
        title: '帧率',
        hint: '收藏内容切勿抽帧！',
        fields: [
          { path: '视频参数_帧速率', label: '', type: { kind: 'select', width: 150, editable: true, placeholder: '直接指定', options: FRAME_RATE } },
          {
            path: '视频参数_帧速率模式', label: '',
            type: { kind: 'select', width: 150, placeholder: '强调帧率模式', options: FRAME_RATE_MODE },
            button: { text: '抽帧设置', dialog: '抽帧参数', after: true },
          },
        ],
        buttons: [
          { text: 'CPU 简易插帧', dialog: '插帧_简易' },
          { text: 'NV FRUC 插帧', dialog: 'NV_FRUC插帧' },
        ],
      },
      {
        title: '增强',
        hint: '专业需求请考虑行业软件或 AI 软件',
        fields: [],
        buttons: [
          { text: '动态模糊', dialog: '动态模糊' },
          { text: '着色器超分', dialog: '超分' },
          { text: '传统降噪', dialog: '降噪' },
          { text: '传统锐化', dialog: '锐化' },
          { text: '胶片颗粒', dialog: '胶片颗粒' },
          { text: '扫描方式', dialog: '扫描方式' },
          { text: '画面翻转', dialog: '画面翻转' },
          { text: '平滑断层', dialog: '平滑断层' },
        ],
      },
      {
        title: '内容',
        hint: '专业需求请用剪辑和特效软件',
        fields: [],
        buttons: [{ text: '烧录字幕', dialog: '烧录字幕' }],
      },
    ],
    dialogs: [
      {
        id: '降噪',
        title: '降噪',
        toggle: { text: '降噪总开关 / 勾选才会启用' },
        fields: [
          { path: '视频参数_降噪_方式', label: '', inline: true, type: { kind: 'select', width: 544, options: DENOISE } },
          { path: '视频参数_降噪_参数1', label: '', inline: true, width: 150, type: { kind: 'text' }, hint: 参数名(降噪参数名表, '视频参数_降噪_方式', 1) },
          { path: '视频参数_降噪_参数2', label: '', inline: true, width: 150, type: { kind: 'text' }, hint: 参数名(降噪参数名表, '视频参数_降噪_方式', 2) },
          { path: '视频参数_降噪_参数3', label: '', inline: true, width: 150, type: { kind: 'text' }, hint: 参数名(降噪参数名表, '视频参数_降噪_方式', 3) },
          { path: '视频参数_降噪_参数4', label: '', inline: true, width: 150, type: { kind: 'text' }, hint: 参数名(降噪参数名表, '视频参数_降噪_方式', 4) },
        ],
      },
      {
        id: '锐化',
        title: '锐化',
        toggle: { text: '锐化总开关 / 勾选才会启用' },
        fields: [
          { path: '视频参数_锐化_方式', label: '', inline: true, type: { kind: 'select', width: 544, options: SHARPEN } },
          { path: '视频参数_锐化_参数1', label: '', inline: true, width: 150, type: { kind: 'text' }, hint: 参数名(锐化参数名表, '视频参数_锐化_方式', 1) },
          { path: '视频参数_锐化_参数2', label: '', inline: true, width: 150, type: { kind: 'text' }, hint: 参数名(锐化参数名表, '视频参数_锐化_方式', 2) },
          { path: '视频参数_锐化_参数3', label: '', inline: true, width: 150, type: { kind: 'text' }, hint: 参数名(锐化参数名表, '视频参数_锐化_方式', 3) },
        ],
      },
      {
        id: '胶片颗粒',
        title: '胶片颗粒',
        toggle: { text: '胶片颗粒总开关 / 勾选才会启用' },
        fields: [
          { path: '视频参数_胶片颗粒_方式', label: '', inline: true, type: { kind: 'select', width: 544, options: GRAIN } },
          { path: '视频参数_胶片颗粒_参数1', label: '', inline: true, width: 150, type: { kind: 'text' }, hint: 参数名(胶片颗粒参数名表, '视频参数_胶片颗粒_方式', 1) },
          { path: '视频参数_胶片颗粒_参数2', label: '', inline: true, width: 150, type: { kind: 'text' }, hint: 参数名(胶片颗粒参数名表, '视频参数_胶片颗粒_方式', 2) },
          { path: '视频参数_胶片颗粒_参数3', label: '', inline: true, width: 150, type: { kind: 'text' }, hint: 参数名(胶片颗粒参数名表, '视频参数_胶片颗粒_方式', 3) },
          { path: '视频参数_胶片颗粒_参数4', label: '', inline: true, width: 150, type: { kind: 'text' }, hint: 参数名(胶片颗粒参数名表, '视频参数_胶片颗粒_方式', 4) },
        ],
      },
      {
        id: '平滑断层',
        title: '平滑断层',
        toggle: { text: '平滑断层总开关 / 勾选才会启用' },
        fields: [
          { path: '视频参数_平滑断层_方式', label: '', inline: true, type: { kind: 'select', width: 544, options: BANDING } },
          { path: '视频参数_平滑断层_参数1', label: '', inline: true, width: 150, type: { kind: 'text' }, hint: 参数名(平滑断层参数名表, '视频参数_平滑断层_方式', 1) },
          { path: '视频参数_平滑断层_参数2', label: '', inline: true, width: 150, type: { kind: 'text' }, hint: 参数名(平滑断层参数名表, '视频参数_平滑断层_方式', 2) },
          { path: '视频参数_平滑断层_参数3', label: '', inline: true, width: 150, type: { kind: 'text' }, hint: 参数名(平滑断层参数名表, '视频参数_平滑断层_方式', 3) },
          { path: '视频参数_平滑断层_参数4', label: '', inline: true, width: 150, type: { kind: 'text' }, hint: 参数名(平滑断层参数名表, '视频参数_平滑断层_方式', 4) },
        ],
      },
      {
        id: '动态模糊',
        title: '动态模糊',
        toggle: { text: '动态模糊总开关 / 勾选才会启用' },
        fields: [
          { path: '视频参数_动态模糊_连续混合帧数', label: '连续混合帧数', inline: true, width: 100, placeholder: 'frames=', hint: '整数，1~1024，默认 3', type: { kind: 'text' } },
          { path: '视频参数_动态模糊_每帧权重', label: '每帧的权重', inline: true, width: 300, placeholder: 'weights=', hint: '用空格分割每帧权重值 weights 数量可以少于 frames，不够的部分会重复最后一个权重', type: { kind: 'text' } },
          { path: '视频参数_动态模糊_输出缩放系数', label: '输出缩放系数', inline: true, width: 100, placeholder: 'scale=', hint: '0~32767，默认 0 自动按权重归一化', type: { kind: 'text' } },
          {
            path: '视频参数_动态模糊_处理颜色平面', label: '处理哪些颜色平面', inline: true, width: 100, placeholder: 'planes=',
            hint: '0~15，默认 15 处理所有平面',
            info: '1 = 只处理第 0 平面，常见是 Y / R；\n2 = 只处理第 1 平面，常见是 U / G\n；4 = 只处理第 2 平面，常见是 V / B\n；8 = 只处理第 3 平面，常见是 A\n；15 = 1+2+4+8，处理全部平面',
            type: { kind: 'text' },
          },
        ],
      },
      {
        id: '扫描方式',
        title: '扫描方式',
        toggle: { text: '扫描方式总开关 / 勾选才会启用' },
        fields: [
          { path: '视频参数_处理扫描方式', label: '', inline: true, type: { kind: 'select', width: 544, options: SCAN } },
        ],
      },
      {
        id: '画面翻转',
        title: '画面翻转',
        toggle: { text: '画面翻转总开关 / 勾选才会启用' },
        fields: [
          { path: '视频参数_画面翻转_角度翻转', label: '角度翻转', inline: true, type: { kind: 'select', width: 220, options: ROTATE } },
          { path: '视频参数_画面翻转_镜像翻转', label: '镜像翻转', inline: true, type: { kind: 'select', width: 180, options: MIRROR } },
        ],
      },
      {
        id: '超分',
        title: '着色器超分',
        toggle: { text: '超分总开关 / 勾选才会使用 / 建议考虑 AI 软件，但合适的着色器配合性价比更高' },
        fields: [
          { path: '视频参数_超分_直接面板.目标宽度', label: '宽度', inline: true, width: 80, placeholder: 'w=', hint: '此设置仅影响此滤镜的输出，后续处理仍可配合使用', type: { kind: 'text' } },
          { path: '视频参数_超分_直接面板.目标高度', label: '高度', inline: true, width: 80, placeholder: 'h=', type: { kind: 'text' } },
          { path: '视频参数_超分_直接面板.上采样算法', label: '上采样算法 (放大用)', inline: true, hint: '仅提供重要设置，如需深度控制请去自定义参数实现整套滤镜参数', type: { kind: 'select', width: 150, placeholder: 'upscaler=', options: SUPERSAMPLE_ALGO } },
          { path: '视频参数_超分_直接面板.下采样算法', label: '下采样算法 (缩小用)', inline: true, type: { kind: 'select', width: 150, placeholder: 'downscaler=', options: SUPERSAMPLE_ALGO } },
          { path: '视频参数_超分_直接面板.抗振铃强度', label: '抗振铃强度', inline: true, width: 150, placeholder: 'antiringing=', hint: '抗振铃强度 0~1 建议 0.3~0.7', type: { kind: 'text' } },
          { path: '视频参数_超分_直接面板.着色器文件路径', label: '着色器', inline: true, width: 470, placeholder: 'custom_shader_path=', hint: '非常推荐使用着色器，支持 .glsl 和 .hook 格式，例如 Anime4K、FSRCNNX', type: { kind: 'text' } },
        ],
      },
      {
        id: '烧录字幕',
        title: '烧录字幕',
        toggle: { text: '烧录字幕总开关 / 勾选才会烧 / 这是把字幕画到视频帧上，是破坏画面的行为，混流字幕在流控制里' },
        fields: [
          { path: '视频参数_烧录字幕_滤镜选择', label: '', inline: true, type: { kind: 'select', width: 156, options: SUBTITLE_FILTER }, info: '建议使用默认 subtitles。ass 滤镜几乎只支持 ass 格式，而且几乎无法复写样式。' },
          { path: '视频参数_烧录字幕_字幕来源是外部文件', label: '字幕来源', inline: true, type: { kind: 'select', width: 154, options: SUBTITLE_SOURCE } },
          { path: '视频参数_烧录字幕_字幕格式优先级', label: '后缀优先级（优先 -> 然后 -> 最后）', inline: true, width: 330, type: { kind: 'stringlist', hint: '按优先级填写，如 SRT,ASS' } },
          { path: '视频参数_烧录字幕_外部字幕文件名', label: '字幕文件名多余字符', inline: true, width: 320, info: '字幕文件名不与视频同名？可在此指定多余字符 (不含后缀)', type: { kind: 'text' } },
          { path: '视频参数_烧录字幕_外部字幕文件夹位置', label: '字幕文件路径（不填是同目录）', inline: true, width: 320, info: '字幕文件在其他地方吗？可在此指定文件夹', type: { kind: 'text' } },
          { path: '视频参数_烧录字幕_指定内嵌的流', label: '流索引', inline: true, width: 100, type: { kind: 'text' } },
          { path: '视频参数_烧录字幕_基本样式_名称', label: '样式·字体名称', inline: true, width: 175, type: { kind: 'text' } },
          { path: '视频参数_烧录字幕_基本样式_大小', label: '样式·大小', inline: true, type: { kind: 'number' } },
          { path: '视频参数_烧录字幕_基本样式_粗体', label: '样式·粗体', inline: true, type: { kind: 'bool' } },
          { path: '视频参数_烧录字幕_基本样式_斜体', label: '样式·斜体', inline: true, type: { kind: 'bool' } },
          { path: '视频参数_烧录字幕_基本样式_下划线', label: '样式·下划线', inline: true, type: { kind: 'bool' } },
          { path: '视频参数_烧录字幕_基本样式_删除线', label: '样式·删除线', inline: true, type: { kind: 'bool' } },
          { path: '视频参数_烧录字幕_边框样式', label: '边框类型', inline: true, type: { kind: 'select', width: 182, options: SUBTITLE_BORDER } },
          { path: '视频参数_烧录字幕_描边宽度', label: '描边宽度', inline: true, width: 64, type: { kind: 'text' } },
          { path: '视频参数_烧录字幕_阴影距离', label: '阴影距离', inline: true, width: 64, type: { kind: 'text' } },
          { path: '视频参数_烧录字幕_主要颜色', label: '主要颜色', inline: true, type: { kind: 'color' } },
          { path: '视频参数_烧录字幕_次要颜色', label: '次要颜色', inline: true, type: { kind: 'color' } },
          { path: '视频参数_烧录字幕_描边颜色', label: '描边颜色', inline: true, type: { kind: 'color' } },
          { path: '视频参数_烧录字幕_背景颜色', label: '背景颜色', inline: true, type: { kind: 'color' } },
          { path: '视频参数_烧录字幕_对齐方位', label: '对齐方位', inline: true, type: { kind: 'select', width: 130, options: SUBTITLE_ALIGN } },
          { path: '视频参数_烧录字幕_垂直边距', label: '垂直边距', inline: true, width: 130, type: { kind: 'text' } },
          { path: '视频参数_烧录字幕_左边距', label: '左边距', inline: true, width: 130, type: { kind: 'text' } },
          { path: '视频参数_烧录字幕_右边距', label: '右边距', inline: true, width: 130, type: { kind: 'text' } },
          { path: '视频参数_烧录字幕_字距', label: '字距', inline: true, width: 130, type: { kind: 'text' } },
          { path: '视频参数_烧录字幕_行距', label: '行距', inline: true, width: 130, type: { kind: 'text' } },
          { path: '视频参数_烧录字幕_字体文件夹', label: '字体文件夹', inline: true, width: 330, info: '要正确渲染 ass 中所使用的非常用字体，除了将字体文件安装到系统外，还可以手动指定文件夹', type: { kind: 'text' } },
          { path: '视频参数_烧录字幕_补充样式', label: '补充样式 (force_style)', inline: true, width: 304, type: { kind: 'text' } },
          { path: '视频参数_烧录字幕_自己写滤镜取代所有设置', label: '自己写滤镜取代所有设置', inline: true, type: { kind: 'textarea', rows: 4, placeholder: '一旦写了这个，本页所有设置除了总开关之外全部失效，以这里的为准，这就像自定义参数里完全自己写模式一样' } },
        ],
      },
      {
        id: '抽帧参数',
        title: '视频抽帧',
        toggle: { text: '抽帧总开关 / 勾选才会使用' },
        hint: '如何决定是否需要抽帧   三思而后行\n当你决定要对视频抽帧时，即代表你认为视频的细节不重要，且没有收藏意义。如果不能同时满足这两点，则不应考虑使用。抽帧是在能够正确传达信息的前提下以细节大量损失为代价换取体积大幅降低来极大增加信息传播效率的手段，属于压片战争的邪修流。如果你的存储空间紧张到需要对收藏内容进行抽帧了，此时你应该去扩充空间，而不是损失自己的收藏。',
        fields: [
          { path: '视频参数_抽帧_max', label: '连续丢帧数量  默认：0', inline: true, width: 100, placeholder: 'max', hint: '正数：最多允许连续丢弃的帧数 负数：两次丢帧之间的最小间隔帧数 0：不限制，无论之前连续丢了多少帧都可以继续丢', type: { kind: 'text' } },
          { path: '视频参数_抽帧_keep', label: '连续相似要求  默认：0', inline: true, width: 100, placeholder: 'keep', hint: '连续相似帧达到多少才开始丢', type: { kind: 'text' } },
          { path: '视频参数_抽帧_hi', label: '高阈值，所有 8*8=64 的像素块差异最大值', inline: true, width: 150, placeholder: 'hi', hint: '格式：64 个像素 × 每像素平均差值 ? 例如：64*10 或 640，写乘法和结果都可以 表示：如果有任一 8*8 块中的每个像素平均变化了 10 灰度级则不丢帧', type: { kind: 'text' } },
          { path: '视频参数_抽帧_lo', label: '低阈值，所有 8*8=64 的像素块差异最小值', inline: true, width: 150, placeholder: 'lo', hint: '（格式同上）在满足高阈值的前提下 变化必须超过低阈值且不能超过最大占比才会丢帧', type: { kind: 'text' } },
          { path: '视频参数_抽帧_frac', label: '允许超过低阈值的最大占比（1=整张图）', inline: true, width: 100, placeholder: 'frac', hint: '例如 0.1 表示只有 10% 以下的变化才会丢帧', type: { kind: 'text' } },
        ],
      },
      {
        id: '插帧_简易',
        title: '简易插帧',
        toggle: { text: '插帧总开关 / 勾选才会使用 / 建议考虑 AI 软件，如 SVP、TopazAI' },
        fields: [
          { path: '视频参数_插帧_目标帧率', label: '目标帧率', inline: true, width: 100, placeholder: 'fps=', hint: '然后就不要在其他地方设置帧率了', type: { kind: 'text' } },
          { path: '视频参数_插帧_插帧模式', label: '插帧模式', inline: true, hint: '推荐的最佳质量：运动补偿插值 + 加权 obmc', type: { kind: 'select', width: 150, placeholder: 'mi_mode=', options: INTERP_MODE } },
          { path: '视频参数_插帧_运动估计模式', label: '运动估计模式', inline: true, type: { kind: 'select', width: 150, placeholder: 'me_mode=', options: INTERP_ME_MODE } },
          { path: '视频参数_插帧_运动估计算法', label: '运动估计算法', inline: true, type: { kind: 'select', width: 150, placeholder: 'me=', options: INTERP_ME } },
          { path: '视频参数_插帧_运动补偿模式', label: '运动补偿模式', inline: true, type: { kind: 'select', width: 150, placeholder: 'mc_mode=', options: INTERP_MC_MODE } },
          { path: '视频参数_插帧_可变块大小的运动补偿', label: '可变块大小的运动补偿 vsbmc=1', inline: true, type: { kind: 'bool' } },
          { path: '视频参数_插帧_块大小', label: '块大小 (默认 16)', inline: true, width: 150, placeholder: 'mb_size=', hint: '值越大计算需求越多越稳定', type: { kind: 'text' } },
          { path: '视频参数_插帧_搜索范围', label: '搜索范围 (默认 32)', inline: true, width: 150, placeholder: 'search_param=', type: { kind: 'text' } },
          { path: '视频参数_插帧_场景变化检测强度', label: '场景变化检测强度 scd=fdiff 默认 10', inline: true, width: 150, placeholder: 'scd_threshold=', type: { kind: 'text' } },
        ],
      },
      {
        id: 'NV_FRUC插帧',
        title: 'NVIDIA Vulkan FRUC 光流加速插帧',
        toggle: { text: '插帧总开关 / 勾选才会使用 / 至少需要 RTX30 显卡' },
        hint: '要使用此滤镜，必须设定 Vulkan 处理流程',
        fields: [
          { path: '视频参数_NV_FRUC_目标帧率', label: '目标帧率', inline: true, hint: '然后就不要在其他地方设置帧率了', type: { kind: 'select', width: 150, placeholder: 'fps=', options: NVFRUC_RATE } },
          { path: '视频参数_NV_FRUC_质量和速度', label: '质量和速度', inline: true, hint: '越慢质量越好，越快质量越差', type: { kind: 'select', width: 150, placeholder: 'perf=', options: NVFRUC_PERF } },
          { path: '视频参数_NV_FRUC_网格大小', label: '网格大小', inline: true, hint: '单位：像素矩形边长。网格越小质量越高，同时越吃显存', type: { kind: 'select', width: 150, placeholder: 'grid=', options: NVFRUC_GRID } },
        ],
      },
      {
        id: 'area',
        title: '画面区域选择',
        hint: '可点击打开或拖入视频 / 图片；图片优先直接加载，视频默认取第 10 秒，可先指定时间戳；鼠标左键移动视野，滚轮缩放，鼠标右键绘制新框选；比例可选择或手写 16:9 / 1.777',
        fields: [
          { path: '视频参数_分辨率_裁剪滤镜参数', label: '裁剪参数', inline: true, width: 168, placeholder: '宽:高:左上X:左上Y', type: { kind: 'text' } },
        ],
      },
    ],
  },
  {
    id: 'quality',
    title: '视频参数 | 质量',
    kind: 'fields',
    sections: [
      {
        title: '全局质量控制',
        hint: '常规压制仅需在此设置全局质量即可满足需求',
        hints: ['CPU 编码首选 CRF 对应 -crf；GPU 编码首选 VBR', 'NVIDIA 使用专属 -cq；Intel 使用 -global_quality；AMD CQP 使用进阶 -qp_i / -qp_p'],
        fields: [
          { path: '视频参数_比特率_控制方式', label: '选择控制方式 -rc', type: { kind: 'select', width: 300, placeholder: '选择控制方式 -rc', options: VIDEO_QUALITY_MODES } },
          { path: '视频参数_质量控制_参数名', label: '参数名', type: { kind: 'select', width: 150, placeholder: '参数名', options: opt({ '': '', '-crf': '-crf', '-cq': '-cq', '-global_quality': '-global_quality', '-qp': '-qp' }) } },
          { path: '视频参数_质量控制_值', label: '质量值', type: { kind: 'text' }, placeholder: '质量值', width: 100 },
        ],
      },
      {
        title: '比特率',
        hint: '传统的转码直接写比特率，范围和缓冲区可配合全局质量控制',
        hints: ['注意带上单位，推荐使用 k（kbps），例如 5000k，其他还有 M（mbps，可能要大写）'],
        columns: 4,
        fields: [
          { path: '视频参数_比特率_基础', label: '基础比特率', type: { kind: 'text' }, placeholder: '-b:v' },
          { path: '视频参数_比特率_最低值', label: '最低比特率', type: { kind: 'text' }, placeholder: '-minrate' },
          { path: '视频参数_比特率_最高值', label: '最高比特率', type: { kind: 'text' }, placeholder: '-maxrate' },
          { path: '视频参数_比特率_缓冲区', label: '缓冲区', type: { kind: 'text' }, placeholder: '-bufsize' },
        ],
      },
      {
        title: '进阶质量控制',
        hint: '可以将编码器内部小参写在这里',
        hints: ['可自由安排换行，注意 参数和值之间 以及 参数与参数之间 的空格即可'],
        action: {
          text: '插入预制条目',
          targetPath: '视频参数_质量控制_进阶参数集',
          insert: 'x264-params=aq-mode=3:deblock=1,1\n# 需要写滤镜请用「滤镜排序」功能，这里强制使用滤镜图，单独写必报错',
        },
        fields: [
          { path: '视频参数_质量控制_进阶参数集', label: '编码器内部小参', type: { kind: 'textarea', rows: 5, placeholder: '如果要写滤镜，请用滤镜排序功能，现在强制使用滤镜图，单独写必报错' }, full: true },
        ],
      },
    ],
  },
  {
    id: 'color',
    title: '视频参数 | 色彩管理',
    kind: 'fields',
    hint: '色彩空间、像素格式与色调映射',
    sections: [
      {
        title: '像素格式',
        hint: '指定像素如何存储，下拉选项跟随选择的具体编码器',
        fields: [
          {
            path: '视频参数_色彩管理_像素格式', label: '最终输出', sideLabel: true,
            info: '此功能需要 CPU 参与，如果你正在使用 NV cuda 全链路，则不能设置此参数，否则会报错，改用滤镜：scale_cuda=format=p010le 或该驱动支持的其他像素格式',
            type: { kind: 'select', width: 175, editable: true, placeholder: '-pix_fmt', options: [] },
          },
          {
            path: '视频参数_色彩管理_像素格式预先转换', label: '预先转换', sideLabel: true,
            hint: '（默认排在所有滤镜最前）', hintTone: 'red',
            info: '一般没有必要设置预先转换，除非你正在进行对滤镜效果有严格要求的编码任务',
            type: {
              kind: 'select', width: 175, editable: true, placeholder: 'format',
              options: [
                { value: '', label: '' },
                { value: 'yuv420p', label: 'yuv420p', hint: 'YUV 4:2:0 8-bit 平面格式，兼容性最好。SDR 常用；HDR 会损失精度，不推荐作为 HDR 中间格式。' },
                { value: 'yuv420p10le', label: 'yuv420p10le', hint: 'YUV 4:2:0 10-bit 平面格式，HDR10/PQ 和 HLG 常用，软件编码器兼容性较好。' },
                { value: 'yuv422p', label: 'yuv422p', hint: 'YUV 4:2:2 8-bit 平面格式，保留更多色度采样，常用于采集或中间流程。' },
                { value: 'yuv422p10le', label: 'yuv422p10le', hint: 'YUV 4:2:2 10-bit 平面格式，适合高质量中间流程或支持 4:2:2 的专业编码。' },
                { value: 'yuv444p', label: 'yuv444p', hint: 'YUV 4:4:4 8-bit 平面格式，不做色度抽样，文件和码率压力更大。' },
                { value: 'yuv444p10le', label: 'yuv444p10le', hint: 'YUV 4:4:4 10-bit 平面格式，高质量中间流程使用；编码器和播放器兼容性要求更高。' },
                { value: 'p010le', label: 'p010le', hint: '10-bit 4:2:0 半平面格式，常见于硬件编码器和 HDR 工作流，如 NVENC、QSV、AMF。' },
              ],
            },
          },
        ],
      },
      {
        title: '色彩空间',
        hint: '在此处转换色彩空间；先选用哪个滤镜，再配置，最后选方式',
        fields: [
          {
            path: '视频参数_色彩管理_滤镜选择', label: '选择滤镜', inline: true,
            hint: 'zscale 使用 CPU 兼容性好，libplacebo 使用 GPU 速度更快',
            type: {
              kind: 'select', width: 175, placeholder: '选择滤镜',
              options: [
                { value: '', label: '' },
                { value: 'zscale', label: 'zscale', hint: '基于 zimg 的 CPU 滤镜，可做矩阵、原色、传输特性和范围转换。适合稳定的标准色彩转换。' },
                { value: 'libplacebo', label: 'libplacebo', hint: '基于 libplacebo 的 GPU 渲染滤镜。支持输出色彩配置、HDR 峰值检测和色调映射，HDR 转 SDR 或复杂映射更适合它。' },
              ],
            },
          },
          {
            path: '视频参数_色彩管理_矩阵系数', label: '矩阵系数 / 颜色格式', inline: true,
            hint: '决定了 亮度和色度 的分配方式',
            type: {
              kind: 'select', width: 175, placeholder: 'colorspace',
              options: [
                { value: '', label: '' },
                { value: 'auto', label: 'auto', hint: '自动或沿用输入；当前生成命令时会视作不显式指定。' },
                { value: 'bt709', label: 'bt709', hint: 'BT.709 亮度/色度矩阵，HD SDR 视频最常见。' },
                { value: 'bt2020nc', label: 'bt2020nc', hint: 'BT.2020 非恒定亮度矩阵，HDR10/PQ 和 HLG 最常用。zscale 中对应 2020_ncl。' },
                { value: 'bt2020c', label: 'bt2020c', hint: 'BT.2020 恒定亮度矩阵，兼容性和实际使用少于 bt2020nc，只有明确需要时再选。' },
                { value: 'rgb', label: 'rgb', hint: 'RGB 色彩空间，不使用常规 YUV 亮度/色度矩阵。适合 RGB 流程，普通 YUV 视频不要误选。' },
                { value: 'gbr', label: 'gbr', hint: 'GBR 平面 RGB 格式常用标记，适合 RGB/GBR 流程，普通 YUV 视频不要误选。' },
                { value: 'bt470bg', label: 'bt470bg', hint: 'BT.470BG / BT.601 625 行矩阵，PAL/SECAM SD 内容常见。' },
                { value: 'smpte170m', label: 'smpte170m', hint: 'SMPTE 170M / BT.601 525 行矩阵，NTSC SD 内容常见。' },
                { value: 'smpte240m', label: 'smpte240m', hint: 'SMPTE 240M 矩阵，早期 HDTV 标准，现代内容较少使用。' },
                { value: 'fcc', label: 'fcc', hint: 'FCC 历史矩阵，旧素材或兼容性场景才可能需要。' },
                { value: 'ictcp', label: 'ictcp', hint: 'ICtCp 色彩表示，BT.2100 HDR/WCG 相关。需要滤镜、编码器和播放器链路明确支持。' },
                { value: 'ycgco', label: 'ycgco', hint: 'YCgCo 颜色变换，部分编码或无损/中间流程可能使用，普通视频输出较少使用。' },
                { value: 'xyz', label: 'xyz', hint: 'CIE XYZ / 数字影院相关颜色表示，通常只用于特殊或影院中间流程。' },
              ],
            },
          },
          {
            path: '视频参数_色彩管理_色域', label: '色域', inline: true,
            hint: '指定采用哪一套色彩标准',
            type: {
              kind: 'select', width: 175, placeholder: 'color_primaries',
              options: [
                { value: '', label: '' },
                { value: 'auto', label: 'auto', hint: '自动或沿用输入；当前生成命令时会视作不显式指定。' },
                { value: 'bt709', label: 'bt709', hint: 'BT.709 原色，HD SDR 视频最常见，也常用于网络 SDR 输出。' },
                { value: 'bt2020', label: 'bt2020', hint: 'BT.2020 宽色域，HDR10/PQ 和 HLG 的常用原色。通常搭配 bt2020nc、smpte2084 或 arib-std-b67。' },
                { value: 'smpte428', label: 'smpte428', hint: 'SMPTE 428 / D-Cinema 相关原色，常与影院或 XYZ 流程相关。' },
                { value: 'smpte431', label: 'smpte431', hint: 'SMPTE 431 / DCI-P3 影院原色，白点不同于常见显示器 P3-D65。' },
                { value: 'smpte432', label: 'smpte432', hint: 'SMPTE 432 / Display P3 原色，常见于 P3-D65 显示和部分 Apple 生态内容。' },
                { value: 'film', label: 'film', hint: '胶片相关原色，主要用于历史或特殊素材。' },
                { value: 'bt470m', label: 'bt470m', hint: 'BT.470M 原色，旧 NTSC 1953 相关标准。' },
                { value: 'bt470bg', label: 'bt470bg', hint: 'BT.470BG 原色，旧 PAL/SECAM 625 行内容常见。' },
                { value: 'smpte170m', label: 'smpte170m', hint: 'SMPTE 170M 原色，NTSC/BT.601 525 行 SD 视频常见。' },
                { value: 'smpte240m', label: 'smpte240m', hint: 'SMPTE 240M 原色，早期 HDTV 标准，现代内容较少使用。' },
                { value: 'jedec-p22', label: 'jedec-p22', hint: 'JEDEC P22 荧光粉原色，旧 CRT/显示标准相关。' },
                { value: 'ebu3213', label: 'ebu3213', hint: 'EBU Tech 3213 原色，欧洲广播旧标准相关。' },
              ],
            },
          },
          {
            path: '视频参数_色彩管理_传输特性', label: '传输特性', inline: true,
            hint: '描述数值与实际光亮度之间的非线性关系',
            type: {
              kind: 'select', width: 175, placeholder: 'color_trc',
              options: [
                { value: '', label: '' },
                { value: 'auto', label: 'auto', hint: '自动或沿用输入；当前生成命令时会视作不显式指定。' },
                { value: 'bt709', label: 'bt709', hint: 'BT.709 SDR 传输特性，HD SDR 视频最常见。' },
                { value: 'bt2020-10', label: 'bt2020-10', hint: 'BT.2020 10-bit 传输特性，不等同于 HDR PQ/HLG；通常用于 BT.2020 SDR 或兼容流程。' },
                { value: 'bt2020-12', label: 'bt2020-12', hint: 'BT.2020 12-bit 传输特性，不等同于 HDR PQ/HLG；用于 12-bit BT.2020 流程。' },
                { value: 'smpte2084', label: 'smpte2084', hint: 'SMPTE ST 2084 PQ，HDR10、HDR10+、Dolby Vision 基础层常用。通常搭配 bt2020、bt2020nc、tv 和 10-bit 像素格式。' },
                { value: 'bt470m', label: 'bt470m', hint: 'BT.470M 传输特性，旧 NTSC 相关标准。' },
                { value: 'bt470bg', label: 'bt470bg', hint: 'BT.470BG 传输特性，旧 PAL/SECAM 相关标准。' },
                { value: 'log', label: 'log', hint: '对数传输曲线，主要用于特殊中间流程或旧素材；普通成片很少直接使用。' },
                { value: 'log_sqrt', label: 'log_sqrt', hint: '平方根对数传输曲线，特殊或旧式流程使用，普通成片很少直接使用。' },
                { value: 'linear', label: 'linear', hint: '线性光传输。常作为色调映射或合成的中间状态，直接输出给普通播放器通常不合适。' },
                { value: 'bt1361e', label: 'bt1361e', hint: 'BT.1361 扩展色域传输特性，历史和兼容性用途为主。' },
                { value: 'iec61966-2-1', label: 'iec61966-2-1', hint: 'sRGB 传输特性，常见于图片、网页、桌面图形和部分全范围 RGB 流程。' },
                { value: 'iec61966-2-4', label: 'iec61966-2-4', hint: 'IEC 61966-2-4 / xvYCC 相关传输特性，普通视频输出较少使用。' },
                { value: 'smpte170m', label: 'smpte170m', hint: 'SMPTE 170M 传输特性，NTSC/BT.601 SD 视频常见。' },
                { value: 'smpte240m', label: 'smpte240m', hint: 'SMPTE 240M 传输特性，早期 HDTV 标准，现代内容较少使用。' },
                { value: 'gamma22', label: 'gamma22', hint: '固定 2.2 gamma，常见于显示和旧素材假设，但不是标准 HDR 曲线。' },
                { value: 'gamma28', label: 'gamma28', hint: '固定 2.8 gamma，常见于旧 PAL/SECAM 相关假设。' },
                { value: 'arib-std-b67', label: 'arib-std-b67', hint: 'ARIB STD-B67 HLG，广播 HDR 常用。通常搭配 bt2020、bt2020nc、tv 和 10-bit 像素格式。' },
              ],
            },
          },
          {
            path: '视频参数_色彩管理_范围', label: '色彩范围', inline: true,
            hint: '实际上大多数视频是有限范围而不是完全范围',
            type: {
              kind: 'select', width: 175, placeholder: 'color_range',
              options: [
                { value: '', label: '' },
                { value: 'tv 有限 16~235', label: 'tv 有限 16~235', hint: '有限范围，也称 TV/MPEG/studio range。大多数 SDR、HDR10、HLG 视频都使用此范围。' },
                { value: 'pc 全范围 0~255', label: 'pc 全范围 0~255', hint: '全范围，也称 PC/JPEG/full range。常见于 RGB、截图、部分相机或中间文件；误用会导致黑位和白位错误。' },
              ],
            },
          },
          {
            path: '视频参数_色彩管理_色调映射算法', label: '色调映射算法', inline: true,
            hint: '可选   仅限 libplacebo 使用',
            type: {
              kind: 'select', width: 175, placeholder: 'tonemapping',
              options: [
                { value: '', label: '' },
                { value: 'auto', label: 'auto', hint: '由 libplacebo 根据内部启发式自动选择算法，适合不知道该选哪一个时使用。' },
                { value: 'clip', label: 'clip', hint: '不做真正的色调映射，只裁剪超出目标范围的亮度和颜色。速度快，但高光信息会直接丢失。' },
                { value: 'st2094-40', label: 'st2094-40', hint: 'SMPTE ST 2094-40 EETF，面向 HDR10+ 动态元数据的曲线。需要源信息准确才有意义。' },
                { value: 'st2094-10', label: 'st2094-10', hint: 'SMPTE ST 2094-10 EETF，会考虑平均亮度和最大最小亮度，适合有相关元数据的 HDR 内容。' },
                { value: 'bt.2390', label: 'bt.2390', hint: 'ITU-R BT.2390 推荐的高光 roll-off 曲线，常用于 HDR 映射到较低峰值显示或 SDR。' },
                { value: 'bt.2446a', label: 'bt.2446a', hint: 'ITU-R BT.2446 方法 A，面向制作良好的 HDR 源，可用于正向和反向色调映射。' },
                { value: 'spline', label: 'spline', hint: '简单样条曲线，在 PQ 空间使用一个枢轴点压缩亮度；可用于正向和反向色调映射。' },
                { value: 'reinhard', label: 'reinhard', hint: '经典全局非线性色调映射，结果稳定但可能压低整体对比度。' },
                { value: 'mobius', label: 'mobius', hint: 'Reinhard 的改进形式，暗部附近保留线性段，默认设置在色彩准确性和高光保留之间较均衡。' },
                { value: 'hable', label: 'hable', hint: '电影感曲线，暗部和高光细节保留较好，但会明显改变平均亮度和观感。' },
                { value: 'gamma', label: 'gamma', hint: '用幂函数拟合源和目标范围，细节保留较稳，但画面可能显得偏灰或不够鲜亮。' },
                { value: 'linear', label: 'linear', hint: '在 PQ 空间线性拉伸输入到输出范围，细节保留直接，但平均亮度变化可能很明显。' },
              ],
            },
          },
          {
            path: '视频参数_色彩管理_处理方式', label: '操作方式', inline: true,
            hint: '对于标准的转换操作应该选择 写入元数据并转换',
            type: {
              kind: 'select', width: 175, placeholder: '选择操作方式',
              options: [
                { value: '', label: '' },
                { value: '写入元数据并转换', label: '写入元数据并转换', hint: '同时生成转换滤镜和输出流色彩元数据。标准转色彩空间时推荐此项，播放器更容易按目标标准识别。' },
                { value: '仅写入元数据', label: '仅写入元数据', hint: '只写 -colorspace、-color_primaries、-color_trc、-color_range，不改变画面数值。适合修正缺失或错误标签。' },
                { value: '仅转换', label: '仅转换', hint: '只生成转换滤镜，不写输出流色彩元数据。适合后续参数或封装流程另行处理标签的情况。' },
              ],
            },
          },
        ],
      },
      {
        title: '简易调色',
        hint: '高级调色去用达芬奇，勾选才会使用',
        sliders: [
          { enablePath: '视频参数_色彩管理_启用调整亮度', valuePath: '视频参数_色彩管理_亮度', label: '亮度', min: -1, max: 1, step: 0.1, def: '0', decimals: 1, topLabel: '最亮', bottomLabel: '最暗', tone: 'red' },
          { enablePath: '视频参数_色彩管理_启用调整对比度', valuePath: '视频参数_色彩管理_对比度', label: '对比度', min: 0, max: 2, step: 0.1, def: '1', decimals: 1, topLabel: '最高', bottomLabel: '原点', tone: 'green' },
          { enablePath: '视频参数_色彩管理_启用调整饱和度', valuePath: '视频参数_色彩管理_饱和度', label: '饱和度', min: 0, max: 3, step: 0.1, def: '1', decimals: 1, topLabel: '最高', bottomLabel: '原点', tone: 'blue' },
          { enablePath: '视频参数_色彩管理_启用调整伽马', valuePath: '视频参数_色彩管理_伽马', label: '伽马', min: 0.1, max: 10, step: 0.1, def: '1', decimals: 1, topLabel: '最亮', bottomLabel: '最暗', tone: 'purple' },
        ],
        fields: [],
      },
    ],
  },
  {
    id: 'audio',
    title: '音频参数',
    kind: 'fields',
    sections: [
      {
        title: '编码',
        fields: [
          { path: '音频参数_编码器_代号', label: '编码器', type: { kind: 'select', options: [] } },
          { path: '音频参数_比特率', label: '比特率', type: { kind: 'text' }, placeholder: '如 192k' },
          { path: '音频参数_质量参数名', label: '质量参数名', type: { kind: 'text' }, placeholder: '如 -q:a 的 q:a' },
          { path: '音频参数_质量值', label: '质量值', type: { kind: 'text' } },
          { path: '音频参数_质量参数名2', label: '质量参数名 2', type: { kind: 'text' } },
          { path: '音频参数_质量值2', label: '质量值 2', type: { kind: 'text' } },
          { path: '音频参数_声道数', label: '声道数', type: { kind: 'text' } },
          { path: '音频参数_位深度', label: '位深度', type: { kind: 'text' } },
          { path: '音频参数_采样率', label: '采样率', type: { kind: 'text' } },
        ],
      },
      {
        title: '响度标准化',
        hint: '歌曲细节少可适当拉响，电影细节多需要放静并提升动态；勾选上才有效',
        sliders: [
          {
            enablePath: '音频参数_响度标准化_启用调整目标响度', valuePath: '音频参数_响度标准化_目标响度',
            label: '目标响度', unit: 'LUFS', min: -36, max: -8, step: 1, def: '-24', decimals: 0, tone: 'red',
            marks: [
              { value: -8, label: '3FUI 最响允许' },
              { value: -12, label: '最响建议 -12' },
              { value: -16, label: '-16 综合推荐' },
              { value: -23, label: '-23 国际标准' },
              { value: -24, label: '我国标准 -24' },
              { value: -36, label: '3FUI 最静允许' },
            ],
          },
          {
            enablePath: '音频参数_响度标准化_启用调整动态范围', valuePath: '音频参数_响度标准化_动态范围',
            label: '动态范围', unit: 'LU', min: 1, max: 40, step: 1, def: '1', decimals: 0, tone: 'green',
            marks: [
              { value: 40, label: '3FUI 允许最大' },
              { value: 25, label: '20~30 星际穿越' },
              { value: 12, label: '10~15 常规内容推荐' },
            ],
          },
          {
            enablePath: '音频参数_响度标准化_启用调整峰值电平', valuePath: '音频参数_响度标准化_峰值电平',
            label: '峰值电平', unit: 'dBTP', min: -5, max: 0, step: 0.5, def: '-1', decimals: 1, tone: 'blue',
            marks: [
              { value: 0, label: '理论最佳' },
              { value: -1, label: '最常用 -1' },
              { value: -5, label: '失真严重' },
            ],
          },
        ],
        fields: [],
      },
    ],
  },
  {
    id: 'cut',
    title: '剪辑区间',
    kind: 'fields',
    sections: [
      {
        fields: [
          { path: '剪辑区间_方法', label: '剪辑方法', type: { kind: 'select', options: CUT } },
          { path: '剪辑区间_入点', label: '入点', type: { kind: 'text' }, placeholder: '如 00:01:00 或 60' },
          { path: '剪辑区间_出点', label: '出点', type: { kind: 'text' } },
          { path: '剪辑区间_向前解码多久秒', label: '向前解码（秒）', type: { kind: 'text' } },
        ],
      },
    ],
  },
  {
    id: 'filterorder',
    title: '滤镜排序',
    kind: 'filterorder',
    hint: '调整各滤镜在滤镜图中的先后顺序，可插入自定义滤镜',
    sections: [],
  },
  {
    id: 'custom',
    title: '自定义参数',
    kind: 'custom',
    sections: [
      {
        title: '自定义滤镜与参数',
        fields: [
          { path: '自定义参数_视频滤镜', label: '自定义视频滤镜', type: { kind: 'textarea', rows: 2 }, full: true },
          { path: '自定义参数_音频滤镜', label: '自定义音频滤镜', type: { kind: 'textarea', rows: 2 }, full: true },
          { path: '自定义参数_视频参数', label: '自定义视频参数', type: { kind: 'text' }, full: true },
          { path: '自定义参数_音频参数', label: '自定义音频参数', type: { kind: 'text' }, full: true },
        ],
      },
      {
        title: '参数插入位置',
        fields: [
          { path: '自定义参数_开头参数', label: '开头参数', type: { kind: 'text' }, full: true },
          { path: '自定义参数_之前参数', label: '之前参数（输入后）', type: { kind: 'text' }, full: true },
          { path: '自定义参数_之后参数', label: '之后参数', type: { kind: 'text' }, full: true },
          { path: '自定义参数_最后参数', label: '最后参数', type: { kind: 'text' }, full: true },
          { path: '自定义参数_完全自己写', label: '完全自己写（取代以上全部参数）', type: { kind: 'textarea', rows: 4 }, full: true },
        ],
      },
    ],
  },
  {
    id: 'streams',
    title: '流控制',
    kind: 'fields',
    sections: [
      {
        title: '流保留与指定',
        hints: ['推荐使用可视化流选择器来快速填写下面三个文本框'],
        buttons: [{ text: '可视化流选择器', dialog: 'streamPicker' }],
        fields: [
          {
            path: '流控制_将视频参数应用于指定流', label: '将 视频参数 应用于哪些文件和流 (v)', highlight: '视频参数', labelTone: 'green',
            hint: '格式：文件索引:v:流索引，例如：0:v 表示第一个文件的全部视频流', hintTone: 'blue',
            type: { kind: 'stringlist' }, full: true,
          },
          { path: '流控制_启用保留其他视频流', label: '然后保留其他视频流', type: { kind: 'bool' } },
          {
            path: '流控制_将音频参数应用于指定流', label: '将 音频参数 应用于哪些文件和流 (a)', highlight: '音频参数', labelTone: 'gold',
            hint: '格式：文件索引:a:流索引，例如：0:a 表示第一个文件的全部音频流', hintTone: 'blue',
            type: { kind: 'stringlist' }, full: true,
          },
          { path: '流控制_启用保留其他音频流', label: '然后保留其他音频流', type: { kind: 'bool' } },
          {
            path: '流控制_将字幕参数应用于指定流', label: '使用哪些文件的哪些 字幕 (s)', highlight: '字幕', labelTone: 'blue',
            hint: '格式：文件索引:s:流索引，例如：0:s 表示第一个文件的全部字幕流', hintTone: 'blue',
            type: { kind: 'stringlist' }, full: true,
          },
          { path: '流控制_如何操作指定的字幕', label: '如何操作', type: { kind: 'select', options: SUBTITLE_OP } },
          { path: '流控制_启用保留其他字幕流', label: '然后保留其他字幕流', type: { kind: 'bool' } },
        ],
      },
      {
        title: '混流同名字幕',
        hints: ['mp4 仅支持 mov_text 字幕'],
        fields: [
          { path: '流控制_自动混流SRT', label: 'SRT', type: { kind: 'bool' } },
          { path: '流控制_自动混流ASS', label: 'ASS', type: { kind: 'bool' } },
          { path: '流控制_自动混流SSA', label: 'SSA', type: { kind: 'bool' } },
          { path: '流控制_自动混流的字幕转为MOVTEXT', label: '转为 mov_text', type: { kind: 'bool' } },
        ],
      },
      {
        title: '元数据 # 章节 # 附件',
        hints: ['这些功能仅应用于首个 -i 的文件'],
        fields: [
          { path: '流控制_元数据选项', label: '元数据选项', type: { kind: 'select', options: META_OP } },
          { path: '流控制_章节选项', label: '清除章节', type: { kind: 'select', options: CHAPTER_OP } },
          { path: '流控制_附件选项', label: '附件选项', type: { kind: 'select', options: ATTACH_OP } },
        ],
      },
      {
        hints: [
          { text: 'ffmpeg 的 -map 参数具有很高的优先级：使用其指定流参数时，其他类型的流也必须带上 -map，否则 ffmpeg 可能会丢弃流', tone: 'gold' },
          { text: '这些功能强制使用 -map，因为无法在一般情况下处理这些需求，注意对其他类的流使用 -map，也就是填写上面的文本框', tone: 'green' },
          { text: '必须指定了具体的流才可以使用「然后保留其他流」，否则会发生意外情况；当同时指定流和保留其他流时，可能有部分参数与该逻辑不兼容', tone: 'red' },
        ],
        fields: [],
      },
    ],
  },
  {
    id: 'extra',
    title: '附加内容',
    kind: 'extras',
    hint: '章节文件、元数据与附件的附加来源',
    sections: [
      {
        title: '章节',
        fields: [
          { path: '章节_来源', label: '章节来源', type: { kind: 'select', options: CHAPTER_SOURCE } },
          { path: '章节_文件路径', label: '章节文件路径', type: { kind: 'text' }, full: true },
        ],
      },
      {
        title: '预设备注',
        fields: [
          { path: '预设备注', label: '预设备注', type: { kind: 'textarea', rows: 3 }, full: true, hint: '备注会在鼠标移到预设上时显示在侧边' },
        ],
      },
    ],
  },
  {
    id: 'frameserver',
    title: '视频帧服务器',
    kind: 'fields',
    hint: '通过 AviSynth / VapourSynth 脚本作为视频源（Docker 内未包含对应运行时）',
    sections: [
      {
        fields: [
          { path: '视频参数_视频帧服务器_使用AviSynth', label: '使用 AviSynth', type: { kind: 'bool' } },
          { path: '视频参数_视频帧服务器_avs脚本文件', label: 'AVS 脚本文件', type: { kind: 'text' }, full: true },
          { path: '视频参数_视频帧服务器_使用VapourSynth', label: '使用 VapourSynth', type: { kind: 'bool' } },
          { path: '视频参数_视频帧服务器_vpy脚本文件', label: 'VPY 脚本文件', type: { kind: 'text' }, full: true },
        ],
      },
    ],
  },
]

export const GROUPS = PAGES

/** 二级菜单条目（含分隔线），顺序与原版一致 */
export const SUB_NAV: SubNavEntry[] = (() => {
  const withSep: string[][] = [
    ['overview', 'presets'],
    ['output', 'decode'],
    ['vcodec', 'frame', 'quality', 'color'],
    ['audio'],
    ['cut', 'filterorder', 'custom', 'streams'],
    ['extra', 'frameserver'],
  ]
  const entries: SubNavEntry[] = []
  withSep.forEach((group, index) => {
    if (index > 0) entries.push({ type: 'sep' })
    for (const id of group) {
      const page = PAGES.find(p => p.id === id)
      if (page) entries.push({ type: 'item', id: page.id, label: page.title })
    }
  })
  return entries
})()

export function findGroup(id: string): GroupDef | undefined {
  return PAGES.find(page => page.id === id)
}

/** 默认音频编码器：核心侧音频编码器数据库里的「复制流」（命令行 -c:a copy） */
export const 默认音频编码器 = 'audio.copy'

export function newPreset(): PresetData {
  const preset: PresetData = { 预设文件版本: 6 }
  const 补默认值 = (field: FieldDef) => {
    const key = field.path
    // 嵌套路径（如 视频参数_超分_直接面板.目标宽度）由下面的固定结构补，不当作扁平键写入
    if (!key || key.includes('.')) return
    if (field.type.kind === 'bool') (preset as Record<string, unknown>)[key] = false
    else if (field.type.kind === 'stringlist') (preset as Record<string, unknown>)[key] = []
    else if (field.type.kind === 'number') (preset as Record<string, unknown>)[key] = 0
    // 下拉一律写空串（= 未选择，控件显示灰色占位符）。绝不能取 options[0]：
    // JS 对象里 '15' / '48' / '120' 这类数字键会被提到最前，于是「首项」常常是真实值，
    // 会被当成默认值写进预设，用户没调过也会生效（帧率被写成 15 → -r:v:0 15，
    // NV FRUC 目标帧率被写成 48 → -filter:v "fruc_vulkan=fps=48"）。
    else if (field.type.kind === 'select') (preset as Record<string, unknown>)[key] = ''
    else (preset as Record<string, unknown>)[key] = ''
  }
  const 补滑杆默认值 = (slider: SliderDef) => {
    // 与曾有的 bool+text 字段对输出完全一致：enablePath→false、valuePath→''（键集合不变，v4 缓存/导出兼容）
    ;(preset as Record<string, unknown>)[slider.enablePath] = false
    ;(preset as Record<string, unknown>)[slider.valuePath] = ''
  }
  for (const group of PAGES) {
    for (const section of group.sections) {
      for (const slider of section.sliders ?? []) 补滑杆默认值(slider)
      for (const field of section.fields) 补默认值(field)
    }
    for (const dialog of group.dialogs ?? []) {
      for (const field of dialog.fields) 补默认值(field)
    }
  }
  preset['视频参数_烧录字幕_字幕格式优先级'] = []
  preset['元数据_要写入的信息'] = []
  preset['附件_要写入的附件'] = []
  preset['视频参数_超分_滤镜叠加策略组'] = []
  preset['视频参数_超分_直接面板'] = { 目标宽度: '', 目标高度: '', 上采样算法: '', 下采样算法: '', 抗振铃强度: '', 着色器文件路径: '' }
  preset['滤镜排序系统'] = []
  for (const name of ['视频参数_烧录字幕_主要颜色', '视频参数_烧录字幕_次要颜色', '视频参数_烧录字幕_描边颜色', '视频参数_烧录字幕_背景颜色']) {
    preset[name] = { 已设置: false, A: 255, R: 0, G: 0, B: 0 }
  }
  // 音频：默认「复制流」（-c:a copy）。这是唯一一处刻意非空的下拉默认值——
  // 核心侧只有 音频参数_编码器_代号 / 音频滤镜 / 音频附加参数 任一非空才会生成 -map 0:a:0?，
  // 全部留空会直接丢掉音轨，与「默认保证视频有声音」相反；复制流不写任何音频编码参数。
  preset['音频参数_编码器_代号'] = 默认音频编码器
  // 自动命名：默认「附加_递增时间戳」（原版 预设数据_v6.vb:32 的枚举默认值）。
  // 留空会被后端当「不使用自动命名」→ 输出文件 = 输入文件同名，ffmpeg 原地覆盖直接报错。
  preset['输出_自动命名选项'] = '附加_递增时间戳'
  // 「源视频有什么就保留什么」的默认策略（NAS 批量转码的主要诉求）：
  // 全音轨 copy（-map 0:a? -c:a copy，不只是第一条）、全字幕 copy、元数据/章节/附件保留（刮削不受影响）。
  // 分辨率/帧率/色彩/滤镜默认全空 = 跟随源视频。图片类编码器由核心自动跳过音频/字幕（见滤镜图门控）。
  preset['流控制_启用保留其他音频流'] = true
  preset['流控制_启用保留其他字幕流'] = true
  preset['流控制_元数据选项'] = '保留元数据'
  preset['流控制_章节选项'] = '保留章节'
  preset['流控制_附件选项'] = '保留附件'
  return preset
}

export function getByPath(obj: Record<string, unknown>, path: string): unknown {
  let current: unknown = obj
  for (const part of path.split('.')) {
    if (current === null || current === undefined) return undefined
    current = (current as Record<string, unknown>)[part]
  }
  return current
}

export function setByPath(obj: Record<string, unknown>, path: string, value: unknown): void {
  const parts = path.split('.')
  let current: Record<string, unknown> = obj
  for (let i = 0; i < parts.length - 1; i++) {
    if (typeof current[parts[i]] !== 'object' || current[parts[i]] === null) current[parts[i]] = {}
    current = current[parts[i]] as Record<string, unknown>
  }
  current[parts[parts.length - 1]] = value
}
