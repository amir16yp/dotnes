import * as path from "path";
import * as vscode from "vscode";

export interface TileReferenceHit {
  uri: vscode.Uri;
  line: number; // 0-based
  score: number;
  reason: string;
  preview: string;
}

export interface FindTileReferencesArgs {
  tileIndex: number;
  /** Length-64 row-major palette indices 0–3. */
  pixels: number[];
  /** Directory of the CHR file — prefer nearby Program.cs / disasm.s. */
  chrUri: vscode.Uri;
}

/** Encode 8×8×2bpp pixels to the canonical 16 NES CHR bytes. */
export function encodeTileBytes(pixels: number[]): number[] {
  if (pixels.length !== 64) {
    throw new Error("Tile must have 64 pixels.");
  }
  const data = new Array<number>(16).fill(0);
  for (let y = 0; y < 8; y++) {
    let plane0 = 0;
    let plane1 = 0;
    for (let x = 0; x < 8; x++) {
      const v = pixels[y * 8 + x] & 3;
      const bit = 7 - x;
      if (v & 1) plane0 |= 1 << bit;
      if (v & 2) plane1 |= 1 << bit;
    }
    data[y] = plane0;
    data[8 + y] = plane1;
  }
  return data;
}

function hex2(n: number): string {
  return n.toString(16).toUpperCase().padStart(2, "0");
}

function isBlankTile(bytes: number[]): boolean {
  return bytes.every((b) => b === 0);
}

interface ByteSite {
  value: number;
  line: number; // 0-based
  index: number; // order among extracted bytes
}

/** Pull ordered byte literals and the line each appears on. */
function extractBytes(
  text: string,
  kind: "csharp" | "asm"
): ByteSite[] {
  const sites: ByteSite[] = [];
  const lines = text.split(/\r?\n/);
  const re =
    kind === "csharp"
      ? /\b0x([0-9A-Fa-f]{1,2})\b/g
      : /\$([0-9A-Fa-f]{2})(?![0-9A-Fa-f])/g;

  for (let line = 0; line < lines.length; line++) {
    const raw = lines[line];
    // Skip pure comments for csharp index noise later; still allow fingerprint in arrays.
    re.lastIndex = 0;
    let m: RegExpExecArray | null;
    while ((m = re.exec(raw)) !== null) {
      sites.push({
        value: parseInt(m[1], 16),
        line,
        index: sites.length,
      });
    }
  }
  return sites;
}

function findFingerprintHits(
  uri: vscode.Uri,
  text: string,
  pattern: number[],
  kind: "csharp" | "asm",
  lines: string[]
): TileReferenceHit[] {
  if (isBlankTile(pattern)) {
    return [];
  }

  const sites = extractBytes(text, kind);
  const hits: TileReferenceHit[] = [];
  const seenLines = new Set<number>();

  for (let i = 0; i <= sites.length - pattern.length; i++) {
    let ok = true;
    for (let j = 0; j < pattern.length; j++) {
      if (sites[i + j].value !== pattern[j]) {
        ok = false;
        break;
      }
    }
    if (!ok) continue;

    const startLine = sites[i].line;
    const endLine = sites[i + pattern.length - 1].line;
    // CHR tiles are usually packed; reject if the 16 bytes span a huge gap.
    if (endLine - startLine > 24) continue;
    if (seenLines.has(startLine)) continue;
    seenLines.add(startLine);

    hits.push({
      uri,
      line: startLine,
      score: 100,
      reason: "CHR byte fingerprint (16-byte match)",
      preview: lines[startLine]?.trim() ?? "",
    });
  }
  return hits;
}

