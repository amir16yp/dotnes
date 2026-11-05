import * as vscode from "vscode";
import {
  LanguageClient,
  LanguageClientOptions,
  ServerOptions,
  TransportKind,
} from "vscode-languageclient/node";
import { resolveToolPath } from "./tools";

let client: LanguageClient | undefined;

export function getClient(): LanguageClient | undefined {
  return client;
}

export async function startLanguageClient(context: vscode.ExtensionContext): Promise<void> {
  const serverPath = resolveToolPath("dotnes-lsp", "dotnes.lsp.path");
  if (!serverPath) {
    throw new Error("dotnes-lsp not found on PATH");
  }

  const serverOptions: ServerOptions = {
    run: { command: serverPath, transport: TransportKind.stdio },
    debug: { command: serverPath, transport: TransportKind.stdio },
  };

  const clientOptions: LanguageClientOptions = {
    documentSelector: [{ scheme: "file", pattern: "**/*.s" }],
    synchronize: {
      fileEvents: vscode.workspace.createFileSystemWatcher("**/*.s"),
    },
  };

  client = new LanguageClient("dotnes", "dotNES Language Server", serverOptions, clientOptions);
  context.subscriptions.push(client);
  await client.start();
}

export async function stopLanguageClient(): Promise<void> {
  if (client) {
    await client.stop();
    client = undefined;
  }
}

export interface GetTilesResult {
  tileCount: number;
  bytesPerTile: number;
  tiles: number[][];
  error?: string;
}

export interface SetTilesResult {
  ok: boolean;
  text?: string;
  error?: string;
}

export async function getTiles(uri: vscode.Uri): Promise<GetTilesResult> {
  if (!client) {
    throw new Error("Language server is not running.");
  }
  return client.sendRequest("dotnes/chr/getTiles", { uri: uri.toString() });
}

export async function setTiles(uri: vscode.Uri, tiles: number[][]): Promise<SetTilesResult> {
  if (!client) {
    throw new Error("Language server is not running.");
  }
  return client.sendRequest("dotnes/chr/setTiles", { uri: uri.toString(), tiles });
}
