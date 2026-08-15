#!/usr/bin/env node
"use strict";

// Focus-safe WebView2 inspection bridge for the CuePilot development app.
// The inspectable launcher exposes WebView2 CDP on 127.0.0.1:9322. This process
// keeps one websocket open and presents a compact HTTP API on 127.0.0.1:9323.

const fs = require("node:fs");
const http = require("node:http");
const path = require("node:path");

const CDP_HOST = process.env.CUEPILOT_CDP_HOST || "127.0.0.1";
const CDP_PORT = Number(process.env.CUEPILOT_CDP_PORT || 9322);
const API_PORT = Number(process.env.CUEPILOT_CDP_API_PORT || 9323);
const TMP_DIR = path.join(__dirname, ".tmp");
const TMP_KEEP = Math.max(1, Number(process.env.CUEPILOT_CDP_TMP_KEEP || 20));
const LOG_KEEP = Math.max(20, Number(process.env.CUEPILOT_CDP_LOG_KEEP || 250));
const APP_TITLE = /^CuePilot(?: Dev)?$/i;
const DEV_URL = /^https?:\/\/(?:localhost|127\.0\.0\.1):1420(?:\/|$)/i;

if (typeof WebSocket !== "function") {
  throw new Error("This bridge needs Node.js 22 or newer (global WebSocket is unavailable).\n");
}

fs.mkdirSync(TMP_DIR, { recursive: true });

const sleep = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));
const messageOf = (error) => error instanceof Error ? error.message : String(error);
const clamp = (value, min, max) => Math.max(min, Math.min(max, Number(value)));

function pruneScreenshots() {
  let captures = [];
  try {
    captures = fs.readdirSync(TMP_DIR)
      .filter((name) => /^snap-.*\.(?:jpe?g|png|webp)$/i.test(name))
      .map((name) => ({ name, modified: fs.statSync(path.join(TMP_DIR, name)).mtimeMs }))
      .sort((left, right) => right.modified - left.modified);
  } catch {
    return;
  }

  for (const capture of captures.slice(TMP_KEEP)) {
    try {
      fs.unlinkSync(path.join(TMP_DIR, capture.name));
    } catch {
      // Scratch-file pruning is best effort.
    }
  }
}

function slimTarget(target) {
  return target ? {
    id: target.id,
    title: target.title,
    type: target.type,
    url: target.url,
  } : null;
}

function classifyTargets(targets) {
  const pages = targets.filter((target) =>
    target.type === "page" &&
    !String(target.url || "").startsWith("devtools://") &&
    !String(target.title || "").startsWith("DevTools"));

  const main = pages.find((target) => APP_TITLE.test(target.title || "") && DEV_URL.test(target.url || ""))
    || pages.find((target) => APP_TITLE.test(target.title || ""))
    || pages.find((target) => DEV_URL.test(target.url || ""))
    || pages[0]
    || null;

  return { main, pages };
}

async function fetchTargets() {
  const response = await fetch(`http://${CDP_HOST}:${CDP_PORT}/json`);
  if (!response.ok) throw new Error(`WebView2 CDP /json returned HTTP ${response.status}`);
  return response.json();
}

async function getMainTarget() {
  let lastError;
  for (let attempt = 0; attempt < 3; attempt += 1) {
    try {
      const targets = await fetchTargets();
      const { main } = classifyTargets(targets);
      if (!main) throw new Error("No CuePilot page target was returned by WebView2 CDP");
      if (!main.webSocketDebuggerUrl) throw new Error("The CuePilot target has no debugger websocket URL");
      return main;
    } catch (error) {
      lastError = error;
      if (attempt < 2) await sleep(350);
    }
  }
  throw lastError;
}

function formatRemoteObject(value) {
  if (!value) return "";
  if (Object.prototype.hasOwnProperty.call(value, "value")) {
    if (typeof value.value === "string") return value.value;
    try {
      return JSON.stringify(value.value);
    } catch {
      return String(value.value);
    }
  }
  return value.description || value.unserializableValue || value.type || "";
}

class CdpConnection {
  constructor() {
    this.ws = null;
    this.target = null;
    this.connecting = null;
    this.nextId = 0;
    this.pending = new Map();
    this.logs = [];
    this.generation = 0;
  }

