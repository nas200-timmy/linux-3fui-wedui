#!/usr/bin/env bash
# 硬件加速冒烟测试：验证镜像在当前宿主机的编码器可用性。
#
# 用法：
#   ./tools/check-hw.sh                # 编码器矩阵 + 已部署容器
#   ./tools/check-hw.sh --encoders-only
#   IMAGE=ghcr.io/xxx/yyy:latest ./tools/check-hw.sh     # 覆盖镜像名
#
# 镜像名优先级：$IMAGE > compose 渲染结果里的 image（避免锁死某个 tag）> linux-3fui-2:latest
#
# 判定口径（虚拟机/无核显直通时别被吓到）：
#   硬失败 = 镜像缺库 / 本地没有该镜像 / CPU 软编都跑不起来 / API 与健康检查不通
#   预期不可用（SKIP，不算失败）= 宿主没有 /dev/dri/renderD* → 硬编必然不可用
#   警告   = QSV 失败但 VAAPI 可用（多为宿主 oneVPL 栈问题，可改用 VAAPI）
set -u
cd "$(dirname "$0")/.." || exit 1

say()  { printf '%s\n' "$*"; }
pass() { printf '  \033[32mPASS\033[0m  %s\n' "$*"; }
fail() { printf '  \033[31mFAIL\033[0m  %s\n' "$*"; }
warn() { printf '  \033[33mWARN\033[0m  %s\n' "$*"; }
skip() { printf '  \033[33mSKIP\033[0m  %s\n' "$*"; }

ENCODERS_ONLY=0
[ "${1:-}" = "--encoders-only" ] && ENCODERS_ONLY=1

say "== 宿主预检 =="
command -v docker >/dev/null || { say "docker 未安装"; exit 1; }

# ── 从 compose 渲染结果取 image / container_name（读不到就退回默认值）──
compose_field() { # $1 = image | container_name
    local key="$1"
    if command -v python3 >/dev/null; then
        docker compose config --format json 2>/dev/null | python3 -c "
import json,sys
try:
    svc = json.load(sys.stdin)['services']['linux-3fui-2']
    print(svc.get('$key') or '')
except Exception:
    print('')
" 2>/dev/null
    else
        docker compose config 2>/dev/null | awk -v k="    $key:" '
            index($0,k){ v=$0; sub(/^[ \t]*/,"",v); sub(k,"",v); gsub(/^[ \t]+|[ \t]+$/,"",v); print v; exit }'
    fi
}
IMAGE_FROM_COMPOSE="$(compose_field image)"
CONTAINER="$(compose_field container_name)"; CONTAINER="${CONTAINER:-linux-3fui-2}"
IMAGE_FROM_ENV="${IMAGE:-}"
IMAGE="${IMAGE:-${IMAGE_FROM_COMPOSE:-linux-3fui-2:latest}}"

# ── 渲染节点：没有 renderD* 时硬编必然不可用（虚拟机常见），别报成 FAIL ──
RENDER_NODES="$(ls /dev/dri/renderD* 2>/dev/null | tr '\n' ' ')"
if [ -n "${DRI_NODE:-}" ]; then
    :                                       # 调用者显式指定
elif [ -n "$RENDER_NODES" ]; then
    DRI_NODE="$(printf '%s\n' $RENDER_NODES | head -1)"
else
    DRI_NODE=""
fi
HAVE_RENDER=0; [ -n "$DRI_NODE" ] && HAVE_RENDER=1

VIDEO_GID="$(getent group video  | cut -d: -f3)"; VIDEO_GID="${VIDEO_GID:-44}"
RENDER_GID="$(getent group render | cut -d: -f3)"; RENDER_GID="${RENDER_GID:-?}"
IMG_SRC="默认值"
[ -n "$IMAGE_FROM_COMPOSE" ] && IMG_SRC="compose 渲染结果"
[ -n "$IMAGE_FROM_ENV" ] && IMG_SRC="环境变量 IMAGE"
say "镜像: ${IMAGE}（来源：${IMG_SRC}）"
say "容器: ${CONTAINER}"
say "宿主组: video=${VIDEO_GID} render=${RENDER_GID}"
say "渲染节点: $([ "$HAVE_RENDER" = 1 ] && echo "$DRI_NODE" || echo "无（/dev/dri/renderD* 不存在）")"
[ -d /dev/dri ] || say "提示: /dev/dri 整个不存在——虚拟机的虚拟显卡只有 card0、无 renderD128 时就是这种情况"
[ "$RENDER_GID" = "?" ] && say "提示: 宿主没有 render 组，设备节点可能属主异常"
[ "$HAVE_RENDER" = 0 ] && say "提示: 无渲染节点 → QSV/VAAPI 不可用属宿主限制，下列硬编项会 SKIP 而不是 FAIL；CPU 软编不受影响"

