import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { createHighlighter, type ThemeInput } from "shiki/bundle/web";
import mermaid from "mermaid";
// eslint-disable-next-line @typescript-eslint/ban-ts-comment
// @ts-ignore -- shiki/langs/toml lacks type declarations
import toml from "shiki/langs/toml";
import nyxGrammar from "../../../../extension/src/syntaxes/nanyx.tmLanguage.json";
import { Check, Copy, Link2 } from "lucide-react";
import { memo, useEffect, useMemo, useRef, useState, createContext, useContext, type ReactNode } from "react";
import { cn } from "@/lib/utils";
import { extractHeadingsFromMarkdown, slugifyHeading } from "@/lib/markdownHeadings";

const PreContext = createContext(false);

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

function MermaidDiagram({ chart, isDark }: { chart: string; isDark: boolean }) {
  const [svg, setSvg] = useState<string>("");
  const [error, setError] = useState<string | null>(null);
  const diagramIdRef = useRef(`mermaid-${Math.random().toString(36).slice(2, 10)}`);

  useEffect(() => {
    let active = true;

    const render = async () => {
      try {
        mermaid.initialize({
          startOnLoad: false,
          securityLevel: "loose",
          theme: isDark ? "dark" : "default",
        });

        const { svg: renderedSvg } = await mermaid.render(diagramIdRef.current, chart);
        if (!active) {
          return;
        }
        setSvg(renderedSvg);
        setError(null);
      } catch {
        if (!active) {
          return;
        }
        setError("Unable to render Mermaid diagram.");
      }
    };

    render();

    return () => {
      active = false;
    };
  }, [chart, isDark]);

  if (error) {
    return (
      <pre className="rounded-lg p-4 mb-4 overflow-x-auto text-sm border bg-[hsl(var(--code-bg))] border-[hsl(var(--code-border))]">
        <code>{chart}</code>
      </pre>
    );
  }

  return (
    <div className="rounded-lg p-4 mb-4 overflow-x-auto border bg-[hsl(var(--code-bg))] border-[hsl(var(--code-border))]">
      {svg ? <div dangerouslySetInnerHTML={{ __html: svg }} /> : <div className="text-sm text-muted-foreground">Rendering diagram...</div>}
    </div>
  );
}

function MarkdownRendererImpl({
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
        themes: [inkNightTheme as unknown as ThemeInput, paperWhiteTheme as unknown as ThemeInput],
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

  const markdownHeadings = useMemo(() => extractHeadingsFromMarkdown(content), [content]);
  const [copiedHeadingId, setCopiedHeadingId] = useState<string | null>(null);

  const extractText = (children: ReactNode): string => {
    if (typeof children === "string") {
      return children;
    }

    if (typeof children === "number") {
      return String(children);
    }

    if (Array.isArray(children)) {
      return children.map(extractText).join("");
    }

    if (children && typeof children === "object" && "props" in children) {
      const props = (children as { props?: { children?: ReactNode } }).props;
      return extractText(props?.children ?? "");
    }

    return "";
  };

  const copyHeadingLink = (id: string) => {
    const hashUrl = `${window.location.origin}${window.location.pathname}${window.location.search}#${id}`;
    navigator.clipboard.writeText(hashUrl);
    setCopiedHeadingId(id);
    setTimeout(() => setCopiedHeadingId(null), 1500);
  };

  let headingIndex = 0;

  const makeHeading = (tag: "h1" | "h2" | "h3" | "h4" | "h5" | "h6") => {
    return ({ children, ...props }: { children?: ReactNode }) => {
      const text = extractText(children ?? "").trim();
      const level = Number(tag.slice(1));
      const heading = markdownHeadings[headingIndex];
      const id =
        heading && heading.text === text && heading.level === level
          ? heading.id
          : slugifyHeading(text);
      headingIndex += 1;
      const Tag = tag;

      return (
        <Tag id={id} className="group scroll-mt-24" {...props}>
          <span>{children}</span>
          <button
            type="button"
            onClick={() => copyHeadingLink(id)}
            className="ml-2 inline-flex align-middle text-muted-foreground opacity-0 transition-opacity hover:text-foreground group-hover:opacity-100 group-focus-within:opacity-100"
            aria-label={`Copy link to ${text}`}
          >
            {copiedHeadingId === id ? <Check className="h-4 w-4" /> : <Link2 className="h-4 w-4" />}
          </button>
        </Tag>
      );
    };
  };

  return (
    <div className={cn("prose", className)}>
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={{
          h1: makeHeading("h1"),
          h2: makeHeading("h2"),
          h3: makeHeading("h3"),
          h4: makeHeading("h4"),
          h5: makeHeading("h5"),
          h6: makeHeading("h6"),
          pre({ children }) {
            return <PreContext.Provider value={true}>{children}</PreContext.Provider>;
          },
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          code: function CodeComponent(codeProps: any) {
            const { className, children, inline, ...props } = codeProps;
            const insidePre = useContext(PreContext);
            const match = /language-(\w+)/.exec(className || "");
            const codeString = String(children).replace(/\n$/, "");
            const isInline = inline ?? !insidePre;

            if (!isInline && match) {
              const rawLang = match[1].toLowerCase();
              const normalizedLang = languageAliases[rawLang] ?? rawLang;

              if (normalizedLang === "mermaid") {
                return <MermaidDiagram chart={codeString} isDark={isDark} />;
              }

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

            if (!isInline) {
              return (
                <pre className="rounded-lg p-4 mb-4 overflow-x-auto text-sm border bg-[hsl(var(--code-bg))] border-[hsl(var(--code-border))]">
                  <code>{codeString}</code>
                </pre>
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

export const MarkdownRenderer = memo(MarkdownRendererImpl);
