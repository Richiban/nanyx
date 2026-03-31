import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { createHighlighter } from "shiki/bundle/web";
import toml from "shiki/langs/toml";
import nyxGrammar from "../../../../extension/src/syntaxes/nanyx.tmLanguage.json";
import { Check, Copy } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { cn } from "@/lib/utils";
import { set } from "date-fns";

interface MarkdownRendererProps {
  content: string;
  className?: string;
}

const languageAliases: Record<string, string> = {
  nyx: "nyx",
  nanyx: "nyx",
  bash: "bash",
  sh: "bash",
  toml: "toml",
};

const paperWhiteTheme = {
  name: "paper-white",
  type: "light",
  colors: {
    "editor.background": "#f7f5f2",
    "editor.foreground": "#2b2b2b",
  },
  settings: [
    { settings: { foreground: "#2b2b2b" } },
    {
      scope: ["comment", "punctuation.definition.comment"],
      settings: { foreground: "#7a7a7a" },
    },
    {
      scope: ["string", "punctuation.definition.string"],
      settings: { foreground: "#c94c12" },
    },
    {
      scope: ["constant.numeric", "constant.language"],
      settings: { foreground: "#098658" },
    },
    { scope: ["keyword.control.nanyx"], settings: { foreground: "#c244b1" } },
    { scope: ["keyword.control"], settings: { foreground: "#c244b1" } },
    {
      scope: ["keyword.declaration.nanyx"],
      settings: { foreground: "#2b579a" },
    },
    { scope: ["keyword.nanyx"], settings: { foreground: "#2b579a" } },
    {
      scope: ["entity.name.function", "support.function"],
      settings: { foreground: "#795e26" },
    },
    {
      scope: ["entity.name.type", "support.type"],
      settings: { foreground: "#23a87c" },
    },
    {
      scope: ["variable", "variable.parameter"],
      settings: { foreground: "#001080" },
    },
    {
      scope: ["entity.name.tag", "support.class"],
      settings: { foreground: "#448eca" },
    },
    {
      scope: ["tag.nanyx", "support.type.nanyx"],
      settings: { foreground: "#1b8a5a" },
    },
    {
      scope: [
        "punctuation.section",
        "punctuation.section.brackets",
        "punctuation.section.parens",
        "punctuation.section.block",
        "punctuation.definition.group",
        "keyword.symbol.nanyx",
        "keyword.symbol.arrow.nanyx",
      ],
      settings: { foreground: "#4a4a4a" },
    },
    { scope: ["keyword.operator"], settings: { foreground: "#000000" } },
  ],
} as const;

const inkNightTheme = {
  name: "ink-night",
  type: "dark",
  colors: {
    "editor.background": "#111214",
    "editor.foreground": "#e6e6e6",
  },
  settings: [
    { settings: { foreground: "#e6e6e6" } },
    {
      scope: ["comment", "punctuation.definition.comment"],
      settings: { foreground: "#8b8f98" },
    },
    {
      scope: ["string", "punctuation.definition.string"],
      settings: { foreground: "#f28f6b" },
    },
    {
      scope: ["constant.numeric", "constant.language"],
      settings: { foreground: "#55c99b" },
    },
    { scope: ["keyword.control.nanyx"], settings: { foreground: "#f07ad8" } },
    { scope: ["keyword.control"], settings: { foreground: "#f07ad8" } },
    {
      scope: ["keyword.declaration.nanyx"],
      settings: { foreground: "#82b1ff" },
    },
    { scope: ["keyword.nanyx"], settings: { foreground: "#82b1ff" } },
    {
      scope: ["entity.name.function", "support.function"],
      settings: { foreground: "#f4bf75" },
    },
    {
      scope: ["entity.name.type", "support.type"],
      settings: { foreground: "#6ee7b7" },
    },
    {
      scope: ["entity.name.tag", "tag.nanyx"],
      settings: { foreground: "#7dfce7" },
    },
    {
      scope: ["punctuation.definition.tag.nanyx"],
      settings: { foreground: "#85beb4" },
    },
    {
      scope: ["support.type.nanyx"],
      settings: { foreground: "#34d3ab" },
    },
    {
      scope: [
        "punctuation.section",
        "punctuation.section.brackets",
        "punctuation.section.parens",
        "punctuation.section.block",
        "punctuation.definition.group",
        "keyword.symbol.nanyx",
        "keyword.symbol.arrow.nanyx",
      ],
      settings: { foreground: "#999797" },
    },
    { scope: ["keyword.operator"], settings: { foreground: "#999797" } },
  ],
} as const;

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
      {copied ? (
        <Check className="h-3.5 w-3.5" />
      ) : (
        <Copy className="h-3.5 w-3.5" />
      )}
    </button>
  );
}

export function MarkdownRenderer({
  content,
  className,
}: MarkdownRendererProps) {
  const [highlighter, setHighlighter] = useState<Awaited<
    ReturnType<typeof createHighlighter>
  > | null>(null);
  const [isDark, setIsDark] = useState(false);

  useEffect(() => {
    let isMounted = true;
    const loadHighlighter = async () => {
      const instance = await createHighlighter({
        themes: [inkNightTheme, paperWhiteTheme],
        langs: [
          "bash",
          toml,
          {
            ...(nyxGrammar as Record<string, unknown>),
            name: "nyx",
            scopeName:
              (nyxGrammar as { scopeName?: string }).scopeName ||
              "source.nanyx",
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
              const canHighlight =
                highlighter && loadedLanguages.has(normalizedLang);
              const theme = isDark ? "ink-night" : "paper-white";

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
