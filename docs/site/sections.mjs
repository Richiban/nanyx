// Section list for the docs. Plain JS so that both astro.config.mjs and
// the site code can import it.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

export const DOCS_BASE = "/docs";

/** @type {{ title: string; dir: string; icon: string }[]} */
export const sections = [
  { title: "Overview", dir: "overview", icon: "View" },
  { title: "Getting Started", dir: "getting-started", icon: "Rocket" },
  { title: "Language Tour", dir: "tour", icon: "Map" },
  { title: "Advanced", dir: "advanced", icon: "Sparkles" },
  { title: "Coming from other languages", dir: "coming-from-other-languages", icon: "Languages" },
  { title: "Standard Library", dir: "stdlib", icon: "Library" },
  { title: "Standard tools", dir: "standard-tools", icon: "Wrench" },
  { title: "Meta", dir: "foundations", icon: "Fingerprint" },
  { title: "Cookbook", dir: "cookbook", icon: "BookOpen" },
  { title: "Work in progress", dir: "work-in-progress", icon: "Construction" },
];

const contentRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../src/content");

/** Slug of the lowest-ordered page in a section directory, read from front matter. */
export function firstPageSlug(dir) {
  const files = fs
    .readdirSync(path.join(contentRoot, dir))
    .filter((f) => f.endsWith(".md"));

  let best = null;
  for (const file of files) {
    const raw = fs.readFileSync(path.join(contentRoot, dir, file), "utf8");
    const match = raw.match(/^---\r?\n([\s\S]*?)\r?\n---/);
    const orderLine = match?.[1].match(/^order:\s*(\d+)/m);
    const order = orderLine ? Number(orderLine[1]) : 99;
    if (!best || order < best.order) {
      best = { order, slug: file.replace(/\.md$/, "") };
    }
  }
  return best?.slug ?? null;
}

/** Redirects from /docs and each section root to that section's first page. */
export function sectionRedirects() {
  const redirects = {};
  for (const section of sections) {
    const slug = firstPageSlug(section.dir);
    if (slug) {
      redirects[`${DOCS_BASE}/${section.dir}`] = `${DOCS_BASE}/${section.dir}/${slug}`;
    }
  }
  const first = sections[0];
  redirects[DOCS_BASE] = redirects[`${DOCS_BASE}/${first.dir}`];
  return redirects;
}
