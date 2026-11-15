import { readFile, writeFile, mkdir } from "node:fs/promises";
import { stripTypeScriptTypes } from "node:module";

const root = new URL("./", import.meta.url);
const target = new URL("skills/senor-arroz-analytics/references/stay-lunch-program.js", root);
let bundle = "";
for (const source of ["helpers/stay-lunch.ts", "helpers/stay-lunch-summary.ts"]) {
  const typed = (await readFile(new URL(source, root), "utf8")).replace(/\r\n/g, "\n");
  bundle += stripTypeScriptTypes(typed, { mode: "strip" }).replace(/^import .*;\r?\n/gm, "").replace(/^export /gm, "") + "\n";
}
await mkdir(new URL("./", target), { recursive: true });
if (process.argv.includes("--check")) {
  if ((await readFile(target, "utf8")).replace(/\r\n/g, "\n") !== bundle) throw new Error("Code Mode bundle is stale");
} else await writeFile(target, bundle);
