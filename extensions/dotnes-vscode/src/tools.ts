import * as fs from "fs";
import * as path from "path";
import { spawn } from "child_process";
import * as vscode from "vscode";

export function resolveToolPath(defaultName: string, settingKey: string): string | undefined {
  const configured = vscode.workspace.getConfiguration("dotnes").get<string>(settingKey)?.trim();
  if (configured) {
    return configured;
  }

  const exe = process.platform === "win32" ? `${defaultName}.exe` : defaultName;
  const pathEnv = process.env.PATH ?? "";
  const parts = pathEnv.split(path.delimiter);
  for (const dir of parts) {
    const candidate = path.join(dir, exe);
    if (fs.existsSync(candidate)) {
      return candidate;
    }
  }

  // Also try without .exe on Windows when looking up scripts
  if (process.platform === "win32") {
    for (const dir of parts) {
      const candidate = path.join(dir, defaultName);
      if (fs.existsSync(candidate)) {
        return candidate;
      }
    }
  }

  return undefined;
}

export function runTool(
  toolPath: string,
  args: string[],
  cwd?: string
): Promise<{ code: number; stdout: string; stderr: string }> {
  return new Promise((resolve, reject) => {
    const child = spawn(toolPath, args, {
      cwd,
      env: process.env,
      shell: false,
    });
    let stdout = "";
    let stderr = "";
    child.stdout.on("data", (d) => (stdout += d.toString()));
    child.stderr.on("data", (d) => (stderr += d.toString()));
    child.on("error", reject);
    child.on("close", (code) => resolve({ code: code ?? 1, stdout, stderr }));
  });
}

export function getOutputChannel(): vscode.OutputChannel {
  return vscode.window.createOutputChannel("dotNES");
}
