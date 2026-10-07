import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// 构建产物直接输出到服务端 wwwroot，dotnet 运行/发布时一并打包。
export default defineConfig({
  plugins: [vue()],
  build: {
    outDir: '../3fui-server/wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://127.0.0.1:8080',
      '/ws': { target: 'ws://127.0.0.1:8080', ws: true },
    },
  },
})
