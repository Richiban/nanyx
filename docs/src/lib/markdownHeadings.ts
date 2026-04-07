export interface MarkdownHeading {
  id: string;
  text: string;
  level: number;
}

function normalizeHeadingText(text: string): string {
  return text
    .replace(/`([^`]+)`/g, "$1")
    .replace(/\[([^\]]+)\]\([^)]+\)/g, "$1")
    .replace(/[*_~]/g, "")
    .trim();
}

export function slugifyHeading(text: string): string {
  const normalized = normalizeHeadingText(text)
    .toLowerCase()
    .replace(/[^\w\s-]/g, "")
    .trim()
    .replace(/\s+/g, "-");

  return normalized || "section";
}

export function createHeadingIdGenerator() {
  const seen = new Map<string, number>();

  return (text: string) => {
    const base = slugifyHeading(text);
    const nextCount = (seen.get(base) ?? 0) + 1;
    seen.set(base, nextCount);
    return nextCount === 1 ? base : `${base}-${nextCount}`;
  };
}

export function extractHeadingsFromMarkdown(content: string): MarkdownHeading[] {
  const headingId = createHeadingIdGenerator();
  const headings: MarkdownHeading[] = [];

  const lines = content.split(/\r?\n/);
  let inFence = false;

  for (const line of lines) {
    if (/^```/.test(line.trim())) {
      inFence = !inFence;
      continue;
    }

    if (inFence) {
      continue;
    }

    const match = /^(#{1,6})\s+(.+?)\s*#*\s*$/.exec(line);
    if (!match) {
      continue;
    }

    const level = match[1].length;
    const text = normalizeHeadingText(match[2]);
    if (!text) {
      continue;
    }

    headings.push({
      id: headingId(text),
      text,
      level,
    });
  }

  return headings;
}
