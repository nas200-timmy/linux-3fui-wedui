#!/bin/sh
# deb prerm / rpm %preun
# 卸载/升级前先停服务（升级时只停不 disable，装完由 postinst 重启）。
# /var/lib/linux-3fui 里的设置、预设、HTTPS 证书**故意保留**，重装/升级可接着用。
set -e

stop_svc() {
    if command -v systemctl >/dev/null 2>&1 && [ -d /run/systemd/system ]; then
        systemctl stop linux-3fui.service >/dev/null 2>&1 || true
    fi
}

case "${1:-}" in
    upgrade|1)
        stop_svc
        ;;
    remove|purge|0)
        stop_svc
        if command -v systemctl >/dev/null 2>&1 && [ -d /run/systemd/system ]; then
            systemctl disable linux-3fui.service >/dev/null 2>&1 || true
        fi
        echo "linux-3fui 已停止。数据目录 /var/lib/linux-3fui 保留（彻底清除请自行 rm -rf）。"
        ;;
    *)
        ;;
esac

exit 0
