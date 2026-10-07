#!/usr/bin/env bash
# linux-3fui 部署闸：宿主环境检查 → 拉起容器 → 容器内硬件/API 验证。
# 解决三类跨机器部署翻车：
#   1. 镜像/编码器缺失（构建后 ffmpeg 缺硬编）——部署后探针矩阵兜底
#   2. 硬件未挂载（无 /dev/dri、缺 NVIDIA runtime）——部署前清点
#   3. 用户组错位（compose group_add 与宿主 video/render GID 不一致）——自动比对并改写
# 用法：
#   ./tools/deploy.sh              # 检查 + up -d + 验证
#   ./tools/deploy.sh --build      # 先构建再部署（首次/改代码后）
#   ./tools/deploy.sh --yes        # 自动修正 compose 时不询问
# 退出码：0 全部通过；1 存在硬失败（看汇总）。
set -u
cd "$(dirname "$0")/.."

BUILD=0; ASSUME_YES=0
for arg in "$@"; do
    case "$arg" in
        --build) BUILD=1 ;;
        --yes|-y) ASSUME_YES=1 ;;
        *) echo "未知参数：$arg（支持 --build / --yes）"; exit 1 ;;
    esac
done

say()  { printf '%s\n' "$*"; }
pass() { printf '  \033[32mPASS\033[0m  %s\n' "$*"; }
warn() { printf '  \033[33mWARN\033[0m  %s\n' "$*"; }
fail() { printf '  \033[31mFAIL\033[0m  %s\n' "$*"; }
step() { printf '\n== %s ==\n' "$*"; }

FAILED=0
note_fail() { fail "$*"; FAILED=1; }

# 从 compose 渲染结果读取服务配置（group_add / devices / volumes）
compose_json() {
    docker compose config --format json 2>/dev/null
}

step "0. 前置：docker 可用性与镜像存在性"
if ! command -v docker >/dev/null; then
    note_fail "docker 命令不存在（先安装 docker.io 与 docker-compose）"
    exit 1
fi
if ! docker info >/dev/null 2>&1; then
    note_fail "docker 守护进程不可用（sudo systemctl enable --now docker；当前用户需加入 docker 组后重新登录）"
    exit 1
fi
pass "docker 守护进程正常"

if [ "$BUILD" = 0 ]; then
    if ! docker image inspect linux-3fui-2:latest >/dev/null 2>&1; then
        note_fail "本地镜像 linux-3fui-2:latest 不存在——首次部署请加 --build：./tools/deploy.sh --build"
    else
        pass "镜像 linux-3fui-2:latest 存在"
    fi
fi

step "1. 宿主 GPU 清点（lspci，失败则退化为 /dev/dri 探测）"
gpu_list=""
if command -v lspci >/dev/null; then
    gpu_list="$(lspci 2>/dev/null | grep -iE 'vga compatible|3d controller' || true)"
fi
if [ -z "$gpu_list" ]; then
    if [ -d /dev/dri ]; then gpu_list="(lspci 不可用，/dev/dri 存在，假定有 GPU)"; else gpu_list=""; fi
fi
if [ -z "$gpu_list" ]; then
    warn "未发现 GPU：容器将以纯 CPU 转码运行（硬编探测会全部不可用，属预期）"
else
    say "$gpu_list" | sed 's/^/  /'
fi

HAS_NVIDIA=0
echo "$gpu_list" | grep -qi nvidia && HAS_NVIDIA=1
[ -e /dev/nvidia0 ] && HAS_NVIDIA=1

