import {
  overviewPages,
  foundationsPages,
  gettingStartedPages,
  tourPages,
  advancedPages,
  comingFromOtherLanguagesPages,
  stdlibPages,
  cookbookPages,
} from "./getting-started";
import type { DocPage } from "./getting-started";

export interface NavSection {
  title: string;
  basePath: string;
  icon: string;
  pages: DocPage[];
}

export const navSections: NavSection[] = [
  {
    title: "Overview",
    basePath: "/docs/overview",
    icon: "View",
    pages: overviewPages,
  },
  {
    title: "Foundations",
    basePath: "/docs/foundations",
    icon: "Landmark",
    pages: foundationsPages,
  },
  {
    title: "Getting Started",
    basePath: "/docs/getting-started",
    icon: "Rocket",
    pages: gettingStartedPages,
  },
  {
    title: "Language Tour",
    basePath: "/docs/tour",
    icon: "Map",
    pages: tourPages,
  },
  {
    title: "Advanced",
    basePath: "/docs/advanced",
    icon: "Sparkles",
    pages: advancedPages,
  },
  {
    title: "Coming from other languages",
    basePath: "/docs/coming-from-other-languages",
    icon: "Languages",
    pages: comingFromOtherLanguagesPages,
  },
  {
    title: "Standard Library",
    basePath: "/docs/stdlib",
    icon: "Library",
    pages: stdlibPages,
  },
  {
    title: "Cookbook",
    basePath: "/docs/cookbook",
    icon: "BookOpen",
    pages: cookbookPages,
  },
];

export function getAllPages(): { path: string; page: DocPage; section: NavSection }[] {
  return navSections.flatMap((section) =>
    section.pages.map((page) => ({
      path: `${section.basePath}/${page.slug}`,
      page,
      section,
    }))
  );
}

export function findPage(path: string) {
  const allPages = getAllPages();
  return allPages.find((p) => p.path === path);
}

export function getAdjacentPages(path: string) {
  const allPages = getAllPages();
  const index = allPages.findIndex((p) => p.path === path);
  return {
    prev: index > 0 ? allPages[index - 1] : null,
    next: index < allPages.length - 1 ? allPages[index + 1] : null,
  };
}