  async connect() {
    if (this.ws?.readyState === WebSocket.OPEN) return;
    if (this.connecting) return this.connecting;
    this.connecting = this.#connect().finally(() => {
      this.connecting = null;
    });
    return this.connecting;
  }

  async #connect() {
    const target = await getMainTarget();
    if (this.ws) {
      try { this.ws.close(); } catch { /* no-op */ }
    }

    await new Promise((resolve, reject) => {
      const socket = new WebSocket(target.webSocketDebuggerUrl);
      let opened = false;
      const timer = setTimeout(() => {
        try { socket.close(); } catch { /* no-op */ }
        reject(new Error("Timed out connecting to the CuePilot CDP websocket"));
      }, 8000);

      socket.addEventListener("message", (event) => this.#onMessage(event));
      socket.addEventListener("close", () => this.#onClose());
      socket.addEventListener("error", () => {
        if (!opened) {
          clearTimeout(timer);
          reject(new Error("The CuePilot CDP websocket failed to open"));
        }
      });
      socket.addEventListener("open", async () => {
        opened = true;
        clearTimeout(timer);
        this.ws = socket;
        this.target = target;
        this.generation += 1;
        try {
          await Promise.all([
            this.#send("Runtime.enable", {}, 5000),
            this.#send("Log.enable", {}, 5000),
            this.#send("Page.enable", {}, 5000),
            this.#send("Accessibility.enable", {}, 5000),
          ]);
          resolve();
        } catch (error) {
          reject(error);
        }
      });
    });
  }

  #onMessage(event) {
    let payload;
    try {
      payload = JSON.parse(String(event.data));
    } catch {
      return;
    }

    if (payload.id) {
      const pending = this.pending.get(payload.id);
      if (!pending) return;
      this.pending.delete(payload.id);
      clearTimeout(pending.timer);
      if (payload.error) pending.reject(new Error(payload.error.message || JSON.stringify(payload.error)));
      else pending.resolve(payload);
      return;
    }

    const { method, params = {} } = payload;
    if (method === "Page.frameNavigated" && !params.frame?.parentId) {
      this.generation += 1;
      return;
    }

    if (method === "Runtime.consoleAPICalled") {
      const level = ["error", "assert"].includes(params.type) ? "error"
        : params.type === "warning" ? "warning"
          : params.type || "log";
      this.#pushLog({
        kind: "console",
        level,
        text: (params.args || []).map(formatRemoteObject).join(" "),
        timestamp: params.timestamp,
        stack: params.stackTrace?.callFrames?.[0],
      });
      return;
    }

    if (method === "Runtime.exceptionThrown") {
      const detail = params.exceptionDetails || {};
      this.#pushLog({
        kind: "exception",
        level: "error",
        text: detail.exception?.description || detail.text || "Unhandled exception",
        url: detail.url,
        line: Number.isInteger(detail.lineNumber) ? detail.lineNumber + 1 : undefined,
        column: Number.isInteger(detail.columnNumber) ? detail.columnNumber + 1 : undefined,
        timestamp: params.timestamp,
      });
      return;
    }

    if (method === "Log.entryAdded") {
      const entry = params.entry || {};
      this.#pushLog({
        kind: "log",
        level: entry.level || "log",
        text: entry.text || "",
        url: entry.url,
        line: entry.lineNumber,
        timestamp: entry.timestamp,
      });
    }
  }

  #pushLog(entry) {
    this.logs.push({ ...entry, generation: this.generation });
    if (this.logs.length > LOG_KEEP) this.logs.splice(0, this.logs.length - LOG_KEEP);
  }

  #onClose() {
    this.ws = null;
    this.target = null;
    for (const pending of this.pending.values()) {
      clearTimeout(pending.timer);
      pending.reject(new Error("The CuePilot CDP websocket closed"));
    }
    this.pending.clear();
  }

  #send(method, params = {}, timeoutMs = 30000) {
    if (this.ws?.readyState !== WebSocket.OPEN) {
      return Promise.reject(new Error("The CuePilot CDP websocket is not open"));
    }
    const id = ++this.nextId;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`${method} timed out after ${timeoutMs}ms`));
      }, timeoutMs);
      this.pending.set(id, { resolve, reject, timer });
      try {
        this.ws.send(JSON.stringify({ id, method, params }));
      } catch (error) {
        clearTimeout(timer);
        this.pending.delete(id);
        reject(error);
      }
    });
  }

  async command(method, params = {}, timeoutMs = 30000) {
    await this.connect();
    try {
      return await this.#send(method, params, timeoutMs);
    } catch (error) {
      if (!/closed|not open/i.test(messageOf(error))) throw error;
      this.ws = null;
      await this.connect();
      return this.#send(method, params, timeoutMs);
    }
  }

  close() {
    if (this.ws) {
      try { this.ws.close(); } catch { /* no-op */ }
    }
    this.#onClose();
  }
}

const connection = new CdpConnection();
const cdp = (method, params = {}, timeoutMs = 30000) => connection.command(method, params, timeoutMs);

async function evaluate(expression, { awaitPromise = true, timeoutMs = 30000, returnByValue = true } = {}) {
  const response = await cdp("Runtime.evaluate", {
    expression,
    awaitPromise,
    returnByValue,
    userGesture: true,
  }, timeoutMs);
  const result = response.result?.result || {};
  const exception = response.result?.exceptionDetails;
  if (exception) {
    return {
      error: exception.exception?.description || exception.text || "Evaluation failed",
    };
  }
  if (!returnByValue) return { objectId: result.objectId, description: result.description };
  return {
    value: Object.prototype.hasOwnProperty.call(result, "value") ? result.value : result.description,
  };
}

