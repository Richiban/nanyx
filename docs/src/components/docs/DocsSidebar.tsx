import { Link, useLocation } from "react-router-dom";
import { navSections } from "@/content/navigation";
import { ChevronDown, Rocket, Map, Library, BookOpen, ScrollText, Menu, X, View, Sparkles, Languages } from "lucide-react";
import { useState } from "react";
import { cn } from "@/lib/utils";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";

const iconMap: Record<string, React.ElementType> = {
  Rocket,
  View,
  Map,
  Sparkles,
  Languages,
  Library,
  BookOpen,
  ScrollText,
};

interface DocsSidebarProps {
  open: boolean;
  onClose: () => void;
}

export function DocsSidebar({ open, onClose }: DocsSidebarProps) {
  const location = useLocation();
  const currentPath = location.pathname;

  return (
    <>
      {/* Mobile overlay */}
      {open && (
        <div className="fixed inset-0 z-40 bg-background/80 backdrop-blur-sm md:hidden" onClick={onClose} />
      )}

      <aside
        className={cn(
          "fixed top-16 bottom-0 left-0 z-50 w-72 border-r border-border bg-card overflow-y-auto transition-transform duration-200 md:sticky md:top-16 md:h-[calc(100vh-4rem)] md:translate-x-0 md:z-0",
          open ? "translate-x-0" : "-translate-x-full"
        )}
      >
        <nav className="p-4 space-y-1">
          {navSections.map((section) => {
            const Icon = iconMap[section.icon] || Rocket;
            const isActive = currentPath.startsWith(section.basePath);

            return (
              <Collapsible key={section.title} defaultOpen={isActive}>
                <CollapsibleTrigger className="flex items-center gap-2 w-full px-3 py-2 text-sm font-semibold rounded-md hover:bg-accent transition-colors group">
                  <Icon className="h-4 w-4 text-primary" />
                  <span className="flex-1 text-left" style={{ fontFamily: "'Space Grotesk', system-ui, sans-serif" }}>
                    {section.title}
                  </span>
                  <ChevronDown className="h-3.5 w-3.5 text-muted-foreground transition-transform group-data-[state=open]:rotate-180" />
                </CollapsibleTrigger>
                <CollapsibleContent>
                  <div className="ml-4 pl-3 border-l border-border space-y-0.5 mt-1 mb-2">
                    {section.pages.map((page) => {
                      const pagePath = `${section.basePath}/${page.slug}`;
                      const isPageActive = currentPath === pagePath;

                      return (
                        <Link
                          key={page.slug}
                          to={pagePath}
                          onClick={onClose}
                          className={cn(
                            "block px-3 py-1.5 text-sm rounded-md transition-colors",
                            isPageActive
                              ? "bg-accent text-accent-foreground font-medium"
                              : "text-muted-foreground hover:text-foreground hover:bg-accent/50"
                          )}
                        >
                          {page.title}
                        </Link>
                      );
                    })}
                  </div>
                </CollapsibleContent>
              </Collapsible>
            );
          })}
        </nav>
      </aside>
    </>
  );
}
