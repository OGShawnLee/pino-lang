import path from "node:path";
import { getCollection, type CollectionEntry } from "astro:content";

export interface GuidePage {
  entry: CollectionEntry<"guide">;
  slug: string;
  title: string;
  description: string;
}

function stripMarkdown(text: string): string {
  return text
    .replace(/\[([^\]]+)\]\([^)]+\)/g, "$1")
    .replace(/`([^`]+)`/g, "$1")
    .replace(/(\*\*|__)(.*?)\1/g, "$2")
    .replace(/(\*|_)(.*?)\1/g, "$2")
    .replace(/\s+/g, " ")
    .trim();
}

function extractTitle(body: string, fallback: string): string {
  const match = body.match(/^#\s+(.+)$/m);
  return match ? stripMarkdown(match[1]) : fallback;
}

function extractFirstParagraph(body: string, truncate: boolean): string {
  const normalized = body.replace(/\r\n/g, "\n");
  const afterHeading = normalized.replace(/^#\s+.+\n+/, "");
  const blocks = afterHeading.split(/\n{2,}/);
  const first = blocks.find((block) => {
    const trimmed = block.trim();
    return trimmed.length > 0 && !trimmed.startsWith("#") && !trimmed.startsWith("<!--");
  });
  if (!first) return "";
  const plain = stripMarkdown(first);
  if (truncate && plain.length > 200) {
    const sentence = plain.match(/^.+?[.!?](?=\s|$)/);
    if (sentence) return sentence[0];
  }
  return plain;
}

function entrySlug(entry: CollectionEntry<"guide">): string {
  if (entry.filePath) {
    return path.basename(entry.filePath, ".md");
  }
  return entry.id;
}

function readmeOrder(body: string): string[] {
  const order: string[] = [];
  const seen = new Set<string>();
  const re = /\[[^\]]+\]\(([^)#/]+)\.md(?:#[^)]*)?\)/gi;
  let match: RegExpExecArray | null;
  while ((match = re.exec(body)) !== null) {
    const slug = match[1];
    if (slug.toLowerCase() !== "readme" && !seen.has(slug)) {
      seen.add(slug);
      order.push(slug);
    }
  }
  return order;
}

export async function guidePages(): Promise<GuidePage[]> {
  const entries = await getCollection("guide");
  const readme = entries.find((e) => entrySlug(e).toLowerCase() === "readme");
  const order = readme ? readmeOrder(readme.body ?? "") : [];
  const orderIndex = new Map(order.map((slug, idx) => [slug, idx]));

  const pages: GuidePage[] = entries
    .filter((e) => entrySlug(e).toLowerCase() !== "readme")
    .map((entry) => {
      const slug = entrySlug(entry);
      const body = entry.body ?? "";
      return {
        entry,
        slug,
        title: extractTitle(body, slug),
        description: extractFirstParagraph(body, true),
      };
    });

  pages.sort((a, b) => {
    const ai = orderIndex.get(a.slug);
    const bi = orderIndex.get(b.slug);
    if (ai !== undefined && bi !== undefined) return ai - bi;
    if (ai !== undefined) return -1;
    if (bi !== undefined) return 1;
    return a.slug.localeCompare(b.slug);
  });

  return pages;
}

export async function guideIntro(): Promise<string> {
  const entries = await getCollection("guide");
  const readme = entries.find((e) => entrySlug(e).toLowerCase() === "readme");
  if (!readme) return "";
  return extractFirstParagraph(readme.body ?? "", false);
}
