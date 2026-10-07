// 极简 Markdown 渲染（代码块 / 行内代码 / 标题 / 有序无序列表 / 引用 / 表格 / 粗体 / 链接），
// 与 3FUI Agent 相同的基础语法支持，纯本地渲染无外部依赖。
//
// 表格是 2026-10-07 补的：模型给方案对比时几乎必用表格，之前没有表格分支 →
// `| 方案 A | 方案 B |` 会被当成普通段落原样显示。这里的手写解析按三种边界都做了兼容：
//   ① 表格行之间夹空行（很多模型会这么输出）
//   ② 没有 `|---|---|` 分隔行（首行直接当表头）
//   ③ 单元格里出现转义的 `\|`
function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

/** 行内语法（粗体 / 行内代码 / 链接白名单）。 */
function inline(text: string): string {
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

function isTableRow(line: string): boolean {
  const trimmed = line.trim()
  if (!trimmed.startsWith('|')) return false
  return (trimmed.match(/\|/g)?.length ?? 0) >= 2
}

/** 按未转义的 `|` 切分一行；`\|` 还原成字面量 `|`。 */
function splitRow(line: string): string[] {
  let text = line.trim()
  if (text.startsWith('|')) text = text.slice(1)
  if (text.endsWith('|')) text = text.slice(0, -1)
  const cells: string[] = []
  let current = ''
  for (let i = 0; i < text.length; i++) {
    const ch = text[i]
    if (ch === '\\' && text[i + 1] === '|') {
      current += '|'
      i++
      continue
    }
    if (ch === '|') {
      cells.push(current.trim())
      current = ''
      continue
    }
    current += ch
  }
  cells.push(current.trim())
  return cells
}

function isSeparatorRow(line: string): boolean {
  if (!isTableRow(line)) return false
  const cells = splitRow(line)
  return cells.length > 0 && cells.every(cell => /^:?-{2,}:?$/.test(cell))
}

function alignmentOf(line: string): (string | null)[] {
  return splitRow(line).map(cell => {
    const left = cell.startsWith(':')
    const right = cell.endsWith(':')
    if (left && right) return 'center'
    if (right) return 'right'
    if (left) return 'left'
    return null
  })
}

function renderTable(head: string[], body: string[][], aligns: (string | null)[]): string {
  const columns = head.length
  const cell = (tag: 'th' | 'td', text: string, index: number) => {
    const align = aligns[index]
    const style = align ? ` style="text-align:${align}"` : ''
    return `<${tag}${style}>${inline(text ?? '')}</${tag}>`
  }
  const normalize = (row: string[]) => Array.from({ length: columns }, (_, i) => row[i] ?? '')
  const headHtml = `<tr>${normalize(head).map((text, index) => cell('th', text, index)).join('')}</tr>`
  const bodyHtml = body
    .map(row => `<tr>${normalize(row).map((text, index) => cell('td', text, index)).join('')}</tr>`)
    .join('')
  return `<table class="md-table"><thead>${headHtml}</thead><tbody>${bodyHtml}</tbody></table>`
}

export function marked(source: string): string {
  const lines = source.split('\n')
  const output: string[] = []
  let inCode = false
  let codeLines: string[] = []
  let listKind: 'ul' | 'ol' | null = null
  let quoteLines: string[] = []

  const flushList = () => {
    if (listKind !== null) {
      output.push(listKind === 'ul' ? '</ul>' : '</ol>')
      listKind = null
    }
  }
  const flushQuote = () => {
    if (quoteLines.length > 0) {
      output.push(`<blockquote>${quoteLines.map(line => inline(line)).join('<br>')}</blockquote>`)
      quoteLines = []
    }
  }
  const flushBlocks = () => {
    flushList()
    flushQuote()
  }

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i]
    if (line.trim().startsWith('```')) {
      if (inCode) {
        output.push(`<pre>${escapeHtml(codeLines.join('\n'))}</pre>`)
        codeLines = []
        inCode = false
      } else {
        flushBlocks()
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
      flushBlocks()
      continue
    }

    // ── 表格：当前行与下一个非空行都是表格行才认 ──
    if (isTableRow(line)) {
      const rawRows: string[] = [line]
      let cursor = i + 1
      let consumed = i
      while (cursor < lines.length) {
        const candidate = lines[cursor]
        if (isTableRow(candidate)) {
          rawRows.push(candidate)
          consumed = cursor
          cursor++
          continue
        }
        if (candidate.trim() === '') {
          // 容忍表格中间的空行：往后找下一个非空行，还是表格行就继续吃
          let peek = cursor + 1
          while (peek < lines.length && lines[peek].trim() === '') peek++
          if (peek < lines.length && isTableRow(lines[peek])) {
            cursor = peek
            continue
          }
        }
        break
      }

      // 第二行是分隔行（|---|---:|）就取它做对齐并跳过；没有分隔行则首行直接当表头
      const hasSeparator = rawRows.length > 1 && isSeparatorRow(rawRows[1])
      const head = splitRow(rawRows[0])
      const aligns = hasSeparator ? alignmentOf(rawRows[1]) : []
      const body = rawRows.slice(hasSeparator ? 2 : 1).map(splitRow)
      if (head.length > 0 && body.length > 0) {
        flushBlocks()
        output.push(renderTable(head, body, aligns))
        i = consumed
        continue
      }
    }

    const heading = /^(#{1,4})\s+(.*)$/.exec(trimmed)
    if (heading) {
      flushBlocks()
      const level = heading[1].length
      output.push(`<h${level} style="margin:0.6em 0 0.3em">${inline(heading[2])}</h${level}>`)
      continue
    }

    const quote = /^>\s?(.*)$/.exec(trimmed)
    if (quote) {
      flushList()
      quoteLines.push(quote[1])
      continue
    }
    flushQuote()

    const bullet = /^[-*+]\s+(.*)$/.exec(trimmed)
    if (bullet) {
      if (listKind !== 'ul') {
        flushList()
        output.push('<ul style="margin:0.3em 0;padding-left:1.3em">')
        listKind = 'ul'
      }
      output.push(`<li>${inline(bullet[1])}</li>`)
      continue
    }
    const ordered = /^\d+[.)]\s+(.*)$/.exec(trimmed)
    if (ordered) {
      if (listKind !== 'ol') {
        flushList()
        output.push('<ol style="margin:0.3em 0;padding-left:1.6em">')
        listKind = 'ol'
      }
      output.push(`<li>${inline(ordered[1])}</li>`)
      continue
    }
    flushList()

    if (/^(-{3,}|\*{3,}|_{3,})$/.test(trimmed)) {
      output.push('<hr>')
      continue
    }
    output.push(`<p>${inline(trimmed)}</p>`)
  }
  if (inCode && codeLines.length > 0) output.push(`<pre>${escapeHtml(codeLines.join('\n'))}</pre>`)
  flushBlocks()
  return output.join('')
}
