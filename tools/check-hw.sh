#!/usr/bin/env bash
# 硬件加速冒烟测试：验证 linux-3fui-2 镜像在当前宿主机的编码器可用性。
# 用法：
#   ./tools/check-hw.sh                # 全量检查（编码器矩阵 + 已部署容器）
#   ./tools/check-hw.sh --encoders-only
# 环境变量：IMAGE（默认 linux-3fui-2:latest）、DRI_NODE（默认 /dev/dri/renderD128）
# 换机器后 compose 的 group_add 该写什么，看输出"宿主组"一行（自动检测）。
#
# 判定口径：
#   硬失败 = 镜像缺库（容器问题，构建坏了）/ VAAPI / NVENC / API / 健康检查
#   警告   = QSV 端到端失败但 VAAPI 可用（多为宿主 oneVPL 栈问题，同引擎可被 VAAPI 替代）
set -u
cd "$(dirname "$0")/.." || exit 1

IMAGE="${IMAGE:-linux-3fui-2:latest}"
DRI_NODE="${DRI_NODE:-/dev/dri/renderD128}"
ENCODERS_ONLY=0
[ "${1:-}" = "--encoders-only" ] && ENCODERS_ONLY=1

say()  { printf '%s\n' "$*"; }
pass() { printf '  \033[32mPASS\033[0m  %s\n' "$*"; }
fail() { printf '  \033[31mFAIL\033[0m  %s\n' "$*"; }
warn() { printf '  \033[33mWARN\033[0m  %s\n' "$*"; }
skip() { printf '  \033[33mSKIP\033[0m  %s\n' "$*"; }

say "== 宿主预检 =="
command -v docker >/dev/null || { say "docker 未安装"; exit 1; }
[ -d /dev/dri ] || say "警告: /dev/dri 不存在，本机无 GPU 渲染节点"
VIDEO_GID="$(getent group video  | cut -d: -f3)"; VIDEO_GID="${VIDEO_GID:-44}"
RENDER_GID="$(getent group render | cut -d: -f3)"; RENDER_GID="${RENDER_GID:-?}"
say "宿主组: video=${VIDEO_GID} render=${RENDER_GID}  DRI_NODE=${DRI_NODE}"
[ "$RENDER_GID" = "?" ] && say "警告: 宿主没有 render 组，设备节点可能属主异常"

RUNTIME_ARGS=(--device /dev/dri --group-add "$VIDEO_GID")
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

say "== 编码器矩阵（端到端，镜像 ${IMAGE}）=="
QSV_OK=1
probe "Intel QSV   h264_qsv"   "-c:v h264_qsv -low_power 1"   || { QSV_OK=0; }
probe "Intel QSV   hevc_qsv"   "-c:v hevc_qsv -low_power 1"   || true
VAAPI_OK=1
probe "Intel VAAPI h264_vaapi" "-vaapi_device $DRI_NODE -vf format=nv12,hwupload -c:v h264_vaapi" || VAAPI_OK=0
probe "Intel VAAPI hevc_vaapi" "-vaapi_device $DRI_NODE -vf format=nv12,hwupload -c:v hevc_vaapi" || true
if [ "$QSV_OK" = 0 ]; then
    if [ "$VAAPI_OK" = 1 ]; then
        warn "QSV 端到端失败但 VAAPI 可用——多为宿主 oneVPL 栈问题（非容器问题），Intel 侧转码用 VAAPI 预设即可"
    else
        fail "QSV 与 VAAPI 同时失败，Intel 核显路径不可用"; FAILED=1
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
    say "== 已部署容器 =="
    if curl -fsSk -m 5 https://127.0.0.1:8443/api/auth/status >/dev/null 2>&1; then
        pass "API https://127.0.0.1:8443/api/auth/status"
    else
        fail "API 无响应（容器没起？）"; FAILED=1
    fi
    HEALTH="$(docker inspect -f '{{.State.Health.Status}}' linux-3fui-2 2>/dev/null || echo 未部署)"
    if [ "$HEALTH" = "healthy" ]; then
        pass "容器健康检查 healthy"
    else
        fail "容器状态: $HEALTH"; FAILED=1
    fi
    # 运行中的容器组与宿主 GID 是否漂移（compose 里 group_add 写错时的第一嫌疑）
    CGROUPS="$(docker inspect -f '{{join .HostConfig.GroupAdd " "}}' linux-3fui-2 2>/dev/null || true)"
    if [ -n "$CGROUPS" ] && [ "$RENDER_GID" != "?" ]; then
        case " $CGROUPS " in
            *" $RENDER_GID "*|*\"$RENDER_GID\"*) : ;;
            *) warn "容器 group_add=[$CGROUPS] 不含宿主 render GID $RENDER_GID，硬件节点访问会失败，请同步 compose" ;;
        esac
    fi
fi

say "== 结论 =="
if [ "$FAILED" = 0 ]; then
    say "容器兼容性通过。NVENC / VAAPI 为受支持路径；QSV 状态见上。"
else
    say "存在硬失败项，按矩阵逐项排查（先查'宿主组'一行与 group_add 是否匹配）。"
fi
exit $FAILED