step "2. NVIDIA runtime（有 N 卡才检查）"
if [ "$HAS_NVIDIA" = 1 ]; then
    if docker info 2>/dev/null | grep -q "nvidia"; then
        pass "docker 已注册 nvidia runtime"
    else
        note_fail "检测到 NVIDIA 显卡但 docker 无 nvidia runtime。
        修复：安装 NVIDIA Container Toolkit 并注册运行时——
          curl -fsSL https://nvidia.github.io/libnvidia-container/gpgkey | sudo gpg --dearmor -o /usr/share/keyrings/nvidia-container-toolkit-keyring.gpg
          curl -fsSL https://nvidia.github.io/libnvidia-container/stable/deb/nvidia-container-toolkit.list | sed 's#deb https://#deb [signed-by=/usr/share/keyrings/nvidia-container-toolkit-keyring.gpg] https://#g' | sudo tee /etc/apt/sources.list.d/nvidia-container-toolkit.list
          sudo apt-get update && sudo apt-get install -y nvidia-container-toolkit
          sudo nvidia-ctk runtime configure --runtime=docker && sudo systemctl restart docker"
    fi
else
    say "  无 NVIDIA 显卡，跳过（compose 的 runtime: nvidia 在纯 Intel 机器上会启动失败——见步骤 4 提示）"
fi

step "3. /dev/dri 与容器组匹配（用户组错位自动修正）"
VIDEO_GID="$(getent group video  | cut -d: -f3)"
RENDER_GID="$(getent group render | cut -d: -f3)"
if [ ! -d /dev/dri ]; then
    if [ "$HAS_NVIDIA" = 1 ]; then
        warn "/dev/dri 不存在：Intel 核显不可用（容器依赖的 /dev/dri 透传会失败）；NVIDIA 路径不受影响"
    else
        warn "/dev/dri 不存在：硬编透传不可用，纯 CPU 模式"
    fi
else
    pass "/dev/dri 存在（video=${VIDEO_GID:-?} render=${RENDER_GID:-?}）"
    if [ -z "$RENDER_GID" ]; then
        note_fail "宿主存在 /dev/dri 但没有 render 组——设备节点属主异常，请检查 udev 规则"
    fi
fi

# 组比对与自动修正
if [ -n "$RENDER_GID" ] && [ -d /dev/dri ] && command -v python3 >/dev/null; then
    cur_groups="$(compose_json | python3 -c "
import json,sys
try:
    cfg=json.load(sys.stdin)
    svc=cfg['services'].get('linux-3fui-2',{})
    print(' '.join(svc.get('group_add') or []))
except Exception:
    print('')
" 2>/dev/null)"
    if [ -z "$cur_groups" ]; then
        warn "compose 未配置 group_add——容器内可能无权访问 /dev/dri 设备节点"
    else
        want_groups="${VIDEO_GID:-44} ${RENDER_GID}"
        if [ "$cur_groups" = "$want_groups" ]; then
            pass "compose group_add=[$cur_groups] 与宿主一致"
        else
            warn "compose group_add=[$cur_groups] 与宿主不符（应为 [$want_groups]）"
            do_fix=1
            if [ "$ASSUME_YES" != 1 ]; then
                printf '  是否自动改写 docker-compose.yml 的 group_add？[y/N] '
                read -r ans
                case "$ans" in y|Y|yes|YES) do_fix=1 ;; *) do_fix=0 ;; esac
            fi
            if [ "$do_fix" = 1 ]; then
                sed -i -E \
                    -e "s|^([[:space:]]*- \")[0-9]+(\"[[:space:]]*# 宿主 video 组.*)$|\1${VIDEO_GID:-44}\2|" \
                    -e "s|^([[:space:]]*- \")[0-9]+(\"[[:space:]]*# 宿主 render 组.*)$|\1${RENDER_GID}\2|" \
                    docker-compose.yml
                new_groups="$(compose_json | python3 -c "import json,sys;print(' '.join(json.load(sys.stdin)['services']['linux-3fui-2'].get('group_add') or []))" 2>/dev/null)"
                if [ "$new_groups" = "$want_groups" ]; then
                    pass "已自动改写 group_add=[$new_groups]"
                else
                    note_fail "自动改写失败（compose 结构可能已变化），请手动把 group_add 改为 [$want_groups]"
                fi
            else
                note_fail "group_add 未修正，容器硬件访问会失败（稍后可重跑 ./tools/deploy.sh --yes）"
            fi
        fi
    fi
