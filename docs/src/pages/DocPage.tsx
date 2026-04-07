import { useLocation, Navigate } from "react-router-dom";
import { findPage, getAdjacentPages, navSections } from "@/content/navigation";
import { MarkdownRenderer } from "@/components/docs/MarkdownRenderer";
import { DocsBreadcrumbs } from "@/components/docs/DocsBreadcrumbs";
import { PrevNextNav } from "@/components/docs/PrevNextNav";
import { useEffect, useMemo, useState } from "react";
import { ListTree } from "lucide-react";
import { cn } from "@/lib/utils";
import { extractHeadingsFromMarkdown, slugifyHeading } from "@/lib/markdownHeadings";

export default function DocPage() {
  const location = useLocation();
  const path = location.pathname;
  const [tocOpen, setTocOpen] = useState(false);
  const [activeIds, setActiveIds] = useState<Set<string>>(new Set());

  const entry = findPage(path);
  const content = entry ? entry.page.content.replace(/^#\s+.*\n+/, "") : "";

  const headings = useMemo(
    () => {
      const bodyHeadings = extractHeadingsFromMarkdown(content);
      if (entry) {
        const titleHeading = { id: slugifyHeading(entry.page.title), text: entry.page.title, level: 1 };
        return [titleHeading, ...bodyHeadings];
      }
      return bodyHeadings;
    },
    [content, entry]
  );

  // Track which headings are currently visible in the viewport
  useEffect(() => {
    const ids = headings.map((h) => h.id);
    if (ids.length === 0) return;

    let ticking = false;

    const updateActiveHeadings = () => {
      const next = new Set<string>();
      const viewportTop = 80; // account for sticky header
      const viewportBottom = window.innerHeight;

      for (let i = 0; i < ids.length; i++) {
        const el = document.getElementById(ids[i]);
        if (!el) continue;

        // Section runs from this heading to the next heading (or end of document)
        const sectionTop = el.getBoundingClientRect().top;
        let sectionBottom: number;

        if (i + 1 < ids.length) {
          const nextEl = document.getElementById(ids[i + 1]);
          sectionBottom = nextEl ? nextEl.getBoundingClientRect().top : viewportBottom;
        } else {
          // Last section extends to the bottom of the document
          sectionBottom = document.documentElement.getBoundingClientRect().bottom;
        }

        // Section is visible if it overlaps the viewport
        if (sectionBottom > viewportTop && sectionTop < viewportBottom) {
          next.add(ids[i]);
        }
      }
      setActiveIds((prev) => {
        if (prev.size !== next.size) {
          return next;
        }

        for (const id of next) {
          if (!prev.has(id)) {
            return next;
          }
        }

        return prev;
      });
      ticking = false;
    };

    const handleScroll = () => {
      if (!ticking) {
        ticking = true;
        requestAnimationFrame(updateActiveHeadings);
      }
    };

    // Defer initial check so MarkdownRenderer has time to mount headings
    const initialTimer = requestAnimationFrame(updateActiveHeadings);
    window.addEventListener("scroll", handleScroll, { passive: true });

    return () => {
      cancelAnimationFrame(initialTimer);
      window.removeEventListener("scroll", handleScroll);
    };
  }, [headings]);

  useEffect(() => {
    window.scrollTo(0, 0);
  }, [path]);

  useEffect(() => {
    if (!location.hash) {
      return;
    }

    const id = decodeURIComponent(location.hash.slice(1));
    const scrollToTarget = () => {
      const element = document.getElementById(id);
      if (element) {
        element.scrollIntoView({ block: "start", behavior: "auto" });
      }
    };

    const frame = window.requestAnimationFrame(scrollToTarget);
    return () => window.cancelAnimationFrame(frame);
  }, [location.hash, content]);

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
  const adjacent = getAdjacentPages(path);

  const renderHeadingLinks = (mobile: boolean) => (
    <nav
      aria-label="Table of contents"
      className={cn(
        "toc rounded-lg",
        mobile ? "p-3" : "p-4"
      )}
    >
      <h2 className="text-sm font-semibold mb-2" style={{ fontFamily: "'Space Grotesk', system-ui" }}>
        Contents
      </h2>
      <ul className={cn("toc-items", mobile ? "max-h-64 overflow-y-auto" : "max-h-[calc(100vh-9rem)] overflow-y-auto") }>
        {headings.map((heading) => {
          const isActive = activeIds.has(heading.id);
          return (
            <li key={heading.id}>
              <a
                href={`#${heading.id}`}
                onClick={() => {
                  if (mobile) {
                    setTocOpen(false);
                  }
                }}
                className={cn("toc-link", isActive && "toc-active", heading.level === 1 && "toc-h1")}
                style={{ paddingLeft: `calc(1ch + ${Math.max(0, heading.level - 1) * 12}px)` }}
              >
                {heading.text}
              </a>
            </li>
          );
        })}
      </ul>
    </nav>
  );

  return (
    <article className="max-w-7xl lg:grid lg:grid-cols-[minmax(0,1fr)_18rem] lg:gap-8">
      <div className="min-w-0">
        <DocsBreadcrumbs
          items={[
            { label: section.title, href: `${section.basePath}/${section.pages[0].slug}` },
            { label: page.title },
          ]}
        />
        <header className="mb-5">
          <h1 id={slugifyHeading(page.title)} className="text-3xl font-bold mb-2" style={{ fontFamily: "'Space Grotesk', system-ui" }}>
            {page.title}
          </h1>
          {page.description && <p className="doc-description text-muted-foreground text-base">{page.description}</p>}
        </header>

        {headings.length > 0 && (
          <div className="mb-6 lg:hidden">
            <button
              type="button"
              onClick={() => setTocOpen((open) => !open)}
              className="inline-flex items-center gap-2 rounded-lg border border-border bg-card px-3 py-2 text-sm font-medium hover:bg-accent transition-colors"
            >
              <ListTree className="h-4 w-4" />
              Table of contents
            </button>
            {tocOpen && <div className="mt-3">{renderHeadingLinks(true)}</div>}
          </div>
        )}

        <MarkdownRenderer content={content} />
        <PrevNextNav prev={adjacent.prev} next={adjacent.next} />
      </div>

      {headings.length > 0 && (
        <aside className="hidden lg:block">
          <div className="sticky top-20">{renderHeadingLinks(false)}</div>
        </aside>
      )}
    </article>
  );
}
