/** Heading-style slug, matching the ids Astro generates for markdown headings. */
export function slugify(text: string): string {
  const slug = text
    .toLowerCase()
    .replace(/`/g, "")
    .replace(/[^\p{L}\p{N}\s-]/gu, "")
    .trim()
    .replace(/\s+/g, "-");
  return slug || "section";
}