async function pageState() {
  return evaluate(`(() => {
    const visible = (element) => {
      const style = getComputedStyle(element);
      const rect = element.getBoundingClientRect();
      return style.display !== 'none' && style.visibility !== 'hidden' && Number(style.opacity) > 0 && rect.width > 0 && rect.height > 0;
    };
    const dialog = Array.from(document.querySelectorAll('[role="dialog"], dialog, .drawer'))
      .find(visible);
    const active = document.activeElement;
    return {
      title: document.title,
      url: location.href,
      location: location.pathname + location.search + location.hash,
      readyState: document.readyState,
      viewport: { width: innerWidth, height: innerHeight, devicePixelRatio },
      scroll: { x: Math.round(scrollX), y: Math.round(scrollY), maxY: Math.round(Math.max(0, document.documentElement.scrollHeight - innerHeight)) },
      elements: document.querySelectorAll('*').length,
      activeElement: active && active !== document.body ? {
        tag: active.tagName.toLowerCase(),
        id: active.id || null,
        label: active.getAttribute('aria-label') || active.getAttribute('name') || active.textContent?.trim().slice(0, 80) || null,
      } : null,
      dialog: dialog ? (dialog.getAttribute('aria-label') || dialog.querySelector('h1,h2,h3')?.textContent?.trim() || dialog.className || 'open') : null,
      bodyText: document.body?.innerText?.replace(/\\s+/g, ' ').trim().slice(0, 240) || '',
    };
  })()`);
}

function consoleLogs({ clear, level, limit = 30, all = false } = {}) {
  const wantedLevel = level ? String(level).toLowerCase() : null;
  const matching = wantedLevel
    ? connection.logs.filter((entry) => String(entry.level).toLowerCase() === wantedLevel)
    : connection.logs.slice();
  const stale = matching.filter((entry) => entry.generation !== connection.generation).length;
  const scoped = all ? matching : matching.filter((entry) => entry.generation === connection.generation);
  const count = clamp(limit || 30, 1, 250);
  const logs = scoped.slice(-count);
  if (clear) connection.logs = [];
  return {
    generation: connection.generation,
    stale,
    count: logs.length,
    totalBuffered: connection.logs.length,
    logs,
  };
}

const SELECTOR_HELPERS = String.raw`
  const attrEscape = (value) => String(value).replace(/\\/g, '\\\\').replace(/"/g, '\\"');
  const unique = (candidate) => {
    try { return document.querySelectorAll(candidate).length === 1; } catch { return false; }
  };
  const selectorFor = (element) => {
    if (element.id) {
      const candidate = '#' + CSS.escape(element.id);
      if (unique(candidate)) return candidate;
    }
    for (const attribute of ['data-testid', 'aria-label', 'name', 'title', 'placeholder']) {
      const value = element.getAttribute(attribute);
      if (!value) continue;
      const candidate = element.tagName.toLowerCase() + '[' + attribute + '="' + attrEscape(value) + '"]';
      if (unique(candidate)) return candidate;
    }
    const path = [];
    let current = element;
    while (current && current !== document.body && path.length < 5) {
      let part = current.tagName.toLowerCase();
      const stableClass = Array.from(current.classList || []).find((name) => /^[a-z][a-z0-9_-]{2,}$/i.test(name));
      if (stableClass) part += '.' + CSS.escape(stableClass);
      const siblings = current.parentElement ? Array.from(current.parentElement.children).filter((child) => child.tagName === current.tagName) : [];
      if (siblings.length > 1) part += ':nth-of-type(' + (siblings.indexOf(current) + 1) + ')';
      path.unshift(part);
      const candidate = path.join(' > ');
      if (unique(candidate)) return candidate;
      current = current.parentElement;
    }
    return path.join(' > ') || element.tagName.toLowerCase();
  };
  const isVisible = (element) => {
    const style = getComputedStyle(element);
    const rect = element.getBoundingClientRect();
    return style.display !== 'none' && style.visibility !== 'hidden' && Number(style.opacity) > 0 && rect.width > 0 && rect.height > 0;
  };
  const rectOf = (element) => {
    const rect = element.getBoundingClientRect();
    return { x: Math.round(rect.x), y: Math.round(rect.y), width: Math.round(rect.width), height: Math.round(rect.height) };
  };
  const labelOf = (element) => (
    element.getAttribute('aria-label') ||
    element.getAttribute('title') ||
    element.getAttribute('placeholder') ||
    element.labels?.[0]?.textContent ||
    element.textContent ||
    element.getAttribute('name') ||
    ''
  ).replace(/\\s+/g, ' ').trim();
`;

