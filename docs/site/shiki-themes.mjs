// Shiki themes for the docs. These mirror the light and dark palettes used
// across the site and target the scopes in the Nanyx TextMate grammar.

const punctuationScopes = [
  "punctuation.section",
  "punctuation.section.brackets",
  "punctuation.section.parens",
  "punctuation.section.block",
  "punctuation.definition.group",
  "keyword.symbol.nanyx",
  "keyword.symbol.arrow.nanyx",
];

export const paperWhite = {
  name: "paper-white",
  type: "light",
  colors: {
    "editor.background": "#f7f5f2",
    "editor.foreground": "#2b2b2b",
  },
  settings: [
    { settings: { foreground: "#2b2b2b" } },
    { scope: ["comment", "punctuation.definition.comment"], settings: { foreground: "#7a7a7a" } },
    { scope: ["string", "punctuation.definition.string"], settings: { foreground: "#c94c12" } },
    { scope: ["constant.numeric", "constant.language"], settings: { foreground: "#098658" } },
    { scope: ["keyword.control.nanyx"], settings: { foreground: "#c244b1" } },
    { scope: ["keyword.control"], settings: { foreground: "#c244b1" } },
    { scope: ["keyword.declaration.nanyx"], settings: { foreground: "#2b579a" } },
    { scope: ["keyword.nanyx"], settings: { foreground: "#2b579a" } },
    { scope: ["entity.name.function", "support.function"], settings: { foreground: "#795e26" } },
    { scope: ["entity.name.type", "support.type"], settings: { foreground: "#23a87c" } },
    { scope: ["variable", "variable.parameter"], settings: { foreground: "#001080" } },
    { scope: ["entity.name.tag", "support.class"], settings: { foreground: "#448eca" } },
    { scope: ["tag.nanyx", "support.type.nanyx"], settings: { foreground: "#1b8a5a" } },
    { scope: punctuationScopes, settings: { foreground: "#4a4a4a" } },
    { scope: ["keyword.operator"], settings: { foreground: "#000000" } },
  ],
};

export const inkNight = {
  name: "ink-night",
  type: "dark",
  colors: {
    "editor.background": "#111214",
    "editor.foreground": "#e6e6e6",
  },
  settings: [
    { settings: { foreground: "#e6e6e6" } },
    { scope: ["comment", "punctuation.definition.comment"], settings: { foreground: "#8b8f98" } },
    { scope: ["string", "punctuation.definition.string"], settings: { foreground: "#f28f6b" } },
    { scope: ["constant.numeric", "constant.language"], settings: { foreground: "#55c99b" } },
    { scope: ["keyword.control.nanyx"], settings: { foreground: "#f07ad8" } },
    { scope: ["keyword.control"], settings: { foreground: "#f07ad8" } },
    { scope: ["keyword.declaration.nanyx"], settings: { foreground: "#82b1ff" } },
    { scope: ["keyword.nanyx"], settings: { foreground: "#82b1ff" } },
    { scope: ["entity.name.function", "support.function"], settings: { foreground: "#f4bf75" } },
    { scope: ["entity.name.type", "support.type"], settings: { foreground: "#6ee7b7" } },
    { scope: ["entity.name.tag", "tag.nanyx"], settings: { foreground: "#7dfce7" } },
    { scope: ["punctuation.definition.tag.nanyx"], settings: { foreground: "#85beb4" } },
    { scope: ["support.type.nanyx"], settings: { foreground: "#34d3ab" } },
    { scope: punctuationScopes, settings: { foreground: "#999797" } },
    { scope: ["keyword.operator"], settings: { foreground: "#999797" } },
  ],
};