# 镜像必须已在本地：不存在就直接说清楚，别让 docker 去 docker.io 拉（慢且常拉不到）
if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
    say ""
    fail "本地没有镜像 $IMAGE"
    say "  用预构建镜像： docker compose pull     （或 IMAGE=<完整镜像名> ./tools/check-hw.sh）"
    say "  本地构建：     ./tools/deploy.sh --build"
    say ""
    say "  国内拉 ghcr.io 慢的话可换镜像源（实测 ~9MB/s）：
    IMAGE=ghcr.nju.edu.cn/<owner>/<repo>:<tag>（改 .env 里的镜像或 compose 的 image 后 docker pull）"
    exit 1
fi
pass "镜像 $IMAGE 本地存在"

RUNTIME_ARGS=()
[ -d /dev/dri ] && RUNTIME_ARGS+=(--device /dev/dri)
RUNTIME_ARGS+=(--group-add "$VIDEO_GID")
[ "$RENDER_GID" != "?" ] && RUNTIME_ARGS+=(--group-add "$RENDER_GID")
if docker info 2>/dev/null | grep -q "nvidia"; then
    RUNTIME_ARGS+=(--runtime=nvidia -e NVIDIA_VISIBLE_DEVICES=all -e NVIDIA_DRIVER_CAPABILITIES=all)
    HAS_NVIDIA=1
else
    HAS_NVIDIA=0
    say "提示: docker 无 nvidia runtime（未装 NVIDIA Container Toolkit），NVENC 将 SKIP"
fi

FAILED=0

say "== 镜像依赖（容器层）=="
if docker run --rm --entrypoint sh "${RUNTIME_ARGS[@]}" "$IMAGE" -c \
    'ls /usr/lib/x86_64-linux-gnu/libvpl.so.2 /usr/lib/x86_64-linux-gnu/libmfx-gen.so.1.2 /usr/lib/x86_64-linux-gnu/dri/iHD_drv_video.so >/dev/null 2>&1'; then
    pass "oneVPL/libmfx-gen/iHD 驱动齐全（QSV 用户态在镜像内，无缺失）"
else
    fail "镜像缺 QSV/VAAPI 用户态库，需重建镜像（检查 Dockerfile apt 列表）"; FAILED=1
fi

# 探针命令与 3fui 应用实际生成的命令保持一致（QSV 不带 init_hw_device，VAAPI 带 -vaapi_device）
probe() { # $1=名称 $2=ffmpeg参数
    if docker run --rm --entrypoint sh "${RUNTIME_ARGS[@]}" "$IMAGE" -c \
        "ffmpeg -v error -f lavfi -i testsrc2=duration=1:size=320x240 $2 -f null - >/dev/null 2>&1"; then
        pass "$1"; return 0
    else
        return 1
    fi
}

say "== CPU 软编（验证镜像与 ffmpeg 本身，硬编不可用时这条必须过）=="
if probe "CPU 软编   libx264" "-c:v libx264 -preset ultrafast"; then
    :
else
    fail "容器内 CPU 软编都失败——镜像或 ffmpeg 有问题，与硬件加速无关"; FAILED=1
fi

say "== 编码器矩阵（端到端，镜像 ${IMAGE}）=="
if [ "$HAVE_RENDER" = 0 ]; then
    skip "Intel QSV   h264_qsv / hevc_qsv（宿主无 /dev/dri/renderD*，属预期不可用）"
    skip "Intel VAAPI h264_vaapi / hevc_vaapi（同上）"
    SKIPPED_HW=1