async function suggestSelectors(query, limit = 6) {
  const safeQuery = JSON.stringify(String(query || ""));
  const result = await evaluate(`(() => {
    ${SELECTOR_HELPERS}
    const query = ${safeQuery}.replace(/[#.\\[\\]:'"=()>+~*]/g, ' ').replace(/\\s+/g, ' ').trim().toLowerCase();
    const terms = query.split(' ').filter((term) => term.length > 1);
    const elements = Array.from(document.querySelectorAll('button,a,input,select,textarea,[role],[aria-label],[title]'));
    const matches = elements.map((element) => {
      const haystack = [labelOf(element), element.id, element.className, element.getAttribute('role')].join(' ').toLowerCase();
      const score = terms.reduce((total, term) => total + (haystack.includes(term) ? 1 : 0), 0);
      return { element, score };
    }).filter((entry) => entry.score > 0)
      .sort((left, right) => right.score - left.score)
      .slice(0, ${clamp(limit, 1, 10)});
    return matches.map(({ element }) => ({
      selector: selectorFor(element),
      label: labelOf(element).slice(0, 100),
      visible: isVisible(element),
    }));
  })()`);
  return result.value || [];
}

async function controlInventory({ selector, limit = 80, includeHidden = false } = {}) {
  const safeSelector = selector ? JSON.stringify(String(selector)) : "null";
  const safeLimit = clamp(limit, 1, 250);
  const result = await evaluate(`(() => {
    ${SELECTOR_HELPERS}
    const selector = ${safeSelector};
    const root = selector ? document.querySelector(selector) : document.body;
    if (!root) return { error: 'Selector not found: ' + selector };
    const query = 'button,a[href],input,select,textarea,summary,[role="button"],[role="tab"],[role="menuitem"],[role="switch"],[role="checkbox"],[tabindex]:not([tabindex="-1"])';
    const elements = [...(root.matches?.(query) ? [root] : []), ...root.querySelectorAll(query)];
    const controls = elements.map((element) => {
      const visible = isVisible(element);
      if (!${Boolean(includeHidden)} && !visible) return null;
      const states = [];
      if (element.disabled || element.getAttribute('aria-disabled') === 'true') states.push('disabled');
      if (element.checked || element.getAttribute('aria-checked') === 'true') states.push('checked');
      if (element.getAttribute('aria-selected') === 'true') states.push('selected');
      if (element.getAttribute('aria-expanded')) states.push('expanded=' + element.getAttribute('aria-expanded'));
      return {
        selector: selectorFor(element),
        role: element.getAttribute('role') || element.tagName.toLowerCase(),
        label: labelOf(element).slice(0, 120),
        state: states.join(', '),
        visible,
        shortcut: element.getAttribute('aria-keyshortcuts') || null,
        rect: rectOf(element),
      };
    }).filter(Boolean);
    return { total: controls.length, controls: controls.slice(0, ${safeLimit}), truncated: controls.length > ${safeLimit} };
  })()`);
  if (result.value?.error && selector) {
    return { ...result.value, suggestions: await suggestSelectors(selector) };
  }
  return result.value || result;
}

async function findElements({ query, limit = 15 } = {}) {
  if (!query) return { error: "A search query is required" };
  const safeQuery = JSON.stringify(String(query).toLowerCase());
  const safeLimit = clamp(limit, 1, 50);
  const result = await evaluate(`(() => {
    ${SELECTOR_HELPERS}
    const query = ${safeQuery};
    const candidates = Array.from(document.querySelectorAll('button,a,input,select,textarea,[role],[aria-label],[title],h1,h2,h3,label'));
    const matches = candidates.filter((element) => {
      const text = [labelOf(element), element.id, element.getAttribute('name'), element.getAttribute('role')].join(' ').toLowerCase();
      return text.includes(query);
    }).slice(0, ${safeLimit}).map((element) => ({
      selector: selectorFor(element),
      tag: element.tagName.toLowerCase(),
      role: element.getAttribute('role') || null,
      label: labelOf(element).slice(0, 140),
      visible: isVisible(element),
      disabled: Boolean(element.disabled || element.getAttribute('aria-disabled') === 'true'),
      rect: rectOf(element),
    }));
    return { count: matches.length, matches };
  })()`);
  return result.value || result;
}

async function renderedText({ selector, maxChars = 5000 } = {}) {
  const safeSelector = selector ? JSON.stringify(String(selector)) : "null";
  const limit = clamp(maxChars, 100, 50000);
  const result = await evaluate(`(() => {
    const selector = ${safeSelector};
    const root = selector ? document.querySelector(selector) : document.body;
    if (!root) return { error: 'Selector not found: ' + selector };
    const text = (root.innerText || root.textContent || '').replace(/\\r/g, '').replace(/[ \\t]+\\n/g, '\\n').trim();
    return { text: text.slice(0, ${limit}), totalChars: text.length, truncated: text.length > ${limit} };
  })()`);
  if (result.value?.error && selector) return { ...result.value, suggestions: await suggestSelectors(selector) };
  return result.value || result;
}

