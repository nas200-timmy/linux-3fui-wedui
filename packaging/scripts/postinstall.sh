#!/bin/sh
# deb postinst / rpm %post
# 装完要做四件事：加入硬件组 → 修数据目录属主 → 自检 ffmpeg → 交给 systemd。
# rpm 的 scriptlet 用 sh 跑且失败会影响事务，所以一律 `|| true` 兜住并 exit 0。
set -e

# 1) 硬件访问组（NAS 上通常有 video/render；没有就跳过，纯 CPU 也能跑）
for g in video render; do
    if getent group "$g" >/dev/null 2>&1; then
        usermod -aG "$g" linux-3fui >/dev/null 2>&1 || true
    fi
done

# 2) 数据目录（设置/预设/队列缓存/证书；用户数据，卸载不删）
install -d -m 0750 -o linux-3fui -g linux-3fui /var/lib/linux-3fui >/dev/null 2>&1 \
    || mkdir -p /var/lib/linux-3fui >/dev/null 2>&1 \
    || true

# 3) 裸机部署的前提：宿主自带 ffmpeg/ffprobe（包里不捆绑）
if ! command -v ffmpeg >/dev/null 2>&1; then
    echo "" >&2
    echo "  ⚠ 未检测到 ffmpeg：linux-3fui 需要宿主自带的 ffmpeg/ffprobe，否则无法转码。" >&2
    echo "      Debian/Ubuntu : sudo apt install ffmpeg" >&2
    echo "      Fedora        : sudo dnf install ffmpeg   （需先启用 RPM Fusion）" >&2
    echo "      硬编另需 VA-API/QSV 用户态驱动，例（Intel 核显）：" >&2
    echo "      sudo apt install intel-media-va-driver libvpl2 libmfx-gen1.2 libva-drm2 mesa-va-drivers" >&2
    echo "" >&2
fi

# 4) 交给 systemd
if command -v systemctl >/dev/null 2>&1 && [ -d /run/systemd/system ]; then
    systemctl daemon-reload >/dev/null 2>&1 || true
    systemctl enable linux-3fui.service >/dev/null 2>&1 || true
    systemctl restart linux-3fui.service >/dev/null 2>&1 || true

    echo "linux-3fui 已安装并启动："
    echo "  1) 指定媒体库（必改）: sudo vi /etc/linux-3fui/env   # MEDIA_ROOT=/你的媒体库"
    echo "  2) 生效            : sudo systemctl restart linux-3fui"
    echo "  3) 打开网页        : http://<本机IP>:8080"
    echo "  4) 看日志          : journalctl -u linux-3fui -f"
else
    echo "linux-3fui 已安装（未检测到运行中的 systemd，跳过服务启用）。"
fi

exit 0
