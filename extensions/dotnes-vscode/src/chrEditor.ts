import * as vscode from "vscode";
import { getTiles, setTiles } from "./lspClient";
import { showTileReferences } from "./tileReferences";

class ChrDocument implements vscode.CustomDocument {
  public tiles: number[][];
  public dirty = false;

  constructor(public readonly uri: vscode.Uri, tiles: number[][]) {
    this.tiles = tiles;
  }

  dispose(): void {
    /* no-op */
  }
}

export class ChrEditorProvider implements vscode.CustomEditorProvider<ChrDocument> {
  public static readonly viewType = "dotnes.chrEditor";

  private readonly _onDidChangeCustomDocument = new vscode.EventEmitter<
    vscode.CustomDocumentEditEvent<ChrDocument>
  >();
  public readonly onDidChangeCustomDocument = this._onDidChangeCustomDocument.event;

  constructor(private readonly context: vscode.ExtensionContext) {}

  async openCustomDocument(
    uri: vscode.Uri,
    _openContext: vscode.CustomDocumentOpenContext,
    _token: vscode.CancellationToken
  ): Promise<ChrDocument> {
    const text = Buffer.from(await vscode.workspace.fs.readFile(uri)).toString("utf8");
    if (!text.includes('.segment "CHARS"') && !text.includes(".segment 'CHARS'")) {
      throw new Error("Not a CHARS segment file. Open as text instead.");
    }

    let tiles: number[][] = [];
    try {
      const result = await getTiles(uri);
      if (result.error) {
        throw new Error(result.error);
      }
      tiles = result.tiles.map((t) => Array.from(t));
    } catch (err) {
      // Fallback: still open empty editor with message via webview
      vscode.window.showWarningMessage(
        `CHR parse via LSP failed (${err instanceof Error ? err.message : String(err)}).`
      );
    }

    return new ChrDocument(uri, tiles);
  }

  async resolveCustomEditor(
    document: ChrDocument,
    webviewPanel: vscode.WebviewPanel,
    _token: vscode.CancellationToken
  ): Promise<void> {
    webviewPanel.webview.options = {
      enableScripts: true,
    };
    webviewPanel.webview.html = this.getHtml(webviewPanel.webview);

    const update = () => {
      webviewPanel.webview.postMessage({
        type: "load",
        tiles: document.tiles,
        uri: document.uri.toString(),
      });
    };

    webviewPanel.webview.onDidReceiveMessage(async (msg) => {
      switch (msg.type) {
        case "ready":
          update();
          break;
        case "paint": {
          const { tileIndex, pixelIndex, color } = msg as {
            tileIndex: number;
            pixelIndex: number;
            color: number;
          };
          if (!document.tiles[tileIndex]) {
            return;
          }
          document.tiles[tileIndex][pixelIndex] = color & 3;
          document.dirty = true;
          this._onDidChangeCustomDocument.fire({
            document,
            label: "Paint pixel",
            undo: () => undefined,
            redo: () => undefined,
          });
          break;
        }
        case "save": {
          await this.saveCustomDocument(document, new vscode.CancellationTokenSource().token);
          break;
        }
        case "findReferences": {
          const { tileIndex } = msg as { tileIndex: number };
          const pixels = document.tiles[tileIndex];
          if (!pixels) {
            vscode.window.showWarningMessage(`Tile ${tileIndex} is out of range.`);
            break;
          }
          await showTileReferences({
            tileIndex,
            pixels: Array.from(pixels),
            chrUri: document.uri,
          });
          break;
        }
      }
    });
  }

  async saveCustomDocument(
    document: ChrDocument,
    _cancellation: vscode.CancellationToken
  ): Promise<void> {
    await this.writeTiles(document, document.uri);
  }

  async saveCustomDocumentAs(
    document: ChrDocument,
    destination: vscode.Uri,
    _cancellation: vscode.CancellationToken
  ): Promise<void> {
    await this.writeTiles(document, destination);
  }

  private async writeTiles(document: ChrDocument, destination: vscode.Uri): Promise<void> {
    const result = await setTiles(document.uri, document.tiles);
    if (!result.ok) {
      throw new Error(result.error ?? "Failed to save CHR tiles.");
    }
    if (result.text) {
      await vscode.workspace.fs.writeFile(destination, Buffer.from(result.text, "utf8"));
    }
    document.dirty = false;
  }

  async revertCustomDocument(
    document: ChrDocument,
    _cancellation: vscode.CancellationToken
  ): Promise<void> {
    const result = await getTiles(document.uri);
    if (result.error) {
      throw new Error(result.error);
    }
    document.tiles = result.tiles.map((t) => Array.from(t));
    document.dirty = false;
  }

  async backupCustomDocument(
    document: ChrDocument,
    context: vscode.CustomDocumentBackupContext,
    _cancellation: vscode.CancellationToken
  ): Promise<vscode.CustomDocumentBackup> {
    const payload = JSON.stringify({ tiles: document.tiles });
    await vscode.workspace.fs.writeFile(context.destination, Buffer.from(payload, "utf8"));
    return {
      id: context.destination.toString(),
      delete: async () => {
        try {
          await vscode.workspace.fs.delete(context.destination);
        } catch {
          /* ignore */
        }
      },
    };
  }

  private getHtml(webview: vscode.Webview): string {
    const scriptUri = webview.asWebviewUri(
      vscode.Uri.joinPath(this.context.extensionUri, "media", "chrEditor.js")
    );
    const styleUri = webview.asWebviewUri(
      vscode.Uri.joinPath(this.context.extensionUri, "media", "chrEditor.css")
    );
    const nonce = getNonce();

    return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta http-equiv="Content-Security-Policy"
        content="default-src 'none'; style-src ${webview.cspSource}; script-src 'nonce-${nonce}';" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <link rel="stylesheet" href="${styleUri}" />
  <title>CHR Tile Editor</title>
</head>
<body>
  <header>
    <div class="toolbar">
      <label>Color
        <select id="color">
          <option value="0">0</option>
          <option value="1" selected>1</option>
          <option value="2">2</option>
          <option value="3">3</option>
        </select>
      </label>
      <label>Zoom
        <input id="zoom" type="range" min="2" max="16" value="4" />
      </label>
      <span id="status">Tile —</span>
      <button id="save" type="button">Save</button>
    </div>
    <div id="palette" class="palette"></div>
  </header>
  <main>
    <canvas id="sheet"></canvas>
    <canvas id="detail" width="256" height="256"></canvas>
  </main>
  <div id="ctx" class="ctx hidden" role="menu">
    <button type="button" data-cmd="findReferences" role="menuitem">Find references…</button>
  </div>
  <script nonce="${nonce}" src="${scriptUri}"></script>
</body>
</html>`;
  }
}

function getNonce(): string {
  const chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
  let text = "";
  for (let i = 0; i < 32; i++) {
    text += chars.charAt(Math.floor(Math.random() * chars.length));
  }
  return text;
}