async function measureElement({ selector, children = true } = {}) {
  if (!selector) return { error: "A selector is required" };
  const safeSelector = JSON.stringify(String(selector));
  const result = await evaluate(`(() => {
    const root = document.querySelector(${safeSelector});
    if (!root) return { error: 'Selector not found: ' + ${safeSelector} };
    const number = (value) => Math.round(Number.parseFloat(value) * 100) / 100;
    const describe = (element) => {
      const rect = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      return {
        tag: element.tagName.toLowerCase(),
        id: element.id || null,
        classes: Array.from(element.classList).slice(0, 8),
        rect: { x: number(rect.x), y: number(rect.y), width: number(rect.width), height: number(rect.height) },
        display: style.display,
        position: style.position,
        padding: style.padding,
        margin: style.margin,
        gap: style.gap,
        font: {
          family: style.fontFamily,
          size: style.fontSize,
          weight: style.fontWeight,
          lineHeight: style.lineHeight,
          letterSpacing: style.letterSpacing,
        },
        color: style.color,
        background: style.backgroundColor,
        border: style.border,
        radius: style.borderRadius,
        shadow: style.boxShadow,
        overflow: style.overflow,
        opacity: style.opacity,
      };
    };
    const output = { element: describe(root) };
    if (${Boolean(children)}) output.children = Array.from(root.children).slice(0, 24).map(describe);
    return output;
  })()`);
  if (result.value?.error) return { ...result.value, suggestions: await suggestSelectors(selector) };
  return result.value || result;
}

async function accessibilityTree({ selector, full = false, limit = 120 } = {}) {
  const safeLimit = clamp(limit, 10, 400);
  let response;
  if (selector) {
    const object = await evaluate(`document.querySelector(${JSON.stringify(String(selector))})`, { returnByValue: false });
    if (!object.objectId) {
      return { error: `Selector not found: ${selector}`, suggestions: await suggestSelectors(selector) };
    }
    response = await cdp("Accessibility.getPartialAXTree", { objectId: object.objectId, fetchRelatives: false }, 10000);
  } else {
    response = await cdp("Accessibility.getFullAXTree", { depth: 10 }, 10000);
  }

  const interestingRoles = new Set([
    "RootWebArea", "main", "navigation", "region", "dialog", "alert", "status",
    "heading", "button", "link", "textbox", "combobox", "listbox", "option",
    "checkbox", "radio", "switch", "tab", "tablist", "menu", "menuitem",
    "progressbar", "slider", "spinbutton", "img", "group",
  ]);
  const nodes = (response.result?.nodes || []).filter((node) => {
    if (node.ignored) return false;
    const role = node.role?.value || "unknown";
    const name = node.name?.value || "";
    return full || interestingRoles.has(role) || Boolean(name);
  }).map((node) => {
    const properties = Object.fromEntries((node.properties || []).map((property) => [property.name, property.value?.value]));
    const state = [
      properties.disabled ? "disabled" : null,
      properties.focused ? "focused" : null,
      properties.selected ? "selected" : null,
      properties.checked !== undefined ? `checked=${properties.checked}` : null,
      properties.expanded !== undefined ? `expanded=${properties.expanded}` : null,
      properties.pressed !== undefined ? `pressed=${properties.pressed}` : null,
    ].filter(Boolean).join(", ");
    return {
      role: node.role?.value || "unknown",
      name: node.name?.value || "",
      value: node.value?.value ?? null,
      state: state || null,
    };
  });
  return { count: Math.min(nodes.length, safeLimit), total: nodes.length, truncated: nodes.length > safeLimit, nodes: nodes.slice(0, safeLimit) };
}

async function clickElement(selector) {
  if (!selector) return { error: "A selector is required" };
  const safeSelector = JSON.stringify(String(selector));
  const located = await evaluate(`(async () => {
    const element = document.querySelector(${safeSelector});
    if (!element) return { error: 'Selector not found: ' + ${safeSelector} };
    if (element.disabled || element.getAttribute('aria-disabled') === 'true') {
      return { error: 'Element is disabled: ' + ${safeSelector}, disabled: true };
    }
    let rect = element.getBoundingClientRect();
    if (rect.top < 0 || rect.left < 0 || rect.bottom > innerHeight || rect.right > innerWidth) {
      element.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
      await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
      rect = element.getBoundingClientRect();
    }
    if (rect.width <= 0 || rect.height <= 0) return { error: 'Element has no visible click area' };
    const x = Math.round(rect.left + rect.width / 2);
    const y = Math.round(rect.top + rect.height / 2);
    const hit = document.elementFromPoint(x, y);
    const covered = Boolean(hit && hit !== element && !element.contains(hit));
    return {
      x, y, covered,
      coveredBy: covered ? (hit.id ? '#' + hit.id : hit.tagName.toLowerCase() + (hit.className ? '.' + String(hit.className).trim().replace(/\\s+/g, '.') : '')) : null,
      tag: element.tagName.toLowerCase(),
      label: (element.getAttribute('aria-label') || element.textContent || '').replace(/\\s+/g, ' ').trim().slice(0, 100),
    };
  })()`);
  const point = located.value || located;
  if (point.error) return { ...point, suggestions: await suggestSelectors(selector) };
  if (point.covered) return { error: `Element is covered by ${point.coveredBy || "another element"}`, ...point };

  await cdp("Input.dispatchMouseEvent", { type: "mouseMoved", x: point.x, y: point.y }, 5000);
  await cdp("Input.dispatchMouseEvent", { type: "mousePressed", x: point.x, y: point.y, button: "left", buttons: 1, clickCount: 1 }, 5000);
  await cdp("Input.dispatchMouseEvent", { type: "mouseReleased", x: point.x, y: point.y, button: "left", buttons: 0, clickCount: 1 }, 5000);
  return { ok: true, via: "pointer", ...point };
}

