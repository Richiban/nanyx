import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { createHighlighter } from "shiki/bundle/web";
import nyxGrammar from "../../../../extension/syntaxes/nyx.tmLanguage.json";
import { Check, Copy } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { cn } from "@/lib/utils";

interface MarkdownRendererProps {
  content: string;
  className?: string;
}

const languageAliases: Record<string, string> = {
  nyx: "nyx",
  nanyx: "nyx",
  "c#": "csharp",
  "f#": "fsharp",
};

function CopyButton({ code }: { code: string }) {
  const [copied, setCopied] = useState(false);

  const handleCopy = () => {
    navigator.clipboard.writeText(code);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <button
      onClick={handleCopy}
      className="absolute top-3 right-3 p-1.5 rounded-md bg-muted/50 hover:bg-muted text-muted-foreground hover:text-foreground transition-colors"
      aria-label="Copy code"
    >
      {copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
    </button>
  );
}

export function MarkdownRenderer({ content, className }: MarkdownRendererProps) {
  const [highlighter, setHighlighter] = useState<Awaited<ReturnType<typeof createHighlighter>> | null>(
    null
  );
  const [isDark, setIsDark] = useState(false);

  useEffect(() => {
    let isMounted = true;
    const loadHighlighter = async () => {
      const instance = await createHighlighter({
        themes: ["github-dark", "github-light"],
        langs: [
          {
            ...(nyxGrammar as Record<string, unknown>),
            name: "nyx",
            scopeName: "source.nyx",
            aliases: ["nanyx"],
          },
        ],
      });

      if (isMounted) {
        setHighlighter(instance);
      }
    };

    loadHighlighter();

    return () => {
      isMounted = false;
    };
  }, []);

  useEffect(() => {
    const root = document.documentElement;
    const updateTheme = () => {
      setIsDark(root.classList.contains("dark"));
    };

    updateTheme();

    const observer = new MutationObserver(updateTheme);
    observer.observe(root, { attributes: true, attributeFilter: ["class"] });

    return () => observer.disconnect();
  }, []);

  const loadedLanguages = useMemo(() => {
    if (!highlighter) return new Set<string>();
    return new Set(highlighter.getLoadedLanguages());
  }, [highlighter]);

  return (
    <div className={cn("prose", className)}>
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={{
          pre({ children }) {
            return <>{children}</>;
          },
          code({ className, children, inline, ...props }) {
            const match = /language-(\w+)/.exec(className || "");
            const codeString = String(children).replace(/\n$/, "");

            if (!inline && match) {
              const rawLang = match[1].toLowerCase();
              const normalizedLang = languageAliases[rawLang] ?? rawLang;
              const canHighlight = highlighter && loadedLanguages.has(normalizedLang);
              const theme = isDark ? "github-dark" : "github-light";

              return (
                <div className="relative group">
                  <CopyButton code={codeString} />
                  {canHighlight ? (
                    <div
                      dangerouslySetInnerHTML={{
                        __html: highlighter.codeToHtml(codeString, {
                          lang: normalizedLang,
                          theme,
                        }),
                      }}
                    />
                  ) : (
                    <pre className="rounded-lg p-4 mb-4 overflow-x-auto text-sm border bg-[hsl(var(--code-bg))] border-[hsl(var(--code-border))]">
                      <code>{codeString}</code>
                    </pre>
                  )}
                </div>
              );
            }

            return (
              <code className={className} {...props}>
                {children}
              </code>
            );
          },
        }}
      >
        {content}
      </ReactMarkdown>
    </div>
  );
}
