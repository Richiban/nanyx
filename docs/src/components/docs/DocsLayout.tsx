import { useState } from "react";
import { Outlet } from "react-router-dom";
import { DocsHeader } from "./DocsHeader";
import { DocsSidebar } from "./DocsSidebar";
import { SearchDialog } from "./SearchDialog";

export function DocsLayout() {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [searchOpen, setSearchOpen] = useState(false);

  return (
    <div className="docs-layout-root">
      <DocsHeader
        onToggleSidebar={() => setSidebarOpen(!sidebarOpen)}
        onOpenSearch={() => setSearchOpen(true)}
      />
      <div className="docs-layout-body">
        <DocsSidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} />
        <main className="docs-layout-main">
          <Outlet />
        </main>
      </div>
      <SearchDialog open={searchOpen} onOpenChange={setSearchOpen} />
    </div>
  );
}
