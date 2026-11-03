import { getCollection, type CollectionEntry } from "astro:content";
import { DOCS_BASE, sections } from "../../site/sections.mjs";

export type DocEntry = CollectionEntry<"docs">;

export interface NavPage {
  path: string;
  title: string;
  description?: string;
  entry: DocEntry;
}

export interface NavSection {
  title: string;
  dir: string;
  basePath: string;
  icon: string;
  pages: NavPage[];
}

let cached: Promise<NavSection[]> | null = null;

export function getNav(): Promise<NavSection[]> {
  cached ??= buildNav();
  return cached;
}

async function buildNav(): Promise<NavSection[]> {
  const entries = await getCollection("docs");

  return sections.map((section) => {
    const pages = entries
      .filter((entry) => entry.id.split("/")[0] === section.dir)
      .sort((a, b) => a.data.order - b.data.order)
      .map((entry) => ({
        path: `${DOCS_BASE}/${entry.id}`,
        title: entry.data.title,
        description: entry.data.description,
        entry,
      }));

    return {
      title: section.title,
      dir: section.dir,
      basePath: `${DOCS_BASE}/${section.dir}`,
      icon: section.icon,
      pages,
    };
  });
}

export async function getAllPages(): Promise<{ page: NavPage; section: NavSection }[]> {
  const nav = await getNav();
  return nav.flatMap((section) => section.pages.map((page) => ({ page, section })));
}

export async function getAdjacentPages(path: string) {
  const all = await getAllPages();
  const index = all.findIndex((p) => p.page.path === path);
  return {
    prev: index > 0 ? all[index - 1] : null,
    next: index >= 0 && index < all.length - 1 ? all[index + 1] : null,
  };
}
