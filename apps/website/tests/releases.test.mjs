import { test } from "node:test";
import assert from "node:assert/strict";
import { validateManifest, releaseRows } from "../scripts/releases.mjs";
const release = {
  id: "2026.10.11-pilot",
  version: "2026.10.11 pilot",
  date: "2026-10-11",
  channel: "development",
  platform: "Windows 11 x64",
  signed: false,
  url: "/downloads/2026.10.11-pilot/DesktopGuide-windows-x64-pilot.zip",
  sha256: "a".repeat(64),
  bytes: 1024,
  notes: "A development preview.",
};
const manifest = () => ({
  schemaVersion: 1,
  latest: release.id,
  releases: [{ ...release }],
});
test("release links remain available in server-rendered HTML", () => {
  const m = validateManifest(manifest());
  assert.match(releaseRows(m), /href="\/downloads\/.+\.zip" download/);
  assert.match(releaseRows(m), /\.zip\.sha256/);
});
test("rejects traversal, external URLs and invalid hashes before packaging", () => {
  for (const url of [
    "https://evil.example/app.zip",
    "/downloads/../DesktopGuide-x.zip",
    "javascript:alert(1)",
  ]) {
    const m = manifest();
    m.releases[0].url = url;
    assert.throws(() => validateManifest(m));
  }
  const m = manifest();
  m.releases[0].sha256 = "wrong";
  assert.throws(() => validateManifest(m));
});
test("cannot label an unsigned development artifact as stable", () => {
  const m = manifest();
  m.releases[0].channel = "stable";
  assert.throws(() => validateManifest(m));
});
test("rejects missing latest, duplicate versions and unknown schema", () => {
  const m = manifest();
  m.latest = "missing";
  assert.throws(() => validateManifest(m));
  const n = manifest();
  n.releases.push({ ...release });
  assert.throws(() => validateManifest(n));
  const o = manifest();
  o.schemaVersion = 2;
  assert.throws(() => validateManifest(o));
});
test("escapes release notes instead of executing their markup", () => {
  const m = manifest();
  m.releases[0].notes = '<script>alert("x")</script>';
  const html = releaseRows(validateManifest(m));
  assert.ok(!html.includes("<script>"));
  assert.match(html, /&lt;script&gt;/);
});
