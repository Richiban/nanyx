import { visit } from "unist-util-visit";

/**
 * The markdown files use `#` for their top-level sections, but the page
 * title is already rendered as the h1. Shift every heading down one level
 * so the document outline stays valid.
 */
export function rehypeShiftHeadings() {
  return (tree) => {
    visit(tree, "element", (node) => {
      const match = /^h([1-5])$/.exec(node.tagName);
      if (match) {
        node.tagName = `h${Number(match[1]) + 1}`;
      }
    });
  };
}

const linkIcon = {
  type: "element",
  tagName: "svg",
  properties: {
    xmlns: "http://www.w3.org/2000/svg",
    width: 16,
    height: 16,
    viewBox: "0 0 24 24",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: 2,
    strokeLinecap: "round",
    strokeLinejoin: "round",
    ariaHidden: "true",
  },
  children: [
    { type: "element", tagName: "path", properties: { d: "M9 17H7A5 5 0 0 1 7 7h2" }, children: [] },
    { type: "element", tagName: "path", properties: { d: "M15 7h2a5 5 0 1 1 0 10h-2" }, children: [] },
    { type: "element", tagName: "line", properties: { x1: 8, x2: 16, y1: 12, y2: 12 }, children: [] },
  ],
};

/**
 * Append a hover-revealed anchor link to each heading that has an id.
 * Must run after Astro's heading id plugin.
 */
export function rehypeHeadingAnchors() {
  return (tree) => {
    visit(tree, "element", (node) => {
      if (!/^h[2-6]$/.test(node.tagName) || !node.properties?.id) return;
      const id = node.properties.id;
      node.properties.className = [...(node.properties.className ?? []), "heading", "group"];
      node.children = [
        { type: "element", tagName: "span", properties: {}, children: node.children },
        {
          type: "element",
          tagName: "a",
          properties: {
            href: `#${id}`,
            className: ["heading-anchor"],
            ariaLabel: "Copy link to this section",
            dataHeadingAnchor: "",
          },
          children: [linkIcon],
        },
      ];
    });
  };
}
