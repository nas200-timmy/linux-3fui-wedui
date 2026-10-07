# linux-3fui 多阶段构建镜像
#   阶段 1：node 构建前端（Vue 3 SPA）
#   阶段 2：.NET SDK 发布服务端（内嵌前端静态资源）
#   阶段 3：debian-slim 运行时（自带 ffmpeg + 中文字体，非 root）
# 支持 linux/amd64 与 linux/arm64（多架构 NAS 通用）。

# ── 阶段 1：前端构建 ──
FROM node:22-alpine AS webbuild
WORKDIR /web
COPY src/3fui-web/package.json src/3fui-web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/3fui-web/ ./
# vite 输出目录为 ../3fui-server/wwwroot
RUN npm run build

# ── 阶段 2：服务端发布 ──
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH=amd64
WORKDIR /src
COPY src/3fui-core/ 3fui-core/
COPY src/3fui-server/ 3fui-server/
COPY --from=webbuild /3fui-server/wwwroot 3fui-server/wwwroot
RUN dotnet publish 3fui-server/3fui-server.csproj -c Release -r linux-${TARGETARCH} --self-contained true -o /out --nologo

# ── 阶段 3：运行时 ──
FROM debian:trixie-slim AS runtime
# 使用清华 Debian 镜像加速（国内访问 deb.debian.org 极慢）
RUN sed -i 's@deb.debian.org@mirrors.tuna.tsinghua.edu.cn@g' /etc/apt/sources.list.d/debian.sources
# ffmpeg：Debian 官方构建（含 VAAPI/QSV 硬件加速支持）；fonts-noto-cjk：中文与烧录字幕字体
# libva / va-driver / mesa-vulkan-drivers：硬件加速所需的用户态驱动（--no-install-recommends 会省略它们）
# libmfx-gen1.2：oneVPL 的 Intel GPU 运行时——QSV（hevc_qsv 等）缺它会报 MFX session -9
# libvpl2：oneVPL 调度库，ffmpeg 链接的 QSV 入口（ldd 确认链 libvpl.so.2，不是旧 libmfx）
# intel-media-va-driver / libmfx-gen1.2 / libvpl2 为 Intel 平台专属，Debian 仅 amd64 发行——
# arm64 构建时跳过（arm64 NAS 一般无 Intel GPU；VAAPI 通用路径由 mesa-va-drivers 提供）
RUN apt-get update \
    && INTEL_PKGS="" \
    && if [ "$(dpkg --print-architecture)" = "amd64" ]; then \
           INTEL_PKGS="intel-media-va-driver libmfx-gen1.2 libvpl2"; \
       fi \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        ffmpeg \
        fonts-noto-cjk \
        libc6 \
        libgcc-s1 \
        libicu76 \
        libssl3t64 \
        libstdc++6 \
        libva-drm2 \
        libva2 \
        libvulkan1 \
        mesa-va-drivers \
        mesa-vulkan-drivers \
        tzdata \
        $INTEL_PKGS \
    && rm -rf /var/lib/apt/lists/* \
    && useradd -r -u 1001 -g users -s /usr/sbin/nologin appuser \
    && mkdir -p /data \
    && chown -R appuser:users /data

WORKDIR /app
COPY --from=build /out ./
RUN chown -R appuser:users /app

ENV DATA_DIR=/data \
    HTTP_PORT=8080 \
    HTTPS_PORT=8443 \
    MEDIA_ROOT=/media

VOLUME ["/data", "/media"]
EXPOSE 8080 8443 10591/udp

USER appuser
ENTRYPOINT ["/app/3fui-server"]
