import type { APIRoute } from "astro";
import { getNav } from "@/lib/nav";
import { pageMarkdown } from "@/lib/markdown-export";

export const GET: APIRoute = async ({ site }) => {
  const nav = await getNav();
  const origin = site?.origin ?? "";

  const parts = ["# Nanyx documentation", ""];

  for (const section of nav) {
    for (const page of section.pages) {
      parts.push(
        pageMarkdown(page, `${section.title}: ${page.title}`),
        `Source: ${origin}${page.path}`,
        "",
        "---",
        "",
      );
    }
  }

  return new Response(parts.join("\n"), {
    headers: { "Content-Type": "text/plain; charset=utf-8" },
  });
};
