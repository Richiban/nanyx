import { Link } from "react-router-dom";
import { ArrowRight, Rocket, Map, Library, BookOpen } from "lucide-react";
import { navSections } from "@/content/navigation";

const iconMap: Record<string, React.ElementType> = {
  Rocket,
  Map,
  Library,
  BookOpen,
};

const Index = () => {
  return (
    <div className="min-h-screen bg-background">
      {/* Hero */}
      <header className="relative overflow-hidden">
        <div className="absolute inset-0 bg-gradient-to-br from-primary/5 via-transparent to-secondary/5" />
        <div className="relative max-w-5xl mx-auto px-6 py-24 md:py-32 text-center">
          <div className="inline-flex items-center gap-2.5 mb-8">
            <div className="h-12 w-12 rounded-xl flex items-center justify-center shadow-lg shadow-primary/25">
              <img src="/nanyx.svg"/>
            </div>
            <h1 className="text-4xl md:text-5xl font-bold tracking-tight" style={{ fontFamily: "'Space Grotesk', system-ui, sans-serif" }}>
              Nanyx
            </h1>
          </div>
          <p className="text-lg md:text-xl text-muted-foreground max-w-2xl mx-auto mb-10 leading-relaxed">
            A type-safe, expressive language for building reliable software.
            <br className="hidden md:block" />
            Simple to learn. Powerful to use.
          </p>
          <div className="flex items-center justify-center gap-4">
            <Link
              to="/docs/getting-started/what-is-nanyx"
              className="inline-flex items-center gap-2 px-6 py-3 rounded-lg bg-primary text-primary-foreground font-medium hover:bg-primary/90 transition-colors shadow-md shadow-primary/20"
            >
              Get Started
              <ArrowRight className="h-4 w-4" />
            </Link>
            <Link
              to="/docs/tour/variables"
              className="inline-flex items-center gap-2 px-6 py-3 rounded-lg border border-border text-foreground font-medium hover:bg-accent transition-colors"
            >
              Language Tour
            </Link>
          </div>

          {/* Code preview */}
          <div className="mt-16 max-w-lg mx-auto text-left">
            <div className="rounded-xl bg-[#111827] border border-border/50 shadow-2xl overflow-hidden">
              <div className="flex items-center gap-1.5 px-4 py-3 border-b border-white/10">
                <div className="h-3 w-3 rounded-full bg-red-500/70" />
                <div className="h-3 w-3 rounded-full bg-yellow-500/70" />
                <div className="h-3 w-3 rounded-full bg-green-500/70" />
                <span className="ml-2 text-xs text-white/40 font-mono">hello.nx</span>
              </div>
              <pre className="p-5 text-sm leading-relaxed font-mono">
                <code>
                  <span className="text-purple-400">import</span>{" "}
                  <span className="text-cyan-300">nanyx/io</span>{"\n\n"}
                  <span className="text-purple-400">def</span>{" "}
                  <span className="text-yellow-300">main</span>
                  <span className="text-white"> = {"{"}</span>{"\n"}
                  <span className="text-white">{"  "}</span>
                  <span className="text-yellow-300">println</span>
                  <span className="text-white">(</span>
                  <span className="text-green-400">"Hello, Nanyx! 🚀"</span>
                  <span className="text-white">)</span>{"\n"}
                  <span className="text-white">{"}"}</span>
                </code>
              </pre>
            </div>
          </div>
        </div>
      </header>

      {/* Section cards */}
      <section className="max-w-5xl mx-auto px-6 pb-24">
        <div className="grid gap-4 md:grid-cols-2">
          {navSections.map((section) => {
            const Icon = iconMap[section.icon] || Rocket;
            return (
              <Link
                key={section.title}
                to={`${section.basePath}/${section.pages[0].slug}`}
                className="group p-6 rounded-xl border border-border hover:border-primary/30 hover:bg-accent/30 transition-all"
              >
                <div className="flex items-start gap-4">
                  <div className="h-10 w-10 rounded-lg bg-accent flex items-center justify-center shrink-0">
                    <Icon className="h-5 w-5 text-primary" />
                  </div>
                  <div>
                    <h2 className="font-semibold mb-1 group-hover:text-primary transition-colors" style={{ fontFamily: "'Space Grotesk', system-ui" }}>
                      {section.title}
                    </h2>
                    <p className="text-sm text-muted-foreground">
                      {section.pages.length} {section.pages.length === 1 ? "page" : "pages"} ·{" "}
                      {section.pages.map((p) => p.title).slice(0, 3).join(", ")}
                      {section.pages.length > 3 && "..."}
                    </p>
                  </div>
                </div>
              </Link>
            );
          })}
        </div>
      </section>
    </div>
  );
};

export default Index;
