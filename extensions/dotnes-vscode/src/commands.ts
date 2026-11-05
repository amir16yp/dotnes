import * as path from "path";
import * as vscode from "vscode";
import { resolveToolPath, runTool } from "./tools";

let output: vscode.OutputChannel | undefined;

function channel(): vscode.OutputChannel {
  if (!output) {
    output = vscode.window.createOutputChannel("dotNES");
  }
  return output;
}

export function registerCommands(context: vscode.ExtensionContext): void {
  context.subscriptions.push(
    vscode.commands.registerCommand("dotnes.importNes", () => importNes()),
    vscode.commands.registerCommand("dotnes.newProject", () => newProject()),
    vscode.commands.registerCommand("dotnes.build", () => runDotnes(["build"])),
    vscode.commands.registerCommand("dotnes.run", () => runDotnes(["run"])),
    vscode.commands.registerCommand("dotnes.openChrAsText", async (uri?: vscode.Uri) => {
      const target = uri ?? vscode.window.activeTextEditor?.document.uri;
      if (!target) {
        return;
      }
      await vscode.commands.executeCommand("vscode.openWith", target, "default");
    })
  );
}

async function importNes(uri?: vscode.Uri): Promise<void> {
  const decompiler =
    resolveToolPath("dotnes-decompiler", "dotnes.decompiler.path") ??
    resolveToolPath("dotnes", "dotnes.cli.path");

  if (!decompiler) {
    vscode.window.showErrorMessage(
      "dotnes-decompiler (or dotnes) not found on PATH. Extract the release zip and add it to PATH."
    );
    return;
  }

  let rom = uri;
  if (!rom || rom.fsPath.toLowerCase().endsWith(".nes") === false) {
    const picked = await vscode.window.showOpenDialog({
      canSelectMany: false,
      filters: { "NES ROMs": ["nes"] },
      openLabel: "Import",
    });
    if (!picked || picked.length === 0) {
      return;
    }
    rom = picked[0];
  }

  const mode = await vscode.window.showQuickPick(
    [
      { label: "auto", description: "Detect dotnes layout vs RawPrg (default)" },
      { label: "transpile", description: "Force NESLib C# recovery" },
      { label: "raw", description: "Force RawPrg byte-identical rebuild" },
    ],
    { placeHolder: "Decompiler pack mode" }
  );
  if (!mode) {
    return;
  }

  const defaultOut = path.join(
    path.dirname(rom.fsPath),
    path.basename(rom.fsPath, path.extname(rom.fsPath))
  );
  const outDir = await vscode.window.showInputBox({
    prompt: "Output directory for the decompiled project",
    value: defaultOut,
  });
  if (!outDir) {
    return;
  }

  const toolName = path.basename(decompiler).replace(/\.exe$/i, "");
  const args =
    toolName === "dotnes"
      ? ["decompile", rom.fsPath, outDir, "--mode", mode.label]
      : [rom.fsPath, outDir, "--mode", mode.label];

  channel().show(true);
  channel().appendLine(`$ ${decompiler} ${args.join(" ")}`);

  await vscode.window.withProgress(
    {
      location: vscode.ProgressLocation.Notification,
      title: "Importing NES ROM…",
      cancellable: false,
    },
    async () => {
      const result = await runTool(decompiler, args);
      channel().appendLine(result.stdout);
      channel().appendLine(result.stderr);
      if (result.code !== 0) {
        vscode.window.showErrorMessage(`Decompile failed (exit ${result.code}). See dotNES output.`);
        return;
      }
      const open = await vscode.window.showInformationMessage(
        `Imported to ${outDir}`,
        "Open Folder"
      );
      if (open === "Open Folder") {
        await vscode.commands.executeCommand("vscode.openFolder", vscode.Uri.file(outDir), true);
      }
    }
  );
}

async function newProject(): Promise<void> {
  const cli = resolveToolPath("dotnes", "dotnes.cli.path");
  if (!cli) {
    vscode.window.showErrorMessage("dotnes CLI not found on PATH.");
    return;
  }

  const name = await vscode.window.showInputBox({
    prompt: "Project name",
    value: "mygame",
    validateInput: (v) => (/^[A-Za-z_][\w-]*$/.test(v) ? undefined : "Invalid project name"),
  });
  if (!name) {
    return;
  }

  const folders = vscode.workspace.workspaceFolders;
  const parent =
    folders && folders.length > 0
      ? folders[0].uri.fsPath
      : (
          await vscode.window.showOpenDialog({
            canSelectFiles: false,
            canSelectFolders: true,
            openLabel: "Create project here",
          })
        )?.[0]?.fsPath;

  if (!parent) {
    return;
  }

  const result = await runTool(cli, ["new", name], parent);
  channel().appendLine(result.stdout + result.stderr);
  if (result.code !== 0) {
    vscode.window.showErrorMessage("dotnes new failed. See dotNES output.");
    return;
  }

  const projectDir = path.join(parent, name);
  await vscode.commands.executeCommand("vscode.openFolder", vscode.Uri.file(projectDir), true);
}

async function runDotnes(args: string[]): Promise<void> {
  const cli = resolveToolPath("dotnes", "dotnes.cli.path");
  if (!cli) {
    vscode.window.showErrorMessage("dotnes CLI not found on PATH.");
    return;
  }

  const cwd = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
  channel().show(true);
  channel().appendLine(`$ ${cli} ${args.join(" ")}`);
  const result = await runTool(cli, args, cwd);
  channel().appendLine(result.stdout);
  channel().appendLine(result.stderr);
  if (result.code !== 0) {
    vscode.window.showErrorMessage(`dotnes ${args[0]} failed (exit ${result.code}).`);
  }
}
