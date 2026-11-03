import type { APIRoute } from "astro";
import { getAllPages } from "@/lib/nav";
import { pageMarkdown } from "@/lib/markdown-export";

export async function getStaticPaths() {
  const all = await getAllPages();
  return all.map(({ page }) => ({
    params: { slug: page.entry.id },
    props: { page },
  }));
}

export const GET: APIRoute = ({ props }) => {
  return new Response(pageMarkdown(props.page), {
    headers: { "Content-Type": "text/markdown; charset=utf-8" },
  });
};