const KEY_DEFINITIONS = {
  Escape: { code: "Escape", key: "Escape", windowsVirtualKeyCode: 27 },
  Enter: { code: "Enter", key: "Enter", windowsVirtualKeyCode: 13 },
  Tab: { code: "Tab", key: "Tab", windowsVirtualKeyCode: 9 },
  Backspace: { code: "Backspace", key: "Backspace", windowsVirtualKeyCode: 8 },
  Delete: { code: "Delete", key: "Delete", windowsVirtualKeyCode: 46 },
  ArrowUp: { code: "ArrowUp", key: "ArrowUp", windowsVirtualKeyCode: 38 },
  ArrowDown: { code: "ArrowDown", key: "ArrowDown", windowsVirtualKeyCode: 40 },
  ArrowLeft: { code: "ArrowLeft", key: "ArrowLeft", windowsVirtualKeyCode: 37 },
  ArrowRight: { code: "ArrowRight", key: "ArrowRight", windowsVirtualKeyCode: 39 },
  Home: { code: "Home", key: "Home", windowsVirtualKeyCode: 36 },
  End: { code: "End", key: "End", windowsVirtualKeyCode: 35 },
  PageUp: { code: "PageUp", key: "PageUp", windowsVirtualKeyCode: 33 },
  PageDown: { code: "PageDown", key: "PageDown", windowsVirtualKeyCode: 34 },
  Space: { code: "Space", key: " ", windowsVirtualKeyCode: 32 },
};
const MODIFIERS = { alt: 1, ctrl: 2, control: 2, meta: 4, cmd: 4, win: 4, shift: 8 };

function resolveKey(input) {
  const pieces = String(input || "").split("+");
  const key = pieces.pop();
  let modifiers = 0;
  for (const modifier of pieces) {
    const bit = MODIFIERS[modifier.toLowerCase()];
    if (!bit) return { error: `Unsupported modifier: ${modifier}` };
    modifiers |= bit;
  }

  if (KEY_DEFINITIONS[key]) return { ...KEY_DEFINITIONS[key], modifiers };
  if (/^[a-z]$/i.test(key)) {
    const upper = key.toUpperCase();
    return { code: `Key${upper}`, key: modifiers & 8 ? upper : key.toLowerCase(), windowsVirtualKeyCode: upper.charCodeAt(0), modifiers };
  }
  if (/^[0-9]$/.test(key)) {
    return { code: `Digit${key}`, key, windowsVirtualKeyCode: 48 + Number(key), modifiers };
  }
  return { error: `Unsupported key: ${input}` };
}

async function pressKey(input) {
  const definition = resolveKey(input);
  if (definition.error) return definition;
  const { modifiers, ...key } = definition;
  await cdp("Input.dispatchKeyEvent", { type: "rawKeyDown", modifiers, ...key }, 5000);
  await cdp("Input.dispatchKeyEvent", { type: "keyUp", modifiers, ...key }, 5000);
  return { ok: true, key: input, modifiers };
}

async function typeText({ selector, text = "", key } = {}) {
  if (!selector) return { error: "A selector is required" };
  const clicked = await clickElement(selector);
  if (clicked.error) return clicked;
  await cdp("Input.insertText", { text: String(text) }, 10000);
  if (key) {
    const keyed = await pressKey(key);
    if (keyed.error) return keyed;
  }
  return { ok: true, selector, chars: String(text).length, key: key || null };
}

async function waitFor({ expression, timeoutMs = 30000, intervalMs = 150 } = {}) {
  if (!expression) return { error: "An expression is required" };
  const started = Date.now();
  const timeout = clamp(timeoutMs, 100, 120000);
  while (Date.now() - started <= timeout) {
    const result = await evaluate(`Boolean(${expression})`, { timeoutMs: Math.min(5000, timeout) });
    if (result.error) return result;
    if (result.value) return { ok: true, waitedMs: Date.now() - started };
    await sleep(clamp(intervalMs, 25, 2000));
  }
  return { error: `Condition did not become true within ${timeout}ms`, waitedMs: Date.now() - started };
}

