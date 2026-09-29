import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const readProjectFile = (relativePath) =>
  readFileSync(path.join(projectRoot, relativePath), 'utf8');

test('Wokwi config points to the merged firmware and matching ELF', () => {
  const config = readProjectFile('firmware/wokwi.toml');
  assert.match(config, /firmware\s*=\s*['"][^'\"]*firmware-merged\.bin['"]/);
  assert.match(config, /elf\s*=\s*['"][^'\"]*firmware\.elf['"]/);
});

test('Wokwi diagram models the CYD display and inactive touch IRQ safely', () => {
  const diagram = JSON.parse(readProjectFile('firmware/diagram.json'));
  const types = new Set(diagram.parts.map((part) => part.type));
  assert.ok(types.has('board-esp32-devkit-c-v4'));
  assert.ok(types.has('wokwi-ili9341'));
  assert.ok(types.has('wokwi-resistor'));

  const connections = new Set(diagram.connections.map(([from, to]) => `${from}|${to}`));
  for (const [from, to] of [
    ['esp:13', 'tft:MOSI'],
    ['esp:14', 'tft:SCK'],
    ['esp:15', 'tft:CS'],
    ['esp:2', 'tft:D/C'],
    ['esp:12', 'tft:MISO'],
    ['esp:VP', 'irq_pullup:1'],
    ['irq_pullup:2', 'esp:3V3'],
  ]) {
    assert.ok(connections.has(`${from}|${to}`), `${from} should connect to ${to}`);
  }
});

test('Wokwi uses a simulator-only app build and bypasses physical onboarding', () => {
  const platformio = readProjectFile('firmware/platformio.ini');
  const app = readProjectFile('firmware/src/ui/app.cpp');
  assert.match(platformio, /\[env:cyd28_ili9341_wokwi_app\][\s\S]*?-DWOKWI_SIMULATOR/);
  assert.match(app, /#if defined\(WOKWI_SIMULATOR\)[\s\S]*?calibration\.valid\s*=\s*true;[\s\S]*?setupSettings\.done\s*=\s*true;[\s\S]*?#endif/);
  assert.match(app, /\[WOKWI\] APP_UI_READY/);
});

test('Wokwi script converts its timeout seconds to CLI milliseconds', () => {
  const script = readProjectFile('scripts/test-wokwi.ps1');
  assert.match(script, /\$timeoutMilliseconds\s*=\s*\$TimeoutSeconds\s*\*\s*1000/);
  assert.match(script, /--timeout\s+\$timeoutMilliseconds/);
});
