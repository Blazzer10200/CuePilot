// Repository-local Markdown links only. No packages, network, or app startup.
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');

function prose(text) {
  return text.replace(/^\s*(`{3,}|~{3,}).*\r?\n[\s\S]*?^\s*\1\s*$/gm, '')
    .replace(/<!--[^]*?-->/g, '');
}

function headings(text) {
  const counts = new Map();
  const ids = new Set();
  for (const match of prose(text).matchAll(/^ {0,3}#{1,6}\s+(.+?)\s*#*\s*$/gm)) {
    const slug = match[1].toLowerCase().replace(/<[^>]*>/g, '')
      .replace(/[^\p{L}\p{N}\p{M}_\-\s]/gu, '').replace(/\s/g, '-');
    const count = counts.get(slug) || 0;
    counts.set(slug, count + 1);
    ids.add(count ? `${slug}-${count}` : slug);
  }
  for (const match of text.matchAll(/\b(?:id|name)=["']([^"']+)["']/g)) ids.add(match[1]);
  return ids;
}

function references(text) {
  const clean = prose(text).replace(/`[^`\n]+`/g, '');
  const links = [];
  // Inline links/images and reference definitions; optional titles are ignored.
  const pattern = /!?\[[^\]\n]*\]\(\s*(?:<([^>]+)>|([^\s)]+))(?:\s+["'][^\n]*?["'])?\s*\)|^ {0,3}\[[^\]\n]+\]:\s*(?:<([^>]+)>|(\S+))/gm;
  for (const match of clean.matchAll(pattern)) {
    links.push(match[1] || match[2] || match[3] || match[4]);
  }
  for (const match of clean.matchAll(/<(?:img|a)\b[^>]*?\b(?:src|href)=["']([^"']+)["']/g)) links.push(match[1]);
  return links;
}

function checkLinks(files, read = file => fs.readFileSync(file, 'utf8'), exists = fs.existsSync) {
  const errors = [];
  let checked = 0;
  for (const file of files) {
    for (const link of references(read(file))) {
      if (/^(?:[a-z][a-z\d+.-]*:|\/\/)/i.test(link)) continue;
      const [destination, fragment] = link.split('#', 2);
      let target, anchor;
      try {
        target = destination ? path.resolve(path.dirname(file), decodeURIComponent(destination.split('?')[0])) : file;
        anchor = fragment ? decodeURIComponent(fragment) : '';
      } catch {
        errors.push(`${file}: invalid URL encoding in ${link}`);
        continue;
      }
      checked++;
      if (!exists(target)) errors.push(`${file}: missing target ${link}`);
      else if (anchor && /\.md$/i.test(target) && !headings(read(target)).has(anchor)) {
        errors.push(`${file}: missing heading ${link}`);
      }
    }
  }
  return { checked, errors };
}

if (require.main === module) {
  const root = path.resolve(__dirname, '..');
  const files = [...new Set(execFileSync('git', ['ls-files', '--cached', '--others', '--exclude-standard', '-z'], { cwd: root, encoding: 'utf8' }).split('\0'))]
    .filter(file => /\.md$/i.test(file)).map(file => path.join(root, file));
  const { checked, errors } = checkLinks(files);
  for (const error of errors) console.error(error);
  console.log(`Documentation: ${files.length} Markdown files, ${checked} local links, ${errors.length} errors.`);
  process.exitCode = errors.length ? 1 : 0;
}

module.exports = { headings, references, checkLinks };
