const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

class Element {
  constructor() { this.children = []; this.dataset = {}; this.attributes = {}; this.events = {}; this.classList = { toggle() {} }; this.lastElementChild = {}; }
  append(...nodes) { this.children.push(...nodes); }
  replaceChildren(...nodes) { this.children = nodes; }
  setAttribute(key, value) { this.attributes[key] = value; }
  removeAttribute(key) { delete this.attributes[key]; }
  addEventListener(name, action) { this.events[name] = action; }
}
function fixture(fetch) {
  const nodes = new Map();
  const context = vm.createContext({ fetch, console, setTimeout, clearTimeout, Intl, crypto: { randomUUID: () => 'command-1' }, document: {
    getElementById(id) { if (!nodes.has(id)) nodes.set(id, new Element()); return nodes.get(id); },
    querySelectorAll() { return []; }, createElement: () => new Element(), createElementNS: () => new Element()
  } });
  const source = fs.readFileSync(path.join(__dirname, '../../src/MS3DPRINT.Manager.Server/wwwroot/app.js'), 'utf8');
  vm.runInContext(source.slice(0, source.indexOf('$("login-form").addEventListener')), context);
  return { context, nodes, run: code => vm.runInContext(code, context) };
}
test('lost response body preserves the command for receipt verification', async () => {
  let calls = 0;
  const ui = fixture(async () => ++calls === 1
    ? { ok: true, text: async () => JSON.stringify({ requestToken: 'csrf' }) }
    : { ok: true, text: async () => { throw new TypeError('connection closed after commit'); } });
  await ui.run('sendOperation({ path: "/api/v1/clients", method: "POST", body: { commandId: "committed-command" }, apply() {} })');
  assert.equal(ui.run('state.pending?.body.commandId'), 'committed-command');
  assert.equal(calls, 2);
});
test('task draft survives rerender of the same project', () => {
  const ui = fixture();
  ui.run('state.project = { id: "project-a", projectName: "Test", reference: "QA", tasks: [], priority: "None" }; renderProject();');
  const descendants = node => [node, ...node.children.flatMap(descendants)];
  const input = descendants(ui.nodes.get('detail-section')).find(node => node.attributes['aria-label'] === 'Nouvelle tâche');
  input.value = 'Brouillon après conflit'; input.events.input();
  ui.run('renderProject()');
  const refreshed = descendants(ui.nodes.get('detail-section')).find(node => node.attributes['aria-label'] === 'Nouvelle tâche');
  assert.equal(refreshed.value, 'Brouillon après conflit');
});
