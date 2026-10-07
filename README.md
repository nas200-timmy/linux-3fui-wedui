# linux-3fui

FFmpegFreeUI（3FUI）的 **Linux / 网页移植版**：为 NAS 打造的批量转码服务。

- **功能一致性**：核心逻辑（预设数据模型、ffmpeg 命令行生成、编码队列、进度解析）直接移植自 3FUI v6.2.26 源码（VB.NET），经 112 项上游回归测试在 Linux 上验证通过
- **预设互通**：`.3fui` 预设文件与 Windows 版 **双向兼容**——Windows 上保存的预设直接导入使用，反之亦然
- **网页前端**：复刻 3FUI v6 暗色毛玻璃风格（起始页/准备文件/参数面板/编码队列/Agent/设置）
- **声明式 HTTPS**：网页上传证书立即生效（无需重启容器）；删除即停用；证书持久化于数据卷
- **远程调用**：UDP 协议与 Windows 版 3FUI 一致（默认端口 10591），局域网自动化零改动迁移。⚠️ 该协议**无认证**（协议兼容所致，可下发任意 ffmpeg 参数）——只在可信局域网开启「设置 → 监听端口」，公网环境务必启用登录认证且防火墙不放行 10591/udp
- **Agent**：接入任意 OpenAI SDK 兼容端点（DeepSeek/Kimi/OpenAI/Ollama…），流式输出，密钥仅存服务端；
  「模型管理」内置 models.dev 厂商目录（国内知名 → 国外知名 → 其他，带搜索），选厂商自动填端点，
  并可一键扫描端点自身的 `/models` 拉取该 Key 下真实可用的模型；
  **工具化**：Agent 能真正操作软件本体——读/改参数面板、管理预设、查看与控制编码队列、读任务日志、探测媒体文件，
  按三档权限（安全区域 / 环境控制 / 系统访问）放行，**写操作逐次弹确认卡**，可随时停止

## 快速开始

### Docker（推荐，NAS 通用）

```bash
# 1. 修改 docker-compose.yml 中的媒体目录挂载
# 2. 环境检查 + 构建 + 启动 + 硬件验证一条龙（amd64 与 arm64 均可）
./tools/deploy.sh --build

# 3. 打开
#    http://NAS的IP:8080    （上传证书后自动跳转 https://NAS的IP:8443）
```

`deploy.sh` 部署前自动检查并尽量修复：GPU 清点、NVIDIA runtime、`group_add` 与宿主
video/render 组一致性（不符时自动改写 compose）、媒体目录属主、端口占用；部署后自动跑
编码器探针矩阵（`tools/check-hw.sh`），任何一层不通会给出具体修复命令。
裸用 `docker compose up -d --build` 亦可，但没有上述检查。

首次使用：
1. 「设置」→ 上传证书（cert.pem + key.pem）启用 HTTPS
2. 「准备文件」浏览 `/media` 选择待转码文件
3. 「参数面板」→「预设管理」→ 导入 Windows 版导出的 `.3fui` 预设（可选）
4. 「参数面板」→「添加到队列」→「编码队列」监控进度

### 二进制（本地 Linux 直接运行）

```bash
# 自包含单文件（无需安装 .NET），需系统有 ffmpeg
curl -LO https://github.com/<你的仓库>/releases/latest/download/3fui-server-linux-x64
chmod +x 3fui-server-linux-x64
DATA_DIR=./data HTTP_PORT=8080 HTTPS_PORT=8443 MEDIA_ROOT=/media ./3fui-server-linux-x64
```

### 开发构建

```bash
# 需要 .NET SDK 10 与 Node.js 22
cd src/3fui-web && npm install && npm run build   # 前端产物输出到 ../3fui-server/wwwroot
cd ../3fui-server && dotnet run                   # http://127.0.0.1:8080

# 核心回归测试（命令行生成与 Windows 版一致性证明）
cd tests/3fui-core.Tests && dotnet run -- --ffmpeg /usr/bin/ffmpeg
```

## 环境变量

| 变量 | 默认 | 说明 |
|------|------|------|
| `DATA_DIR` | `./data` | 数据目录（设置/预设/队列缓存/HTTPS 证书） |
| `HTTP_PORT` | `8080` | HTTP 端口（TLS 启用后 301 跳转 HTTPS） |
| `HTTPS_PORT` | `8443` | HTTPS 端口（恒常监听，证书就绪即握手成功） |
| `MEDIA_ROOT` | `/media`（存在时） | 媒体根目录，网页文件浏览限制在此范围内 |
| `TLS_CERT_PATH` | — | 声明式 HTTPS：证书文件路径（优先级高于网页上传） |
| `TLS_KEY_PATH` | — | 声明式 HTTPS：私钥文件路径 |

## 硬件加速（NAS 转码必读）

Docker 内自带 Debian 官方 ffmpeg（含 VAAPI/QSV）。使用硬件编码时：

- **Intel QSV / AMD VAAPI**：compose 中取消注释 `devices: - /dev/dri:/dev/dri`
- **NVIDIA NVENC**：宿主机安装 NVIDIA Container Toolkit，compose 中取消注释 `runtime: nvidia`
- **arm64 NAS**：镜像支持 amd64/arm64 双架构构建；Intel 专属驱动（oneVPL/iHD/libmfx）仅 amd64 安装，arm64 下 QSV 不可用属预期，VAAPI 由 mesa 通用驱动提供

编码器参数面板沿用 Windows 版编码器数据库（参数生成完全一致），可用性取决于镜像内 ffmpeg 的编译选项；也可通过「设置 → 替代进程文件名」挂载宿主机 ffmpeg 包装脚本。

## 与 Windows 版 3FUI 的差异

| 项目 | Windows 3FUI | linux-3fui |
|------|-------------|------------|
| UI | WinForms + LakeUI（DirectX 11） | Web（Vue 3，风格复刻，非像素级一致） |
| 预设文件 | `.3fui`（JSON） | **相同格式，双向兼容** |
| 队列缓存 | `QueuePendingTasks_v6.json` | 相同文件名与结构 |
| 设置文件 | `Settings.json` | 字段名兼容（UI 专属字段省略） |
| 远程调用 | UDP 10591 | 相同协议与端口 |
| 暂停/恢复 | NtSuspendProcess | SIGSTOP / SIGCONT（行为等价） |
| 硬件监控 | LibreHardwareMonitor | /proc + nvidia-smi |
| 性能 | 原生 | 核心为原生 .NET；UI 为网页 |
| 插件系统 / ffplay 预览 / AviSynth | 支持 | 暂未移植（规划中） |

## 目录结构

```
src/3fui-core/    VB.NET 核心库：上游纯逻辑逐文件移植 + 少量适配
  └─ 预设系统/    预设模型与命令行生成（与上游同名同内容）
  └─ 引擎/        编码任务/队列/UDP（去除 WinForms/LakeUI 依赖的适配版）
src/3fui-server/  ASP.NET Core 服务端（REST/WebSocket/TLS/Agent/UDP）
src/3fui-web/     Vue 3 前端（构建产物输出到 server/wwwroot）
tests/            上游回归测试移植（一致性证明）
Dockerfile        多阶段构建（node → dotnet sdk → debian-slim + ffmpeg）
docker-compose.yml
```

## 许可证

- 本项目本体：MIT
- 移植自上游 [FFmpegFreeUI](https://github.com/Lake1059/FFmpegFreeUI)（MIT，v6.2.26 基线），中文标识符与文件结构保留以保持 diff 可追踪
- **不包含任何 GPL 的 LakeUI 代码**（上游 UI 依赖 LakeUI，移植时已全部剥离）
