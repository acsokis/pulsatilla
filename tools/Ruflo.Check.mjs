import { homedir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

const install = process.env.PULSATILLA_RUFLO_ROOT || join(homedir(), '.codex', 'tools', 'ruflo');
const sdk = join(install, 'node_modules', '@modelcontextprotocol', 'sdk', 'dist', 'esm', 'client');
const { Client } = await import(pathToFileURL(join(sdk, 'index.js')).href);
const { StdioClientTransport } = await import(pathToFileURL(join(sdk, 'stdio.js')).href);
const transport = new StdioClientTransport({ command: process.execPath,
  args: [join(install, 'node_modules', 'ruflo', 'bin', 'ruflo.js'), 'mcp', 'start'],
  cwd: process.cwd(), stderr: 'pipe' });
const client = new Client({ name: 'pulsatilla-ruflo-check', version: '1.0.0' });
try {
  await client.connect(transport);
  const { tools } = await client.listTools();
  const relevant = tools.filter(tool => /health|memory_store|memory_search|hooks_route|swarm_init|agent_spawn/.test(tool.name));
  console.log(JSON.stringify({ connected: true, toolCount: tools.length, relevant: relevant.map(tool => tool.name) }));
  const health = tools.find(tool => tool.name === 'system_health');
  if (health) console.log(JSON.stringify({ health: await client.callTool({ name: health.name, arguments: { fix: false } }) }));
  if (process.argv.includes('--remember')) {
    const value = 'Pulsatilla is the project name. MIT Community core; optional paid services separate. No configured external breach provider, telemetry or checkout. Product docs describe own features without competitor comparisons. Creator: Gábor Kocsis. Preserve old profiles via copy-only migration and legacy firewall removal aliases.';
    console.log(JSON.stringify({ stored: await client.callTool({ name: 'memory_store', arguments: {
      key: 'product-decisions', value, namespace: 'pulsatilla', provenance_type: 'user_claim', tags: ['branding', 'licensing', 'privacy'] } }) }));
    console.log(JSON.stringify({ found: await client.callTool({ name: 'memory_search', arguments: {
      query: 'Pulsatilla product-decisions MIT privacy branding', namespace: 'pulsatilla', limit: 1, threshold: 0 } }) }));
  }
} finally { await client.close(); }
