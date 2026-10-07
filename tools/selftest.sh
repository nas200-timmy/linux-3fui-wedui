#!/usr/bin/env bash
# tools/ 自测：deploy.sh 的 group_add 适配 + check-hw.sh 的镜像名/渲染节点口径。
#
# 为什么需要它：group_add 的坑只在"宿主 GID 与 compose 里的值部分相同"时才出现
# （比如 video 相同、render 不同），组合很多，靠人工试很容易漏；而一旦写错，
# compose 会因列表重复直接报 `items at 0 and 2 are equal` 把整条部署中止。
#
# 做法：不碰真 docker——在临时目录里造一个 `docker` 桩（外加 getent/curl 桩），
# 桩里的 `compose config` 按 compose 的真实语义**合并 yml + override，并在 group_add 重复时复现同款报错**。
#
# 用法：./tools/selftest.sh          # 全过则退出码 0
set -u
cd "$(dirname "$0")/.." || exit 1
REPO="$PWD"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

ok()   { printf '  \033[32mPASS\033[0m  %s\n' "$*"; }
bad()  { printf '  \033[31mFAIL\033[0m  %s\n' "$*"; FAILED=1; }
FAILED=0

# ── 造桩 ──
mkdir -p "$WORK/bin"
cat > "$WORK/bin/getent" <<'EOF'
#!/bin/sh
case "$1 $2" in
  "group video")  echo "video:x:44:" ;;
  "group render") echo "render:x:991:" ;;
  *) exit 2 ;;
esac
EOF
cat > "$WORK/bin/docker" <<'EOF'
#!/usr/bin/env python3
"""docker 桩：实现 deploy.sh / check-hw.sh 用到的子命令；
compose config 会真的合并 yml + override，并在 group_add 重复时报 compose 同款错误。"""
import json, os, sys
argv = sys.argv[1:]
cwd = os.getcwd()

def merged():
    import yaml
    with open(os.path.join(cwd, 'docker-compose.yml')) as f:
        cfg = yaml.safe_load(f)
    svc = cfg['services']['linux-3fui-2']
    groups = [str(g) for g in (svc.get('group_add') or [])]
    ovr_path = os.path.join(cwd, 'docker-compose.override.yml')
    if os.path.exists(ovr_path):
        with open(ovr_path) as f:
            ovr = yaml.safe_load(f) or {}
        osvc = (ovr.get('services') or {}).get('linux-3fui-2') or {}
        groups += [str(g) for g in (osvc.get('group_add') or [])]   # compose 对列表是追加合并
        for k, v in osvc.items():
            if k != 'group_add':
                svc[k] = v
    dup = [g for i, g in enumerate(groups) if g in groups[:i]]
    if dup:
        i = groups.index(dup[0]); j = groups.index(dup[0], i + 1)
        sys.stderr.write('docker-compose.yml(或 override) 校验失败: services.linux-3fui-2.group_add\n'
                         f'items at {i} and {j} are equal\n')
        sys.exit(1)
    svc['group_add'] = groups
    svc.setdefault('image', 'ghcr.io/example/linux-3fui:latest')
    svc.setdefault('container_name', 'linux-3fui-2')
    if not svc.get('volumes'):
        svc['volumes'] = [{'type': 'bind', 'source': '/tmp', 'target': '/media'}]
    return {'services': {'linux-3fui-2': svc}}

if not argv:
    sys.exit(0)
cmd = argv[0]
if cmd == 'info':
    print('Server Version: stub'); sys.exit(0)
if cmd == 'image' and len(argv) > 2 and argv[1] == 'inspect':
    sys.exit(1 if os.environ.get('STUB_IMAGE_MISSING') == '1' else 0)
if cmd == 'compose':
    print(json.dumps(merged()) if len(argv) > 1 and argv[1] == 'config' else 'stub'); sys.exit(0)
if cmd == 'inspect':
    fmt = argv[argv.index('-f') + 1] if '-f' in argv else ''
    print('healthy' if 'Health' in fmt else ('44 105 991' if 'GroupAdd' in fmt else 'stub')); sys.exit(0)
