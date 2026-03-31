import { useParams, useLocation, Navigate } from "react-router-dom";
import { findPage, getAdjacentPages, navSections } from "@/content/navigation";
import { MarkdownRenderer } from "@/components/docs/MarkdownRenderer";
import { DocsBreadcrumbs } from "@/components/docs/DocsBreadcrumbs";
import { PrevNextNav } from "@/components/docs/PrevNextNav";
import { useEffect } from "react";

export default function DocPage() {
  const location = useLocation();
  const path = location.pathname;

  const entry = findPage(path);

  useEffect(() => {
    window.scrollTo(0, 0);
  }, [path]);

  if (!entry) {
    // Redirect to first page if at a section root
    const section = navSections.find((s) => path === s.basePath || path === s.basePath + "/");
    if (section && section.pages.length > 0) {
      return <Navigate to={`${section.basePath}/${section.pages[0].slug}`} replace />;
    }
    // Redirect /docs to first page
    if (path === "/docs" || path === "/docs/") {
      return <Navigate to="/docs/getting-started/what-is-nanyx" replace />;
    }
    return (
      <div className="py-12 text-center">
        <h1 className="text-2xl font-bold mb-2" style={{ fontFamily: "'Space Grotesk', system-ui" }}>Page Not Found</h1>
        <p className="text-muted-foreground">The documentation page you're looking for doesn't exist.</p>
      </div>
    );
  }

  const { page, section } = entry;
  const content = page.content.replace(/^#\s+.*\n+/, "");
  const adjacent = getAdjacentPages(path);

  return (
    <article>
      <DocsBreadcrumbs
        items={[
          { label: section.title, href: `${section.basePath}/${section.pages[0].slug}` },
          { label: page.title },
        ]}
      />
      <header className="mb-8">
        <h1 className="text-3xl font-bold mb-2" style={{ fontFamily: "'Space Grotesk', system-ui" }}>
          {page.title}
        </h1>
        {page.description && <p className="doc-description text-muted-foreground text-base">{page.description}</p>}
      </header>
      <MarkdownRenderer content={content} />
      <PrevNextNav prev={adjacent.prev} next={adjacent.next} />
    </article>
  );
}