else
    SKIPPED_HW=0
    QSV_OK=1
    probe "Intel QSV   h264_qsv"   "-c:v h264_qsv -low_power 1"   || { QSV_OK=0; }
    probe "Intel QSV   hevc_qsv"   "-c:v hevc_qsv -low_power 1"   || true
    VAAPI_OK=1
    probe "Intel VAAPI h264_vaapi" "-vaapi_device $DRI_NODE -vf format=nv12,hwupload -c:v h264_vaapi" || VAAPI_OK=0
    probe "Intel VAAPI hevc_vaapi" "-vaapi_device $DRI_NODE -vf format=nv12,hwupload -c:v hevc_vaapi" || true
    if [ "$QSV_OK" = 0 ] && [ "$VAAPI_OK" = 1 ]; then
        warn "QSV 端到端失败但 VAAPI 可用——多为宿主 oneVPL 栈问题（非容器问题），Intel 侧转码用 VAAPI 预设即可"
    elif [ "$QSV_OK" = 0 ] && [ "$VAAPI_OK" = 0 ]; then
        fail "QSV 与 VAAPI 同时失败，而宿主确实有渲染节点——查 /dev/dri 权限与组，或宿主 VA 驱动"; FAILED=1
    fi
fi
if [ "$HAS_NVIDIA" = 1 ]; then
    probe "NVIDIA NVENC h264_nvenc" "-c:v h264_nvenc" || { fail "NVENC h264 不可用"; FAILED=1; }
    probe "NVIDIA NVENC hevc_nvenc" "-c:v hevc_nvenc" || true
else
    skip "NVIDIA NVENC h264_nvenc（无 nvidia runtime）"
    skip "NVIDIA NVENC hevc_nvenc（无 nvidia runtime）"
fi

if [ "$ENCODERS_ONLY" = 0 ]; then
    say "== 已部署容器（HTTP 8080 口径，与健康检查一致）=="
    if curl -fsSLk -m 5 "http://127.0.0.1:8080/api/auth/status" >/dev/null 2>&1; then
        pass "API http://127.0.0.1:8080/api/auth/status"
    elif curl -fsSk -m 5 "https://127.0.0.1:8443/api/auth/status" >/dev/null 2>&1; then
        pass "API https://127.0.0.1:8443/api/auth/status"
    else
        fail "API 无响应（容器没起？docker compose ps / docker compose logs 看看）"; FAILED=1
    fi
    if ! curl -fsSk -m 5 "https://127.0.0.1:8443/api/auth/status" >/dev/null 2>&1; then
        say "  提示: 8443(HTTPS) 当前无响应——没上传证书时属正常，用 http://<IP>:8080 访问；证书在网页「设置」里上传"
    fi
    HEALTH="$(docker inspect -f '{{.State.Health.Status}}' "$CONTAINER" 2>/dev/null || echo 未部署)"
    if [ "$HEALTH" = "healthy" ]; then
        pass "容器健康检查 healthy"
    else
        fail "容器状态: $HEALTH"; FAILED=1
    fi
    # 运行中的容器组与宿主 GID 是否漂移（compose 里 group_add 写错时的第一嫌疑）
    CGROUPS="$(docker inspect -f '{{join .HostConfig.GroupAdd " "}}' "$CONTAINER" 2>/dev/null || true)"
    if [ -n "$CGROUPS" ] && [ "$RENDER_GID" != "?" ]; then
        case " $CGROUPS " in
            *" $RENDER_GID "*|*\"$RENDER_GID\"*) : ;;
            *) warn "容器 group_add=[$CGROUPS] 不含宿主 render GID $RENDER_GID，硬件节点访问会失败（deploy.sh 会写 override 修它）" ;;
        esac
    fi
fi

say "== 结论 =="
if [ "$FAILED" = 0 ]; then
    if [ "${SKIPPED_HW:-0}" = 1 ]; then
        say "通过（硬编项因宿主无渲染节点而 SKIP——预期）。CPU 软编可用；装了核显/独显并透传 /dev/dri 后可重跑本脚本。"
    else
        say "容器兼容性通过。NVENC / VAAPI 为受支持路径；QSV 状态见上。"
    fi
else
    say "存在硬失败项，按矩阵逐项排查（先看'宿主组'与'本地是否有该镜像'两行）。"
fi
exit $FAILED
