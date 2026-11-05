import * as vscode from "vscode";
import { startLanguageClient, stopLanguageClient, getClient } from "./lspClient";
import { registerCommands } from "./commands";
import { ChrEditorProvider } from "./chrEditor";

export async function activate(context: vscode.ExtensionContext): Promise<void> {
  try {
    await startLanguageClient(context);
  } catch (err) {
    vscode.window.showWarningMessage(
      `dotNES: language server did not start (${err instanceof Error ? err.message : String(err)}). ` +
        "Install the release zip on PATH or set dotnes.lsp.path."
    );
  }

  context.subscriptions.push(
    vscode.window.registerCustomEditorProvider(
      ChrEditorProvider.viewType,
      new ChrEditorProvider(context),
      {
        webviewOptions: { retainContextWhenHidden: true },
        supportsMultipleEditorsPerDocument: false,
      }
    )
  );

  registerCommands(context);
}

export async function deactivate(): Promise<void> {
  await stopLanguageClient();
}

export { getClient };