function collectConstNames(text: string, indices: number[]): Map<string, number> {
  const names = new Map<string, number>();
  const lines = text.split(/\r?\n/);
  const indexSet = new Set(indices);

  // const byte SPR_PLAYER = 0x00;  |  const byte CH_FLOOR = 0xf4;
  const re =
    /\bconst\s+(?:byte|sbyte|int|uint|ushort|short)\s+(\w+)\s*=\s*(0x[0-9A-Fa-f]+|\d+)\s*;/;

  for (let line = 0; line < lines.length; line++) {
    const m = lines[line].match(re);
    if (!m) continue;
    const value = m[2].startsWith("0x") || m[2].startsWith("0X")
      ? parseInt(m[2].slice(2), 16)
      : parseInt(m[2], 10);
    if (indexSet.has(value & 0xff) || indexSet.has(value)) {
      names.set(m[1], line);
    }
  }
  return names;
}

function findProgramIndexHits(
  uri: vscode.Uri,
  text: string,
  tileIndex: number,
  lines: string[]
): TileReferenceHit[] {
  const hits: TileReferenceHit[] = [];
  const local = tileIndex & 0xff;
  const candidates = local === tileIndex ? [tileIndex] : [tileIndex, local];
  const hexForms = new Set(
    candidates.flatMap((n) => [
      `0x${hex2(n)}`,
      `0x${n.toString(16)}`,
      `0X${hex2(n)}`,
      `0X${n.toString(16)}`,
    ])
  );
  const decForms = new Set(candidates.map(String));

  const constNames = collectConstNames(text, candidates);
  for (const [name, line] of constNames) {
    hits.push({
      uri,
      line,
      score: 90,
      reason: `const ${name} = tile index`,
      preview: lines[line]?.trim() ?? "",
    });
  }

  // oam_spr(...) / oam.spr(...) — tile is the third argument
  const oamRe =
    /\boam(?:_|\.)spr\s*\(\s*[^,]+,\s*[^,]+,\s*([^\s,]+)\s*,/g;

  for (let line = 0; line < lines.length; line++) {
    const raw = lines[line];
    oamRe.lastIndex = 0;
    let m: RegExpExecArray | null;
    while ((m = oamRe.exec(raw)) !== null) {
      const arg = m[1];
      const hitConst = constNames.has(arg);
      const lit = arg.match(/^(0x[0-9A-Fa-f]+|\d+)$/i);
      let match = hitConst;
      if (lit) {
        const v = lit[1].toLowerCase().startsWith("0x")
          ? parseInt(lit[1].slice(2), 16)
          : parseInt(lit[1], 10);
        match = candidates.includes(v) || candidates.includes(v & 0xff);
      }
      if (match) {
        hits.push({
          uri,
          line,
          score: 85,
          reason: hitConst ? `oam_spr via ${arg}` : "oam_spr tile argument",
          preview: raw.trim(),
        });
      }
    }
  }

  // // Tile 0x00: … comments (shoot2 style)
  const commentRe = /Tile\s+\$?([0-9A-Fa-f]+)\b|Tile\s+0x([0-9A-Fa-f]+)\b/i;
  for (let line = 0; line < lines.length; line++) {
    const m = lines[line].match(commentRe);
    if (!m) continue;
    const v = parseInt(m[1] ?? m[2], 16);
    if (candidates.includes(v) || candidates.includes(v & 0xff)) {
      hits.push({
        uri,
        line,
        score: 80,
        reason: "tile comment",
        preview: lines[line].trim(),
      });
    }
  }

  // Named const usages elsewhere
  for (const name of constNames.keys()) {
    const useRe = new RegExp(`\\b${name}\\b`);
    for (let line = 0; line < lines.length; line++) {
      if (constNames.get(name) === line) continue;
      if (!useRe.test(lines[line])) continue;
      // Skip the const declaration line already added
      hits.push({
        uri,
        line,
        score: 75,
        reason: `uses const ${name}`,
        preview: lines[line].trim(),
      });
    }
  }

  // Metasprite / byte arrays: prefer lines that look like sprite defs containing the literal.
  // Skip bare decimal for tiny indices (0–3) — too common in non-tile code.
  const allowDec = local > 3;
  for (let line = 0; line < lines.length; line++) {
    const raw = lines[line];
    if (raw.trimStart().startsWith("//")) continue;
    const lower = raw.toLowerCase();
    const hasHex = [...hexForms].some((h) => lower.includes(h.toLowerCase()));
    const hasDec =
      allowDec &&
      [...decForms].some((d) => new RegExp(`(^|[^0-9])${d}([^0-9]|$)`).test(raw));
    if (!hasHex && !hasDec) continue;

    const contextual =
      /\b(oam|sprite|meta|tile|chr|vram|nametable|bg_|spr_)/i.test(raw) ||
      /\[\s*0x|\{\s*0x/.test(raw);
    if (!contextual) continue;

    hits.push({
      uri,
      line,
      score: 50,
      reason: "tile index literal in graphics context",
      preview: raw.trim(),
    });
  }

  return dedupeHits(hits);
}

function findDisasmIndexHits(
  uri: vscode.Uri,
  text: string,
  tileIndex: number,
  lines: string[]
): TileReferenceHit[] {
  // Immediates only — bare .byte $NN nametable dumps are too noisy.
  const local = tileIndex & 0xff;
  const hex = hex2(local);
  // Skip ultra-common immediates that flood every ROM listing.
  if (local === 0x00 || local === 0xff) {
    return [];
  }

  const hits: TileReferenceHit[] = [];
  const immRe = new RegExp(
    `\\b(lda|ldx|ldy|cmp|cpx|cpy|adc|sbc|and|ora|eor)\\s+#\\$${hex}\\b`,
    "i"
  );

  for (let line = 0; line < lines.length; line++) {
    if (!immRe.test(lines[line])) continue;
    // Boost if nearby lines mention OAM / PPU data ports.
    let boost = 0;
    for (let d = 1; d <= 6; d++) {
      const around = `${lines[line - d] ?? ""} ${lines[line + d] ?? ""}`.toLowerCase();
      if (/\$2004|\$4014|oam|ppu/.test(around)) {
        boost = 15;
        break;
      }
    }
    hits.push({
      uri,
      line,
      score: 45 + boost,
      reason: boost ? "immediate near PPU/OAM" : `immediate #$${hex}`,
      preview: lines[line].trim(),
    });
  }
  return dedupeHits(hits);
}

function dedupeHits(hits: TileReferenceHit[]): TileReferenceHit[] {
  const best = new Map<string, TileReferenceHit>();
  for (const h of hits) {
    const key = `${h.uri.toString()}:${h.line}`;
    const prev = best.get(key);
    if (!prev || h.score > prev.score) {
      best.set(key, h);
    }
  }
  return [...best.values()].sort(
    (a, b) => b.score - a.score || a.line - b.line || a.uri.fsPath.localeCompare(b.uri.fsPath)
  );
}

async function resolveTargetFiles(chrUri: vscode.Uri): Promise<{
  program: vscode.Uri[];
  disasm: vscode.Uri[];
}> {
  const dir = path.dirname(chrUri.fsPath);
  const programLocal = vscode.Uri.file(path.join(dir, "Program.cs"));
  const disasmLocal = vscode.Uri.file(path.join(dir, "disasm.s"));

  const program: vscode.Uri[] = [];
  const disasm: vscode.Uri[] = [];

  try {
    await vscode.workspace.fs.stat(programLocal);
    program.push(programLocal);
  } catch {
    /* missing */
  }
  try {
    await vscode.workspace.fs.stat(disasmLocal);
    disasm.push(disasmLocal);
  } catch {
    /* missing */
  }

  // Fall back to workspace search if not beside the CHR file.
  if (program.length === 0) {
    program.push(...(await vscode.workspace.findFiles("**/Program.cs", "**/node_modules/**", 8)));
  }
  if (disasm.length === 0) {
    disasm.push(...(await vscode.workspace.findFiles("**/disasm.s", "**/node_modules/**", 8)));
  }

  return { program, disasm };
}

async function readText(uri: vscode.Uri): Promise<string> {
  const buf = await vscode.workspace.fs.readFile(uri);
  return Buffer.from(buf).toString("utf8");
}

/**
 * Find references to a CHR tile in Program.cs and disasm.s.
 *
 * Clever bits:
 * 1. Encode the selected tile to 16 CHR bytes and fingerprint-match that
 *    sequence in both files (hits CHR-RAM upload tables / SPRITE_TILES arrays
 *    and PRG-embedded tile data in disasm listings).
 * 2. In Program.cs, bind `const … = index` names and score oam_spr / comments /
 *    graphics-context literals — including `tileIndex & 0xFF` for tables at $1000.
 * 3. In disasm.s, only use immediates (optionally near PPU/OAM), never raw
 *    nametable `.byte` dumps.
 */
export async function findTileReferences(
  args: FindTileReferencesArgs
): Promise<TileReferenceHit[]> {
  const bytes = encodeTileBytes(args.pixels);
  const { program, disasm } = await resolveTargetFiles(args.chrUri);
  const hits: TileReferenceHit[] = [];

  for (const uri of program) {
    const text = await readText(uri);
    const lines = text.split(/\r?\n/);
    hits.push(...findFingerprintHits(uri, text, bytes, "csharp", lines));
    hits.push(...findProgramIndexHits(uri, text, args.tileIndex, lines));
  }

  for (const uri of disasm) {
    const text = await readText(uri);
    const lines = text.split(/\r?\n/);
    hits.push(...findFingerprintHits(uri, text, bytes, "asm", lines));
    hits.push(...findDisasmIndexHits(uri, text, args.tileIndex, lines));
  }

  return dedupeHits(hits);
}

export async function showTileReferences(
  args: FindTileReferencesArgs
): Promise<void> {
  const hits = await findTileReferences(args);
  if (hits.length === 0) {
    vscode.window.showInformationMessage(
      `No references found for tile $${hex2(args.tileIndex)} (${args.tileIndex}) in Program.cs / disasm.s.`
    );
    return;
  }

  const bestProgram = hits.find((h) => path.basename(h.uri.fsPath).toLowerCase() === "program.cs");
  const bestDisasm = hits.find((h) => path.basename(h.uri.fsPath).toLowerCase() === "disasm.s");

  type PickItem = vscode.QuickPickItem & { hit?: TileReferenceHit; openBoth?: boolean };
  const items: PickItem[] = [];

  if (bestProgram && bestDisasm) {
    items.push({
      label: "$(files) Open best in Program.cs and disasm.s",
      description: `scores ${bestProgram.score}/${bestDisasm.score}`,
      openBoth: true,
    });
  }

  for (const h of hits.slice(0, 60)) {
    items.push({
      label: `${path.basename(h.uri.fsPath)}:${h.line + 1}`,
      description: h.reason,
      detail: h.preview.slice(0, 120),
      hit: h,
    });
  }

  const picked = await vscode.window.showQuickPick(items, {
    placeHolder: `Tile $${hex2(args.tileIndex)} (${args.tileIndex}) — ${hits.length} reference(s)`,
    matchOnDescription: true,
    matchOnDetail: true,
  });
  if (!picked) return;

  if (picked.openBoth && bestProgram && bestDisasm) {
    await openHit(bestProgram, vscode.ViewColumn.One);
    await openHit(bestDisasm, vscode.ViewColumn.Beside);
    return;
  }

  if (picked.hit) {
    await openHit(picked.hit, vscode.ViewColumn.Active);
  }
}

async function openHit(hit: TileReferenceHit, column: vscode.ViewColumn): Promise<void> {
  const doc = await vscode.workspace.openTextDocument(hit.uri);
  const editor = await vscode.window.showTextDocument(doc, { viewColumn: column, preview: true });
  const pos = new vscode.Position(hit.line, 0);
  editor.selection = new vscode.Selection(pos, pos);
  editor.revealRange(new vscode.Range(pos, pos), vscode.TextEditorRevealType.InCenter);
}
