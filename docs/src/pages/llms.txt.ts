import type { APIRoute } from "astro";
import { getNav } from "@/lib/nav";

export const GET: APIRoute = async ({ site }) => {
  const nav = await getNav();
  const origin = site?.origin ?? "";

  const lines = [
    "# Nanyx",
    "",
    "> Nanyx is a type-safe, expressive language for building reliable software.",
    "",
    "This site is the Nanyx language documentation. Every page is available as",
    `plain markdown by appending \`.md\` to its URL, and ${origin}/llms-full.txt`,
    "contains the whole documentation set in one file.",
    "",
  ];

  for (const section of nav) {
    lines.push(`## ${section.title}`, "");
    for (const page of section.pages) {
      const description = page.description ? `: ${page.description}` : "";
      lines.push(`- [${page.title}](${origin}${page.path}.md)${description}`);
    }
    lines.push("");
  }

  return new Response(lines.join("\n"), {
    headers: { "Content-Type": "text/plain; charset=utf-8" },
  });
};
