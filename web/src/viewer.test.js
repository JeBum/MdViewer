// @vitest-environment jsdom
import { beforeEach, describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { renderMarkdown } from './markdown.js';
import { renderDiagramBlocks } from './renderers.js';

const sample = readFileSync(join(process.cwd(), '..', 'diagram-sample.md'), 'utf8');

beforeEach(() => { document.body.innerHTML = '<article id="preview"></article>'; });

describe('저장된 다이어그램 샘플', () => {
  it('저장된 Mermaid 종합 샘플의 17개 블록을 감지한다', () => {
    const root = document.querySelector('#preview');
    root.innerHTML = renderMarkdown(sample);
    expect([...root.querySelectorAll('[data-diagram]')].map((block) => block.dataset.diagram))
      .toEqual(Array(17).fill('mermaid'));
    expect(root.querySelectorAll('.code-block')).toHaveLength(0);
    expect(root.textContent).toContain('KTS DBG');
  });

  it('샘플의 모든 Mermaid 블록을 등록 렌더러에 원문 그대로 전달한다', async () => {
    const root = document.querySelector('#preview');
    root.innerHTML = renderMarkdown(sample);
    const calls = [];
    const registry = Object.fromEntries(
      ['mermaid'].map((kind) => [kind, async (source, context) => {
        calls.push({ kind, source, option: context.option });
        return '<svg xmlns="http://www.w3.org/2000/svg"><text>ok</text></svg>';
      }]),
    );
    await renderDiagramBlocks(root, new AbortController().signal, registry);
    expect(calls).toHaveLength(17);
    expect(calls[0].source).toContain('flowchart LR');
    expect(calls[1].source).toContain('sequenceDiagram');
    expect(calls[2].source).toContain('classDiagram');
    expect(calls[16].source).toContain('block-beta');
    expect(root.querySelectorAll('.diagram-result svg')).toHaveLength(17);
    expect(root.querySelectorAll('.diagram-source')).toHaveLength(17);
  });

  it('graphviz 별칭과 간단한 sequence 펜스를 인식한다', () => {
    const root = document.querySelector('#preview');
    root.innerHTML = renderMarkdown('```graphviz\ndigraph G { A -> B }\n```\n\n```sequence\nA->>B: Hi\n```');
    expect([...root.querySelectorAll('[data-diagram]')].map((block) => block.dataset.diagram))
      .toEqual(['dot', 'sequence']);
  });
});

describe('안전한 출력과 실패 복구', () => {
  it('Markdown HTML을 실행하지 않고 인식하지 못한 펜스는 코드로 표시한다', () => {
    const html = renderMarkdown('<script>alert(1)</script>\n\n```unknown\n<img src=x onerror=alert(1)>\n```');
    expect(html).not.toContain('<script>');
    expect(html).not.toContain('<img');
    expect(html).toContain('&lt;img');
    expect(html).toContain('code-block');
  });

  it('렌더링 실패 시 오류와 원문을 표시한다', async () => {
    const root = document.querySelector('#preview');
    root.innerHTML = renderMarkdown('```mermaid\nbad diagram\n```');
    await renderDiagramBlocks(root, new AbortController().signal, {
      mermaid: async () => { throw new Error('구문 오류'); },
    });
    expect(root.querySelector('[role="alert"]').textContent).toContain('구문 오류');
    expect(root.querySelector('.diagram-source').textContent).toContain('bad diagram');
  });

  it('이전 렌더링이 취소되면 새 미리보기를 덮어쓰지 않는다', async () => {
    const root = document.querySelector('#preview');
    root.innerHTML = renderMarkdown('```dot\ndigraph G {}\n```');
    const controller = new AbortController();
    const pending = renderDiagramBlocks(root, controller.signal, {
      dot: async () => {
        await new Promise((resolve) => setTimeout(resolve, 10));
        return '<svg></svg>';
      },
    });
    controller.abort();
    await pending;
    expect(root.querySelector('.diagram-result')).toBeNull();
  });
});
