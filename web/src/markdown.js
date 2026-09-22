import MarkdownIt from 'markdown-it';
import DOMPurify from 'dompurify';
import hljs from 'highlight.js/lib/common';

// 외부 서버가 필요한 문법은 지원하지 않고 원문 코드 블록으로 표시한다.
const diagramTags = new Set(['mermaid', 'dot', 'graphviz', 'sequence']);
const markdown = new MarkdownIt({ html: false, linkify: true, typographer: true });
const escape = markdown.utils.escapeHtml;

// 요구사항: 등록된 언어 태그의 코드 펜스만 다이어그램으로 바꾸고, 나머지는 코드로 표시한다.
markdown.renderer.rules.fence = (tokens, index) => {
  const token = tokens[index];
  const [tag = '', option = ''] = token.info.trim().toLowerCase().split(/\s+/);
  if (diagramTags.has(tag)) {
    const kind = tag === 'graphviz' ? 'dot' : tag;
    return '<figure class="diagram" data-diagram="' + kind + '" data-option="' + escape(option) +
      '"><div class="diagram-loading" role="status"><span class="spinner" aria-hidden="true"></span>다이어그램 렌더링 중…</div>' +
      '<pre class="diagram-source">' + escape(token.content) + '</pre></figure>';
  }
  const safeTag = /^[\w-]+$/.test(tag) ? tag : 'text';
  const code = hljs.getLanguage(safeTag)
    ? hljs.highlight(token.content, { language: safeTag, ignoreIllegals: true }).value
    : escape(token.content);
  return '<pre class="code-block"><code class="hljs language-' + safeTag + '">' + code + '</code></pre>';
};

export function renderMarkdown(source) {
  return DOMPurify.sanitize(markdown.render(source));
}

export { diagramTags };
