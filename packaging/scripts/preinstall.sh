#!/bin/sh
# deb preinst / rpm %pre
# 必须在解包**之前**建好服务用户/组：包里 /var/lib/linux-3fui 的属主写的是用户名，
# 解包时解析不到会报"unknown user"。本脚本走 POSIX sh（rpm 的 scriptlet 也是用 sh 跑）。
set -e

if ! getent group linux-3fui >/dev/null 2>&1; then
    groupadd -r linux-3fui >/dev/null 2>&1 || groupadd linux-3fui >/dev/null 2>&1 || true
fi

if ! id -u linux-3fui >/dev/null 2>&1; then
    useradd -r -g linux-3fui -d /var/lib/linux-3fui -s /usr/sbin/nologin \
        -c "linux-3fui service" linux-3fui >/dev/null 2>&1 \
    || useradd -r -g linux-3fui -d /var/lib/linux-3fui -s /bin/false \
        linux-3fui >/dev/null 2>&1 \
    || true
fi

exit 0