sys.exit(0)   # run / ps / pull / up / tag
EOF
cat > "$WORK/bin/curl" <<'EOF'
#!/bin/sh
[ "${WANT_FAIL:-0}" = 1 ] && exit 7
exit 0
EOF
chmod +x "$WORK/bin"/*

# ── 每次场景都在干净的临时项目目录里跑 ──
mkcase() {   # $1 = override 内容（空则不放 override）
    rm -rf "$WORK/proj"; mkdir -p "$WORK/proj/tools"
    cp "$REPO/docker-compose.yml" "$WORK/proj/"
    cp "$REPO/tools/deploy.sh" "$REPO/tools/check-hw.sh" "$WORK/proj/tools/"
    [ -n "$1" ] && printf '%s' "$1" > "$WORK/proj/docker-compose.override.yml"
    return 0
}
LAST_RC=0
run() {   # 在临时项目目录里跑命令（去色），退出码放 LAST_RC
    out="$( cd "$WORK/proj" && env "$@" 2>&1 )"
    LAST_RC=$?
    out="$(printf '%s\n' "$out" | sed 's/\x1b\[[0-9;]*m//g')"
}
run_deploy() { run PATH="$WORK/bin:$PATH" bash tools/deploy.sh --yes; }
MARK='# 由 tools/deploy.sh 生成'

echo "== 场景1：无 override，宿主 render 与 compose 不同（video 相同）→ 只补 991 =="
mkcase ""
run_deploy; rc=$LAST_RC
[ "$rc" = 0 ] && ok "退出码 0（旧版会在这里中止）" || bad "退出码 $rc"
grep -q '只补缺失的 GID：991' <<<"$out" && ok "只把 991 写进 override" || bad "未按预期只补 991：$(grep -o 'GID：.*' <<<"$out" | head -1)"
grep -q '有效 group_add=\[44 105 991\]' <<<"$out" && ok "有效 group_add 无重复、且覆盖两个宿主组" || bad "有效 group_add 不对"
grep -c 'items at' <<<"$out" >/dev/null && bad "出现了 compose 重复项报错" || ok "没有 compose 重复项报错"

echo "== 场景2：手写 override 只补 991（正确状态）→ 识别为已覆盖且不覆盖该文件 =="
OVR_HAND_GOOD="$(cat <<'EOF'
# 手写的
services:
  linux-3fui-2:
    group_add:
      - "991"
EOF
)"
mkcase "$OVR_HAND_GOOD"
before="$(cat "$WORK/proj/docker-compose.override.yml")"
run_deploy; rc=$LAST_RC
[ "$rc" = 0 ] && ok "退出码 0" || bad "退出码 $rc"
grep -q '已覆盖宿主 video=44 render=991' <<<"$out" && ok "判定为已覆盖" || bad "未判定为已覆盖"
[ "$before" = "$(cat "$WORK/proj/docker-compose.override.yml")" ] && ok "手写文件未被改动" || bad "手写文件被改了"

echo "== 场景3：自家标记的坏 override（44+991）→ 自愈重写为只补 991 =="
OVR_OURS_BAD="$(cat <<EOF
$MARK：本机适配
services:
  linux-3fui-2:
    group_add:
      - "44"
      - "991"
EOF
)"
mkcase "$OVR_OURS_BAD"
run_deploy; rc=$LAST_RC
[ "$rc" = 0 ] && ok "退出码 0（自愈后继续部署）" || bad "退出码 $rc"
grep -q '已重写为只补缺失的 GID：991' <<<"$out" && ok "识别并重写了自家坏 override" || bad "未自愈：$(grep -oE '(渲染失败|已重写)[^\n]*' <<<"$out" | head -1)"
grep -qE '^\s+- "991"$' "$WORK/proj/docker-compose.override.yml" \
  && ! grep -qE '^\s+- "44"$' "$WORK/proj/docker-compose.override.yml" \
  && ok "override 现在只含 991" || bad "override 内容不对：$(cat "$WORK/proj/docker-compose.override.yml")"

echo "== 场景4：手写的坏 override → 报错并给出正确 YAML（含正确的补充列表）=="
OVR_HAND_BAD="$(cat <<'EOF'
# 手写的
services:
  linux-3fui-2:
    group_add:
      - "44"
      - "991"
EOF
)"
mkcase "$OVR_HAND_BAD"
run_deploy; rc=$LAST_RC
[ "$rc" != 0 ] && ok "退出码非 0（拦下部署）" || bad "不该成功"
grep -q 'items at 0 and 2 are equal' <<<"$out" && ok "把 compose 的原始报错透出来了" || bad "没透出 compose 报错"
grep -q '本机需要补的是：991' <<<"$out" && ok "给出的补组列表正确（只 991）" || bad "补组列表不对：$(grep -o '本机需要补的是：.*' <<<"$out")"

echo "== 场景5：check-hw 镜像名从 compose 读；本地不存在时不拉取、直接说清 =="
mkcase ""
run PATH="$WORK/bin:$PATH" STUB_IMAGE_MISSING=1 bash tools/check-hw.sh --encoders-only; rc=$LAST_RC
grep -qE '^镜像: .+:latest（来源：compose 渲染结果）$' <<<"$out" && ok "镜像名取自 compose（不再是写死的本地 tag）" || bad "镜像名不对：$(grep -o '镜像:.*' <<<"$out")"
[ "$rc" != 0 ] && grep -q '本地没有镜像' <<<"$out" && ok "缺镜像时明确报错并退出" || bad "缺镜像时行为不对"

echo "== 场景6：无渲染节点 → 硬编 SKIP（不算失败）、CPU 软编 PASS =="
run PATH="$WORK/bin:$PATH" bash tools/check-hw.sh --encoders-only; rc=$LAST_RC
[ "$rc" = 0 ] && ok "退出码 0（硬编不可用不算失败）" || bad "退出码 $rc"
grep -q 'SKIP  Intel QSV' <<<"$out" && ok "QSV 报 SKIP" || bad "QSV 未按预期 SKIP"
grep -q 'PASS  CPU 软编' <<<"$out" && ok "CPU 软编探针 PASS" || bad "CPU 软编探针未跑"

echo
if [ "$FAILED" = 0 ]; then
    printf '\033[32mtools/ 自测全部通过\033[0m\n'
else
    printf '\033[31m自测存在失败项\033[0m\n'
fi
exit $FAILED
