#!/usr/bin/env bash
# linux-3fui 部署闸：宿主环境检查 → 拉起容器 → 容器内硬件/API 验证。
# 解决四类跨机器部署翻车：
#   1. 镜像/编码器缺失（构建后 ffmpeg 缺硬编）——部署后探针矩阵兜底
#   2. 镜像来源（没有本地镜像时先拉 CI 发布的预构建镜像，拉不到才要求 --build）
#   3. 硬件未挂载（无 /dev/dri、缺 NVIDIA runtime）——部署前清点
#   4. 用户组错位（group_add 与宿主 video/render GID 不一致）——写 docker-compose.override.yml 适配
#      （不动受版本控制的 compose：GID 是每台机器不同的本地配置，改 tracked 文件会让
#       部署机工作区永久 dirty、之后 git pull 必冲突）
# 用法：
#   ./tools/deploy.sh              # 检查 + up -d + 验证
#   ./tools/deploy.sh --build      # 先本地构建再部署（首次/改代码后）
#   ./tools/deploy.sh --yes        # 自动写入 override 适配时不询问
# 退出码：0 全部通过；1 存在硬失败（看汇总）。
set -u
cd "$(dirname "$0")/.." || exit 1

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

# 镜像来源：优先本地已有；没有就先拉预构建镜像（CI 发布到 ghcr.io），拉不到才要求本地构建
if [ "$BUILD" = 0 ]; then
    image="$(compose_json | python3 -c "
import json,sys
try:
    print(json.load(sys.stdin)['services']['linux-3fui-2'].get('image',''))
except Exception:
    print('')
" 2>/dev/null)"
    [ -n "$image" ] || image="linux-3fui-2:latest"
    if docker image inspect "$image" >/dev/null 2>&1; then
        pass "镜像 $image 已存在"
    else
        say "本地没有镜像 $image，尝试拉取预构建镜像……"
        if docker compose pull --quiet >/dev/null 2>&1 || docker pull "$image" >/dev/null 2>&1; then
            pass "已拉取 $image"
        else
            note_fail "既无本地镜像也拉不到 $image——要本地构建请加 --build：./tools/deploy.sh --build"
        fi
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

# 组比对与自动修正（渲染后的有效配置 = 受版本控制的 compose + 本机 override）
if [ -n "$RENDER_GID" ] && [ -d /dev/dri ] && command -v python3 >/dev/null; then
    # 去重后的有效 group_add：compose 的列表合并语义（替换/追加）不确定，这里按集合判断
    effective_groups() {
        compose_json | python3 -c "
import json,sys
try:
    g=[str(x) for x in (json.load(sys.stdin)['services']['linux-3fui-2'].get('group_add') or [])]
except Exception:
    g=[]
seen=set(); out=[]
for x in g:
    if x not in seen:
        seen.add(x); out.append(x)
print(' '.join(out))
" 2>/dev/null
    }
    cur_groups="$(effective_groups)"
    if [ -z "$cur_groups" ]; then
        warn "compose 未配置 group_add——容器内可能无权访问 /dev/dri 设备节点"
    else
        missing=""
        for g in "${VIDEO_GID:-44}" "$RENDER_GID"; do
            case " $cur_groups " in *" $g "*) ;; *) missing="$missing $g" ;; esac
        done
        if [ -z "$missing" ]; then
            pass "compose group_add=[$cur_groups] 已覆盖宿主 video=${VIDEO_GID:-44} render=${RENDER_GID}"
        else
            warn "compose group_add=[$cur_groups] 缺少宿主组：$missing"
            do_fix=0
            if [ "$ASSUME_YES" = 1 ]; then
                do_fix=1
            else
                printf '  是否写入 docker-compose.override.yml 做本机适配？[y/N] '
                read -r ans
                case "$ans" in y|Y|yes|YES) do_fix=1 ;; *) do_fix=0 ;; esac
            fi
            if [ "$do_fix" = 1 ]; then
                # 写 override 而不是 sed 改 docker-compose.yml：GID 是每台机器不同的本地配置，
                # 改受版本控制的文件会让部署机工作区永久 dirty、之后 git pull 必冲突。
                cat > docker-compose.override.yml <<EOF
# 由 tools/deploy.sh 生成：本机 video/render 组 GID 适配（每台机器不同，已被 .gitignore 忽略）
services:
  linux-3fui-2:
    group_add:
      - "${VIDEO_GID:-44}"
      - "${RENDER_GID}"
EOF
                new_groups="$(effective_groups)"
                still_missing=""
                for g in "${VIDEO_GID:-44}" "$RENDER_GID"; do
                    case " $new_groups " in *" $g "*) ;; *) still_missing="$still_missing $g" ;; esac
                done
                if [ -z "$still_missing" ]; then
                    pass "已写入 docker-compose.override.yml，有效 group_add=[$new_groups]"
                else
                    note_fail "写入 override 后仍缺组:$still_missing（compose 结构可能已变化），请人工检查 docker-compose.override.yml"
                fi
            else
                note_fail "group_add 未适配，容器硬件访问会失败（稍后可重跑 ./tools/deploy.sh --yes）"
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
