import DOMPurify from 'dompurify';

let mermaidInstance;
let graphvizInstance;
let nextDiagramId = 0;

function cleanSvg(svg) {
  const clean = DOMPurify.sanitize(svg, { USE_PROFILES: { svg: true, svgFilters: true } });
  if (!clean.includes('<svg')) throw new Error('렌더러가 유효한 SVG를 반환하지 않았습니다.');
  return clean;
}

async function mermaid(source) {
  if (!mermaidInstance) {
    const { default: library } = await import('mermaid');
    library.initialize({
      startOnLoad: false,
      securityLevel: 'strict',
      theme: 'default',
      flowchart: { htmlLabels: false },
    });
    mermaidInstance = library;
  }
  const renderId = 'diagram-' + (++nextDiagramId);
  try {
    const { svg } = await mermaidInstance.render(renderId, source);
    return cleanSvg(svg);
  } finally {
    // Mermaid는 파싱 실패 시 body에 오류 SVG를 남기므로 임시 노드를 정리한다.
    document.getElementById('d' + renderId)?.remove();
  }
}

async function graphviz(source) {
  if (!graphvizInstance) {
    const { Graphviz } = await import('@hpcc-js/wasm');
    graphvizInstance = await Graphviz.load();
  }
  return cleanSvg(graphvizInstance.layout(source, 'svg', 'dot'));
}

// 새 문법은 이 레지스트리에 렌더러 함수를 추가하면 된다.
export const renderers = {
  mermaid: (source) => mermaid(source),
  sequence: (source) => mermaid(/^\s*sequenceDiagram\b/.test(source) ? source : 'sequenceDiagram\n' + source),
  dot: (source) => graphviz(source),
};

export async function renderDiagramBlocks(root, signal, registry = renderers) {
  document.querySelectorAll('body > div[id^="ddiagram-"]').forEach((element) => element.remove());
  const blocks = [...root.querySelectorAll('[data-diagram]')];
  await Promise.all(blocks.map(async (block) => {
    const kind = block.dataset.diagram;
    const source = block.querySelector('.diagram-source')?.textContent ?? '';
    const renderer = registry[kind];
    try {
      if (!renderer) throw new Error('지원하지 않는 다이어그램: ' + kind);
      const result = await renderer(source, { signal, option: block.dataset.option ?? '' });
      if (signal.aborted || !block.isConnected) return;
      block.innerHTML = '<div class="diagram-result">' + cleanSvg(result) +
        '</div><details><summary>원문 보기</summary><pre class="diagram-source"></pre></details>';
      block.querySelector('.diagram-source').textContent = source;
    } catch (error) {
      if (signal.aborted || !block.isConnected) return;
      block.querySelector('.diagram-loading')?.remove();
      const notice = document.createElement('div');
      notice.className = 'diagram-error';
      notice.setAttribute('role', 'alert');
      notice.textContent = '다이어그램을 그릴 수 없습니다: ' + error.message;
      block.prepend(notice);
    }
  }));
}
