import './style.css';
import 'highlight.js/styles/github-dark.css';
import { renderMarkdown } from './markdown.js';
import { renderDiagramBlocks } from './renderers.js';
import sampleMarkdown from '../../diagram-sample.md?raw';

const editor = document.querySelector('#editor');
const preview = document.querySelector('#preview');
const status = document.querySelector('#render-status');
const fileInput = document.querySelector('#open-file');
const sourceName = document.querySelector('#source-name');
let timer;
let currentRender;

function updatePreview() {
  currentRender?.abort();
  currentRender = new AbortController();
  const signal = currentRender.signal;
  preview.innerHTML = renderMarkdown(editor.value);
  const count = preview.querySelectorAll('[data-diagram]').length;
  status.textContent = count ? count + '개 다이어그램 렌더링 중' : '렌더링 완료';
  renderDiagramBlocks(preview, signal).then(() => {
    if (!signal.aborted) status.textContent = count + '개 다이어그램 처리 완료';
  });
}

function schedulePreview() {
  clearTimeout(timer);
  timer = setTimeout(updatePreview, 300);
}

editor.addEventListener('input', schedulePreview);
fileInput.addEventListener('change', async () => {
  const file = fileInput.files?.[0];
  if (!file) return;
  editor.value = await file.text();
  sourceName.textContent = file.name;
  updatePreview();
});
document.querySelector('#load-sample').addEventListener('click', () => {
  editor.value = sampleMarkdown;
  sourceName.textContent = 'diagram-sample.md';
  updatePreview();
});
document.querySelector('#theme-toggle').addEventListener('click', () => {
  const dark = document.documentElement.classList.toggle('dark');
  document.querySelector('#theme-toggle').textContent = dark ? '라이트 모드' : '다크 모드';
  localStorage.setItem('mdviewer-theme', dark ? 'dark' : 'light');
});
document.querySelector('#print').addEventListener('click', () => window.print());
if (localStorage.getItem('mdviewer-theme') === 'dark') {
  document.documentElement.classList.add('dark');
  document.querySelector('#theme-toggle').textContent = '라이트 모드';
}
document.querySelector('#load-sample').click();
