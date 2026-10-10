export function validateManifest(manifest) {
  if (
    manifest.schemaVersion !== 1 ||
    !Array.isArray(manifest.releases) ||
    !manifest.releases.length
  )
    throw new Error("A nonempty schemaVersion 1 release manifest is required.");
  const ids = new Set();
  const paths = new Set();
  for (const release of manifest.releases) {
    if (!/^[a-z0-9][a-z0-9.-]+$/.test(release.id) || ids.has(release.id))
      throw new Error("Release IDs must be unique safe identifiers.");
    ids.add(release.id);
    if (!["development", "stable"].includes(release.channel))
      throw new Error("Unknown release channel.");
    if (
      !/^\d{4}-\d{2}-\d{2}$/.test(release.date) ||
      Number.isNaN(Date.parse(release.date))
    )
      throw new Error("Invalid release date.");
    for (const field of ["version", "notes", "platform"])
      if (typeof release[field] !== "string" || !release[field].trim())
        throw new Error(`Missing ${field}.`);
    if (release.platform !== "Windows 11 x64")
      throw new Error("Unsupported package platform.");
    if (
      typeof release.signed !== "boolean" ||
      (release.channel === "stable" && !release.signed)
    )
      throw new Error("Stable packages must be signed.");
    if (
      !/^\/downloads\/[a-z0-9.-]+\/DesktopGuide-[a-zA-Z0-9.-]+\.zip$/.test(
        release.url,
      ) ||
      paths.has(release.url) ||
      release.url.includes("..")
    )
      throw new Error("Unsafe or duplicate download path.");
    paths.add(release.url);
    if (
      !/^[a-f0-9]{64}$/.test(release.sha256) ||
      !Number.isSafeInteger(release.bytes) ||
      release.bytes <= 0
    )
      throw new Error("Invalid checksum or package size.");
  }
  if (!ids.has(manifest.latest))
    throw new Error("Latest must reference an available release.");
  return manifest;
}
export function escapeHtml(value) {
  return String(value).replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
}
export function formatSize(bytes) {
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}
export function releaseRows(manifest) {
  return [...manifest.releases]
    .sort((a, b) => b.date.localeCompare(a.date) || b.id.localeCompare(a.id))
    .map((r) => {
      const date = new Date(`${r.date}T12:00:00Z`).toLocaleDateString("en-GB", {
        day: "numeric",
        month: "short",
        year: "numeric",
        timeZone: "UTC",
      });
      return `<article class="release-entry" data-channel="${r.channel}"><div class="release-row"><span class="release-version">${escapeHtml(r.version)}</span><time datetime="${r.date}">${date}</time><span class="release-channel">${r.channel === "stable" ? "Stable" : "Development"}</span><div class="release-links"><a href="${r.url}" download aria-label="Download Desktop Guide ${escapeHtml(r.version)} for Windows x64">Windows x64 ↗</a></div></div><details class="release-notes" ${r.id === manifest.latest ? "open" : ""}><summary>Release notes</summary><p>${escapeHtml(r.notes)}</p><a class="checksum-link" href="${r.url}.sha256" download>SHA-256 checksum <span aria-hidden="true">↓</span></a><code>${r.sha256}</code><p>${formatSize(r.bytes)} · ${r.signed ? "Signed" : "Unsigned development build"} · ${escapeHtml(r.platform)}</p></details></article>`;
    })
    .join("\n");
}
