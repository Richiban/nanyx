import { useState } from "react";
import { Outlet } from "react-router-dom";
import { DocsHeader } from "./DocsHeader";
import { DocsSidebar } from "./DocsSidebar";
import { SearchDialog } from "./SearchDialog";

export function DocsLayout() {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [searchOpen, setSearchOpen] = useState(false);

  return (
    <div className="min-h-screen bg-background">
      <DocsHeader
        onToggleSidebar={() => setSidebarOpen(!sidebarOpen)}
        onOpenSearch={() => setSearchOpen(true)}
      />
      <div className="flex">
        <DocsSidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} />
        <main className="flex-1 min-w-0 px-6 py-8 md:px-12 lg:px-16">
          <Outlet />
        </main>
      </div>
      <SearchDialog open={searchOpen} onOpenChange={setSearchOpen} />
    </div>
  );
}
