// Exact, reviewable literal replacements. Never translates identifiers, INI keys,
// paths, numeric settings, conditions, or C#/C++ code outside string literals.
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const base = 'v0.2.16';
const tracked = execFileSync('git', ['ls-tree', '-r', '--name-only', base], { cwd: root, encoding: 'utf8' }).trim().split('\n');
const sourceFiles = tracked.filter(f =>
  /^apps\/launcher\/src\/.*\.cs$/.test(f) ||
  /^installer\/msi\/(MsiActions|MsiStorage|GamePackage|LegacyMigration|LegacyMigrationTests)\.cs$/.test(f) ||
  f === 'installer/single-game/SuiteActions.cs' ||
  /^src\/.*\.(cpp|h)$/.test(f));
const xmlFiles = ['Interface.wxs', 'Package.wxs', 'Folder.wxs.in', 'Game.wxs.in'].map(f => 'installer/single-game/' + f);

function literals(source) {
  const tokens = [];
  let i = 0;
  while (i < source.length) {
    if (source.startsWith('//', i)) { i = source.indexOf('\n', i); if (i < 0) break; continue; }
    if (source.startsWith('/*', i)) { const end = source.indexOf('*/', i + 2); if (end < 0) throw Error('Unclosed comment'); i = end + 2; continue; }
    const raw = source.slice(i).match(/^R"([^()\\\s]{0,16})\(/);
    if (raw) {
      const end = source.indexOf(')' + raw[1] + '"', i + raw[0].length);
      if (end < 0) throw Error('Unclosed raw string');
      i = end + raw[1].length + 2; continue;
    }
    const verbatim = source.startsWith('@"', i);
    if (source[i] === '"' || verbatim) {
      const start = i + (verbatim ? 2 : 1);
      let end = start;
      while (end < source.length) {
        if (source[end] === '"') {
          if (verbatim && source[end + 1] === '"') { end += 2; continue; }
          break;
        }
        end += !verbatim && source[end] === '\\' ? 2 : 1;
      }
      if (end >= source.length) throw Error('Unclosed string');
      tokens.push({ start, end, text: source.slice(start, end), verbatim });
      i = end + 1; continue;
    }
    if (source[i] === "'") {
      // C++ digit separators are not character literals.
      if (/[0-9]/.test(source[i - 1] || '') && /[0-9]/.test(source[i + 1] || '')) { i++; continue; }
      i++;
      while (i < source.length && source[i] !== "'") i += source[i] === '\\' ? 2 : 1;
      i++; continue;
    }
    i++;
  }
  return tokens;
}

const mode = process.argv[2] || 'inventory';
const mapPath = path.join(root, 'localization/en-US.json');
const map = fs.existsSync(mapPath) ? JSON.parse(fs.readFileSync(mapPath, 'utf8')) : {};
const matches = new Map();
let replacements = 0;
let verified = 0;
function readSource(file) {
  return mode === 'verify'
    ? execFileSync('git', ['show', `${base}:${file}`], { cwd: root, encoding: 'utf8', maxBuffer: 8 * 1024 * 1024 })
    : fs.readFileSync(path.join(root, file), 'utf8');
}
function verifySource(file, expected) {
  const actual = fs.readFileSync(path.join(root, file), 'utf8');
  const normalize = value => value.replace(/\r\n/g, '\n');
  if (normalize(actual) !== normalize(expected)) throw Error('Non-localization change or missing translation: ' + file);
  verified++;
}
function record(text, file, source, offset) {
  if (!matches.has(text)) matches.set(text, []);
  matches.get(text).push(`${file}:${source.slice(0, offset).split('\n').length}`);
}
for (const file of sourceFiles) {
  const source = readSource(file);
  const tokens = literals(source);
  let result = source;
  for (const token of [...tokens].reverse()) {
    record(token.text, file, source, token.start);
    if (!Object.hasOwn(map, token.text)) continue;
    const translated = map[token.text];
    if (typeof translated !== 'string') throw Error('Invalid translation: ' + token.text);
    // Preserve printf/.NET formatting and escaped line breaks exactly.
    const placeholders = text => (text.match(file.endsWith('.cs')
      ? /\{\d+(?:[^{}]*)\}|\\[rn]/g
      : /%(?:\d+\$)?[-+ #0]*(?:\d+|\*)?(?:\.(?:\d+|\*))?(?:ll|I64|z|l|h)?[diuoxXfFeEgGaAcsp%]|\{\d+(?:[^{}]*)\}|\\[rn]/g) || []).join('|');
    if (placeholders(token.text) !== placeholders(translated)) throw Error('Placeholder mismatch: ' + token.text);
    if (!token.verbatim && /(?<!\\)"/.test(translated)) throw Error('Unescaped quote: ' + translated);
    result = result.slice(0, token.start) + translated + result.slice(token.end);
    replacements++;
  }
  if (mode === 'apply' && result !== source) fs.writeFileSync(path.join(root, file), result, 'utf8');
  if (mode === 'verify') verifySource(file, result);
}
for (const file of xmlFiles) {
  const source = readSource(file);
  const result = source.replace(/\b(Text|Title|Description|Value|Message|ProductName|Name)="([^"]*)"/g, (whole, attr, text, offset) => {
    record(text, file, source, offset);
    if (!Object.hasOwn(map, text)) return whole;
    if (/["<>]/.test(map[text])) throw Error('Unsafe XML translation: ' + text);
    replacements++;
    return `${attr}="${map[text]}"`;
  });
  if (mode === 'apply' && result !== source) fs.writeFileSync(path.join(root, file), result, 'utf8');
  if (mode === 'verify') verifySource(file, file.endsWith('/Package.wxs') ? result.replace('Language="3082"', 'Language="1033"') : result);
}
const likelySpanish = /[áéíóúñÁÉÍÓÚÑ¿¡]|\b(?:no se|se ha|guardar|guardado|visor|lanzador|instala|instalar|archivos|del |para |por |valor|modo|calidad|resolucion|reinicio|cambiar|espera|anterior|siguiente|esta |este |tiene|hay |sin |con |de |el |la |los |las |tus |su |sus |opciones|sombras|reflejos|desactivado|activado|si|alto|bajo|pendiente|error al|preparando|leyendo|ocultar|actual|comprobando|selecciona|adelante|atras|aceptar|finalizar|salir|recargar|abrir)\b/i;
if (mode === 'inventory') {
  const translatedValues = new Set(Object.values(map));
  const output = [...matches].filter(([text]) => !Object.hasOwn(map, text) && !translatedValues.has(text) && (process.argv.includes('--all') || likelySpanish.test(text)));
  const filter = process.argv[3];
  output.filter(([, files]) => !filter || files.some(f => f.includes(filter))).forEach(([text, files]) => console.log(JSON.stringify({ text, files: files.filter(f => !filter || f.includes(filter)) })));
  console.error(`Untranslated candidates: ${output.length}; literal occurrences scheduled: ${replacements}`);
} else if (mode === 'apply') {
  console.log(`Applied ${replacements} exact translations across C#/C++ literals and MSI display attributes.`);
} else if (mode === 'verify') {
  console.log(`PASS: ${verified} source/MSI files match ${base} exactly except approved literal translations, launcher version and MSI language. ${replacements} translated occurrences.`);
} else {
  throw Error('Unknown mode: ' + mode);
}