async function settleQuiet({ quietMs = 140, maxMs = 1600 } = {}) {
  const quiet = clamp(quietMs, 50, 1000);
  const maximum = clamp(maxMs, quiet, 10000);
  const result = await evaluate(`new Promise((resolve) => {
    const started = performance.now();
    let lastMutation = started;
    let mutations = 0;
    const observer = new MutationObserver((records) => {
      mutations += records.length;
      lastMutation = performance.now();
    });
    observer.observe(document.documentElement, { childList: true, subtree: true, attributes: true, characterData: true });
    const check = () => {
      const now = performance.now();
      if (now - lastMutation >= ${quiet}) {
        observer.disconnect();
        resolve({ quiet: true, waitedMs: Math.round(now - started), mutations });
      } else if (now - started >= ${maximum}) {
        observer.disconnect();
        resolve({ quiet: false, waitedMs: Math.round(now - started), mutations });
      } else {
        setTimeout(check, 25);
      }
    };
    setTimeout(check, ${quiet});
  })`, { timeoutMs: maximum + 2500 });
  return result.value || result;
}

async function captureScreenshot({ selector, format = "jpeg", quality = 78 } = {}) {
  const normalizedFormat = ["jpeg", "png", "webp"].includes(format) ? format : "jpeg";
  const params = {
    format: normalizedFormat,
    fromSurface: true,
    captureBeyondViewport: false,
  };
  if (normalizedFormat !== "png") params.quality = clamp(quality, 1, 100);

  let dimensions;
  if (selector) {
    const safeSelector = JSON.stringify(String(selector));
    const located = await evaluate(`(async () => {
      const element = document.querySelector(${safeSelector});
      if (!element) return { error: 'Selector not found: ' + ${safeSelector} };
      let rect = element.getBoundingClientRect();
      if (rect.top < 0 || rect.left < 0 || rect.bottom > innerHeight || rect.right > innerWidth) {
        element.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
        await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
        rect = element.getBoundingClientRect();
      }
      return {
        x: rect.left + scrollX,
        y: rect.top + scrollY,
        width: rect.width,
        height: rect.height,
      };
    })()`);
    dimensions = located.value || located;
    if (dimensions.error) return { ...dimensions, suggestions: await suggestSelectors(selector) };
    if (dimensions.width <= 0 || dimensions.height <= 0) return { error: "Selector has no visible capture area" };
    params.captureBeyondViewport = true;
    params.clip = { ...dimensions, scale: 1 };
  } else {
    const metrics = await cdp("Page.getLayoutMetrics", {}, 5000);
    const viewport = metrics.result?.cssVisualViewport || metrics.result?.visualViewport;
    dimensions = viewport ? { width: viewport.clientWidth, height: viewport.clientHeight } : null;
  }

  const response = await cdp("Page.captureScreenshot", params, 20000);
  const data = response.result?.data;
  if (!data) return { error: "WebView2 returned no screenshot data" };
  const extension = normalizedFormat === "jpeg" ? "jpg" : normalizedFormat;
  const stamp = new Date().toISOString().replace(/[:.]/g, "-");
  const filename = `snap-${stamp}-${Math.random().toString(36).slice(2, 7)}.${extension}`;
  const outputPath = path.resolve(TMP_DIR, filename);
  fs.mkdirSync(TMP_DIR, { recursive: true });
  fs.writeFileSync(outputPath, Buffer.from(data, "base64"));
  pruneScreenshots();
  return {
    ok: true,
    path: outputPath,
    format: normalizedFormat,
    selector: selector || null,
    width: dimensions?.width ? Math.round(dimensions.width) : null,
    height: dimensions?.height ? Math.round(dimensions.height) : null,
  };
}

async function look({ selector, noShot = false, level = "error", limit = 30 } = {}) {
  const [pageResult, shot] = await Promise.all([
    pageState().catch((error) => ({ error: messageOf(error) })),
    noShot ? Promise.resolve(null) : captureScreenshot({ selector }).catch((error) => ({ error: messageOf(error) })),
  ]);
  const errors = consoleLogs({ level, limit });
  return {
    page: pageResult.value || pageResult,
    errorCount: errors.count,
    staleErrors: errors.stale,
    errors: errors.logs,
    screenshot: shot,
  };
}

async function runOperation({ op, params = {} }) {
  switch (op) {
    case "eval": return evaluate(params.expression || params.js, { timeoutMs: params.timeoutMs });
    case "click": return clickElement(params.selector);
    case "key": return pressKey(params.key);
    case "type": return typeText(params);
    case "wait": return waitFor(params);
    case "settle": return settleQuiet(params);
    case "page": return pageState();
    case "console": return consoleLogs(params);
    case "ax": return accessibilityTree(params);
    case "controls": return controlInventory(params);
    case "find": return findElements(params);
    case "text": return renderedText(params);
    case "measure": return measureElement(params);
    case "screenshot": return captureScreenshot(params);
    case "look": return look(params);
    default: return { error: `Unknown operation: ${op}` };
  }
}

