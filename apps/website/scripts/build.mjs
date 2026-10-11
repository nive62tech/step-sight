import {
  mkdir,
  readFile,
  writeFile,
  copyFile,
  readdir,
  stat,
} from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { resolve, join, basename } from "node:path";
import { createHash } from "node:crypto";
import {
  validateManifest,
  escapeHtml,
  formatSize,
  releaseRows,
} from "./releases.mjs";

const root = fileURLToPath(new URL("../", import.meta.url));
const dist = join(root, "dist");
const manifest = validateManifest(
  JSON.parse(await readFile(join(root, "releases.json"), "utf8")),
);
const latest = manifest.releases.find(
  (release) => release.id === manifest.latest,
);
await mkdir(join(dist, "assets"), { recursive: true });
for (const [source, destination] of [
  ["styles.css", "assets/styles.css"],
  ["main.js", "assets/main.js"],
  ["mark.svg", "assets/mark.svg"],
  ["robots.txt", "robots.txt"],
  ["404.html", "404.html"],
]) {
  await copyFile(join(root, "src", source), join(dist, destination));
}
for (const file of await readdir(join(root, "assets"))) {
  if (/\.(ttf|woff2|txt)$/.test(file))
    await copyFile(join(root, "assets", file), join(dist, "assets", file));
}
let html = await readFile(join(root, "src/index.html"), "utf8");
for (const [key, value] of Object.entries({
  LATEST_URL: latest.url,
  LATEST_VERSION: escapeHtml(latest.version),
  LATEST_SIZE: formatSize(latest.bytes),
  RELEASE_ROWS: releaseRows(manifest),
  RELEASE_COUNT_LABEL: `${manifest.releases.length} available release${manifest.releases.length === 1 ? "" : "s"}`,
})) {
  html = html.replaceAll(`{{${key}}}`, String(value));
}
if (/\{\{[A-Z_]+\}\}/.test(html)) throw new Error("Unresolved template value.");
// Verify copied binaries against the reviewed manifest; never upload a package under a stale hash.
const packageDir = process.env.WEBSITE_PACKAGE_DIR
  ? resolve(process.env.WEBSITE_PACKAGE_DIR)
  : resolve(root, "../../artifacts/website-packages");
for (const release of manifest.releases) {
  const source = join(packageDir, release.id, basename(release.url));
  const target = join(dist, release.url.slice(1));
  const info = await stat(source);
  if (info.size !== release.bytes)
    throw new Error(`Size mismatch for ${release.id}.`);
  const hash = createHash("sha256")
    .update(await readFile(source))
    .digest("hex");
  if (hash !== release.sha256)
    throw new Error(`Checksum mismatch for ${release.id}.`);
  await mkdir(resolve(target, ".."), { recursive: true });
  await copyFile(source, target);
  await writeFile(`${target}.sha256`, `${hash}  ${basename(target)}\n`);
}
await writeFile(join(dist, "index.html"), html);
await writeFile(
  join(dist, "releases.json"),
  `${JSON.stringify(manifest, null, 2)}\n`,
);
console.log(
  `Built Desktop Guide website with ${manifest.releases.length} verified downloadable release(s).`,
);
