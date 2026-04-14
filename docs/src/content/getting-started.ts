export interface DocPage {
  slug: string;
  title: string;
  description?: string;
  content: string;
  order: number;
}

interface FrontMatter {
  title: string;
  description?: string;
  order?: number;
}

function parseFrontMatter(raw: string): { data: FrontMatter; content: string } {
  const match = raw.match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n([\s\S]*)$/);
  if (!match) {
    return { data: { title: "Untitled" }, content: raw };
  }

  const frontMatterBlock = match[1];
  const content = match[2];
  const data: Record<string, string | number> = {};

  for (const line of frontMatterBlock.split("\n")) {
    const colonIdx = line.indexOf(":");
    if (colonIdx === -1) continue;
    const key = line.slice(0, colonIdx).trim();
    let value: string | number = line.slice(colonIdx + 1).trim();
    // Strip surrounding quotes
    if (value.startsWith('"') && value.endsWith('"')) {
      value = value.slice(1, -1);
    }
    // Parse numbers
    if (/^\d+$/.test(String(value))) {
      value = parseInt(String(value), 10);
    }
    data[key] = value;
  }

  return { data: data as unknown as FrontMatter, content };
}

function loadSection(
  globResult: Record<string, { default: string } | string>
): DocPage[] {
  return Object.entries(globResult)
    .map(([path, module]) => {
      const raw = typeof module === "string" ? module : (module as { default: string }).default;
      const { data, content } = parseFrontMatter(raw);
      const slug = path.split("/").pop()!.replace(/\.md$/, "");
      return {
        slug,
        title: data.title,
        description: data.description,
        content,
        order: data.order ?? 99,
      };
    })
    .sort((a, b) => a.order - b.order);
}

// Glob import all .md files per section
const overviewFiles = import.meta.glob("./overview/*.md", {
  query: "?raw",
  eager: true,
});
const foundationsFiles = import.meta.glob("./foundations/*.md", {
  query: "?raw",
  eager: true,
});
const gettingStartedFiles = import.meta.glob("./getting-started/*.md", {
  query: "?raw",
  eager: true,
});
const tourFiles = import.meta.glob("./tour/*.md", {
  query: "?raw",
  eager: true,
});
const advancedFiles = import.meta.glob("./advanced/*.md", {
  query: "?raw",
  eager: true,
});
const comingFromOtherLanguagesFiles = import.meta.glob("./coming-from-other-languages/*.md", {
  query: "?raw",
  eager: true,
});
const stdlibFiles = import.meta.glob("./stdlib/*.md", {
  query: "?raw",
  eager: true,
});
const standardToolsFiles = import.meta.glob("./standard-tools/*.md", {
  query: "?raw",
  eager: true,
});
const cookbookFiles = import.meta.glob("./cookbook/*.md", {
  query: "?raw",
  eager: true,
});
const specificationsFiles = import.meta.glob("./specifications/*.md", {
  query: "?raw",
  eager: true,
});

export const overviewPages = loadSection(overviewFiles as Record<string, { default: string }>);
export const foundationsPages = loadSection(foundationsFiles as Record<string, { default: string }>);
export const gettingStartedPages = loadSection(gettingStartedFiles as Record<string, { default: string }>);
export const tourPages = loadSection(tourFiles as Record<string, { default: string }>);
export const advancedPages = loadSection(advancedFiles as Record<string, { default: string }>);
export const comingFromOtherLanguagesPages = loadSection(
  comingFromOtherLanguagesFiles as Record<string, { default: string }>
);
export const stdlibPages = loadSection(stdlibFiles as Record<string, { default: string }>);
export const standardToolsPages = loadSection(standardToolsFiles as Record<string, { default: string }>);
export const cookbookPages = loadSection(cookbookFiles as Record<string, { default: string }>);
export const specificationsPages = loadSection(specificationsFiles as Record<string, { default: string }>);