function readJson(request) {
  return new Promise((resolve, reject) => {
    let body = "";
    request.on("data", (chunk) => {
      body += chunk;
      if (body.length > 1024 * 1024) request.destroy(new Error("Request body exceeds 1 MiB"));
    });
    request.on("end", () => {
      if (!body) return resolve({});
      try { resolve(JSON.parse(body)); } catch (error) { reject(error); }
    });
    request.on("error", reject);
  });
}

const routes = {
  "GET /health": async () => {
    try {
      const target = await getMainTarget();
      const started = Date.now();
      const ping = await evaluate("1", { timeoutMs: 3000 });
      return {
        ok: ping.value === 1,
        target: slimTarget(target),
        pingMs: Date.now() - started,
        websocketOpen: connection.ws?.readyState === WebSocket.OPEN,
        generation: connection.generation,
        logsBuffered: connection.logs.length,
      };
    } catch (error) {
      return {
        ok: false,
        error: messageOf(error),
        hint: "Start the app with `npm run cdp:dev`, then start this wrapper with `npm run cdp:serve`.",
      };
    }
  },
  "GET /targets": async () => {
    try {
      const targets = await fetchTargets();
      const { main, pages } = classifyTargets(targets);
      return { main: slimTarget(main), count: pages.length, pages: pages.map(slimTarget) };
    } catch (error) {
      return { error: messageOf(error) };
    }
  },
  "GET /page": async () => pageState(),
  "GET /console": async (_body, query) => consoleLogs(query),
  "POST /eval": async (body) => evaluate(body.expression || body.js, { timeoutMs: body.timeoutMs }),
  "POST /click": async (body) => clickElement(body.selector),
  "POST /key": async (body) => pressKey(body.key),
  "POST /type": async (body) => typeText(body),
  "POST /wait": async (body) => waitFor(body),
  "POST /settle": async (body) => settleQuiet(body),
  "POST /ax": async (body) => accessibilityTree(body),
  "POST /controls": async (body) => controlInventory(body),
  "POST /find": async (body) => findElements(body),
  "POST /text": async (body) => renderedText(body),
  "POST /measure": async (body) => measureElement(body),
  "POST /screenshot": async (body) => captureScreenshot(body),
  "POST /look": async (body) => look(body),
  "POST /batch": async ({ operations = [], parallel = false }) => {
    const started = Date.now();
    const results = parallel
      ? await Promise.all(operations.map((operation) => runOperation(operation).catch((error) => ({ error: messageOf(error) }))))
      : [];
    if (!parallel) {
      for (const operation of operations) {
        try { results.push(await runOperation(operation)); }
        catch (error) { results.push({ error: messageOf(error) }); }
      }
    }
    return { results, elapsedMs: Date.now() - started };
  },
  "POST /reload": async () => {
    await cdp("Page.reload", { ignoreCache: true }, 10000);
    return { ok: true };
  },
  "POST /shutdown": async () => {
    setTimeout(() => {
      connection.close();
      server.close(() => process.exit(0));
    }, 100);
    return { ok: true };
  },
};

const server = http.createServer(async (request, response) => {
  const url = new URL(request.url, `http://127.0.0.1:${API_PORT}`);
  const route = routes[`${request.method} ${url.pathname}`];
  response.setHeader("Content-Type", "application/json; charset=utf-8");
  response.setHeader("Cache-Control", "no-store");
  if (!route) {
    response.statusCode = 404;
    response.end(JSON.stringify({ error: `No route for ${request.method} ${url.pathname}` }));
    return;
  }

  try {
    const body = request.method === "POST" ? await readJson(request) : {};
    const query = Object.fromEntries(url.searchParams.entries());
    const result = await route(body, query);
    response.statusCode = 200;
    response.end(JSON.stringify(result));
  } catch (error) {
    response.statusCode = 500;
    response.end(JSON.stringify({ error: messageOf(error) }));
  }
});

server.listen(API_PORT, "127.0.0.1", () => {
  console.log(`[cuepilot-cdp] API listening on http://127.0.0.1:${API_PORT}`);
  console.log(`[cuepilot-cdp] WebView2 target at http://${CDP_HOST}:${CDP_PORT}`);
  pruneScreenshots();
  connection.connect()
    .then(() => console.log(`[cuepilot-cdp] connected to ${connection.target?.url}`))
    .catch((error) => console.log(`[cuepilot-cdp] initial connection pending: ${messageOf(error)}`));
});

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.on(signal, () => {
    connection.close();
    server.close(() => process.exit(0));
  });
}
