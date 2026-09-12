const { test } = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const { checkLinks, headings, references } = require('./check-docs.cjs');

test('extracts local links, images, reference definitions, and HTML without code samples', () => {
  assert.deepEqual(references('[Guide](docs/guide.md#start) ![image](image.png)\n[ref]: <with spaces.md>\n<img src="icon.png">\n`[ignored](missing)`\n```md\n[ignored](missing)\n```'),
    ['docs/guide.md#start', 'image.png', 'with spaces.md', 'icon.png']);
});

test('supports Unicode and duplicate Markdown heading anchors', () => {
  assert.deepEqual([...headings('# Hello, World!\n## Hello, World!\n## Café\n')], ['hello-world', 'hello-world-1', 'café']);
});

test('reports missing paths and anchors while ignoring external links', () => {
  const first = path.resolve('virtual', 'README.md');
  const second = path.resolve('virtual', 'guide.md');
  const sources = new Map([
    [first, '[ok](guide.md#start) [bad](guide.md#absent) [missing](lost.md) [external](https://example.com)'],
    [second, '# Start'],
  ]);
  const result = checkLinks([first, second], file => sources.get(file), file => sources.has(file));
  assert.equal(result.checked, 3);
  assert.equal(result.errors.length, 2);
  assert.match(result.errors[0], /missing heading/);
  assert.match(result.errors[1], /missing target/);
});

test('accepts encoded file paths and reports malformed encoding', () => {
  const first = path.resolve('virtual', 'README.md');
  const second = path.resolve('virtual', 'a b.md');
  const sources = new Map([[first, '[ok](a%20b.md#start) [bad](%ZZ.md)'], [second, '# Start']]);
  const result = checkLinks([first], file => sources.get(file), file => sources.has(file));
  assert.equal(result.checked, 1);
  assert.equal(result.errors.length, 1);
  assert.match(result.errors[0], /invalid URL encoding/);
});
