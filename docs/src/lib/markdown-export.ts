import type { NavPage } from "./nav";

/**
 * Markdown for a page as served to LLMs and other machine readers.
 * The page title becomes the single h1 and body headings are shifted down a
 * level, matching the rendered HTML.
 */
export function pageMarkdown(page: NavPage, heading = page.title): string {
  const lines = [`# ${heading}`, ""];
  if (page.description) {
    lines.push(page.description, "");
  }
  lines.push(shiftHeadings((page.entry.body ?? "").trim()), "");
  return lines.join("\n");
}

function shiftHeadings(markdown: string): string {
  let inFence = false;
  return markdown
    .split("\n")
    .map((line) => {
      if (/^\s*(```|~~~)/.test(line)) {
        inFence = !inFence;
        return line;
      }
      if (!inFence && /^#{1,5}\s/.test(line)) {
        return `#${line}`;
      }
      return line;
    })
    .join("\n");
}
