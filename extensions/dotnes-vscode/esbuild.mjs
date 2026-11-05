import * as esbuild from "esbuild";

await esbuild.build({
  entryPoints: ["out/extension.js"],
  bundle: true,
  outfile: "out/extension.js",
  allowOverwrite: true,
  external: ["vscode"],
  platform: "node",
  format: "cjs",
  sourcemap: true,
  minify: false,
});

console.log("Bundled out/extension.js");
