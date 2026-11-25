import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_BLOB = "https://github.com/OGShawnLee/pino-lang/blob/main/";

export function rewriteGuideUrl(url) {
  if (!url || url.startsWith("#") || /^(https?:|mailto:)/i.test(url)) {
    return url;
  }

  const hashIdx = url.indexOf("#");
  const target = hashIdx === -1 ? url : url.slice(0, hashIdx);
  const frag = hashIdx === -1 ? "" : url.slice(hashIdx);

  if (/^[^/]+\.md$/i.test(target)) {
    if (target.toLowerCase() === "readme.md") {
      return `/learn/${frag}`;
    }
    const slug = target.slice(0, -3);
    return `/learn/${slug}/${frag}`;
  }

  if (target.startsWith("../")) {
    const resolved = path.posix.normalize(`docs/guide/${target}`);
    if (!resolved.startsWith("..")) {
      return `${REPO_BLOB}${resolved}${frag}`;
    }
  }

  return url;
}

function isGuideFile(fileURL) {
  if (!fileURL) return false;
  const fsPath = fileURL instanceof URL ? fileURLToPath(fileURL) : String(fileURL);
  return fsPath.replace(/\\/g, "/").includes("/docs/guide/");
}

/** Sätteri mdast plugin (Astro 7's default Markdown processor) that rewrites links in docs/guide/. */
export function remarkGuideLinks() {
  const visitLink = (node, ctx) => {
    if (!isGuideFile(ctx.fileURL) || typeof node.url !== "string") return;
    const next = rewriteGuideUrl(node.url);
    if (next !== node.url) {
      ctx.setProperty(node, "url", next);
    }
  };

  return {
    name: "pino-guide-links",
    link: visitLink,
    definition: visitLink,
  };
}
