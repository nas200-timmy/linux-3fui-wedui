// 极简 Markdown 渲染（代码块 / 行内代码 / 标题 / 列表 / 粗体 / 链接），
// 与 3FUI Agent 相同的基础语法支持，纯本地渲染无外部依赖。
function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

export function marked(source: string): string {
  const lines = source.split('\n')
  const output: string[] = []
  let inCode = false
  let codeLines: string[] = []
  let inList = false

  const flushList = () => {
    if (inList) {
      output.push('</ul>')
      inList = false
    }
  }

  const inline = (text: string): string => {
    let result = escapeHtml(text)
    result = result.replace(/`([^`]+)`/g, '<code>$1</code>')
    result = result.replace(/\*\*([^*]+)\*\*/g, '<b>$1</b>')
    result = result.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (match, text: string, url: string) => {
      // 协议白名单：Agent 消息是不可信 LLM 输出且经 v-html 渲染，拦截 javascript: 等可执行协议
      const safe = /^(https?:|mailto:|#|\.|\/)/i.test(url.trim()) ? url : '#'
      return `<a href="${safe}" target="_blank" rel="noopener">${text}</a>`
    })
    return result
  }

  for (const line of lines) {
    if (line.trim().startsWith('```')) {
      if (inCode) {
        output.push(`<pre>${escapeHtml(codeLines.join('\n'))}</pre>`)
        codeLines = []
        inCode = false
      } else {
        flushList()
        inCode = true
      }
      continue
    }
    if (inCode) {
      codeLines.push(line)
      continue
    }
    const trimmed = line.trim()
    if (trimmed === '') {
      flushList()
      continue
    }
    const heading = /^(#{1,4})\s+(.*)$/.exec(trimmed)
    if (heading) {
      flushList()
      const level = heading[1].length
      output.push(`<h${level} style="margin:0.6em 0 0.3em">${inline(heading[2])}</h${level}>`)
      continue
    }
    const listItem = /^[-*]\s+(.*)$/.exec(trimmed)
    if (listItem) {
      if (!inList) {
        output.push('<ul style="margin:0.3em 0;padding-left:1.3em">')
        inList = true
      }
      output.push(`<li>${inline(listItem[1])}</li>`)
      continue
    }
    flushList()
    output.push(`<p>${inline(trimmed)}</p>`)
  }
  if (inCode && codeLines.length > 0) output.push(`<pre>${escapeHtml(codeLines.join('\n'))}</pre>`)
  flushList()
  return output.join('')
}