fi

step "4. compose 硬件配置与实际环境匹配"
# NVIDIA runtime 要求：无 N 卡的机器上 runtime: nvidia 会让容器启动失败
if [ "$HAS_NVIDIA" != 1 ] && compose_json | grep -q '"runtime"[^,]*"nvidia"'; then
    note_fail "compose 启用了 runtime: nvidia 但本机无 NVIDIA 显卡——容器将无法启动。
    修复：编辑 docker-compose.yml 注释掉 runtime: nvidia 与 NVIDIA_VISIBLE_DEVICES/NVIDIA_DRIVER_CAPABILITIES 两行环境变量"
else
    pass "runtime 配置与宿主 GPU 匹配"
fi

step "5. 媒体挂载目录"
media_src="$(compose_json | python3 -c "
import json,sys
try:
    cfg=json.load(sys.stdin)
    for v in cfg['services']['linux-3fui-2'].get('volumes') or []:
        # compose config --format json 输出长语法对象；字符串形式（src:dst）也兼容
        if isinstance(v, dict):
            if str(v.get('target','')).rstrip('/')=='/media':
                print(v.get('source','')); break
        else:
            parts=str(v).split(':')
            if len(parts)>=2 and parts[1].rstrip('/')=='/media':
                print(parts[0]); break
except Exception:
    pass
" 2>/dev/null)"
if [ -z "$media_src" ]; then
    warn "compose 中未找到 /media 挂载"
elif [ ! -d "$media_src" ]; then
    note_fail "媒体目录 $media_src 不存在（docker 会自动以 root 创建空目录，属主不对容器读不到文件）"
else
    owner="$(stat -c '%u:%g' "$media_src" 2>/dev/null || echo '?')"
    pass "媒体目录 $media_src 存在（属主 $owner，容器运行 uid=1000）"
fi

step "6. 端口占用（8080/8443/10591）"
if [ -n "$(docker compose ps -q 2>/dev/null)" ]; then
    pass "compose 服务已在运行，端口属本服务（将重建）"
else
    ports_in_use="$(ss -tlnup 2>/dev/null | grep -E ':(8080|8443|10591)\b' || true)"
    if [ -n "$ports_in_use" ]; then
        note_fail "端口被其他进程占用：$(echo "$ports_in_use" | awk '{print $5}' | sort -u | tr '\n' ' ')"
    else
        pass "端口空闲"
    fi
fi

if [ "$FAILED" != 0 ]; then
    printf '\n\033[31m部署前检查未通过\033[0m——修复上述 FAIL 后再运行（组问题可用 --yes 自动修）。\n'
    exit 1
fi

step "7. 构建与启动"
if [ "$BUILD" = 1 ]; then
    say "docker compose build ..."
    docker compose build || { note_fail "镜像构建失败"; exit 1; }
fi
docker compose up -d || { note_fail "容器启动失败"; exit 1; }
pass "容器已启动"

step "8. 等待健康检查"
health=""
for _ in $(seq 1 30); do
    health="$(docker inspect -f '{{.State.Health.Status}}' linux-3fui-2 2>/dev/null || echo 未知)"
    [ "$health" = "healthy" ] && break
    sleep 2
done
if [ "$health" = "healthy" ]; then
    pass "容器 healthy"
else
    note_fail "健康检查超时（当前：$health），查看日志：docker compose logs"
fi

step "9. 容器内硬件/API 验证（check-hw.sh）"
if [ "$FAILED" = 0 ]; then
    ./tools/check-hw.sh
    FAILED=$?
fi

printf '\n== 汇总 ==\n'
if [ "$FAILED" = 0 ]; then
    say "部署完成：环境检查、容器启动、编码器矩阵全部通过。"
    say "访问 https://$(hostname -I | awk '{print $1}'):8443（自签名证书需点继续）"
else
    say "部署完成但存在失败项，按上方输出排查。"
fi
exit $FAILED
