import fs from "node:fs/promises";
import path from "node:path";
import { defineCollection } from "astro:content";
import { glob, type Loader, type LoaderContext } from "astro/loaders";
import { z } from "astro/zod";
import {
  EXAMPLES_DIR,
  EXAMPLES_OVERVIEW_SLUG,
  EXAMPLES_SOURCE_URL,
  allExamples,
  exampleGroups,
} from "../site/examples.mjs";

const EXAMPLES_SECTION = "examples";

const markdownPages = glob({ pattern: "**/*.md", base: "./src/content" });

/**
 * The docs collection: every markdown page under `src/content`, plus the
 * Examples section, whose pages are generated from the repository's
 * `examples/*.nyx` files (listed and described in `site/examples.mjs`).
 */
const docsLoader: Loader = {
  name: "docs-loader",
  async load(context) {
    await markdownPages.load(context);
    await loadExamples(context);

    // In dev, regenerate the example pages when a source file changes.
    if (context.watcher) {
      context.watcher.add(EXAMPLES_DIR);
      const onChange = (changed: string) => {
        if (path.resolve(changed).startsWith(EXAMPLES_DIR)) {
          loadExamples(context).catch((error) => context.logger.error(String(error)));
        }
      };
      context.watcher.on("change", onChange);
      context.watcher.on("add", onChange);
      context.watcher.on("unlink", onChange);
    }
  },
};

async function loadExamples({ store, renderMarkdown, logger }: LoaderContext) {
  const examples = allExamples();

  const overviewBody = overviewMarkdown();
  const overviewId = `${EXAMPLES_SECTION}/${EXAMPLES_OVERVIEW_SLUG}`;
  store.set({
    id: overviewId,
    data: {
      title: "All examples",
      description: "Complete Nanyx programs from the repository, grouped by theme.",
      order: 0,
    },
    body: overviewBody,
    rendered: await renderMarkdown(overviewBody),
  });

  for (const example of examples) {
    const filePath = path.join(EXAMPLES_DIR, example.file);
    const source = (await fs.readFile(filePath, "utf8")).replace(/\r\n/g, "\n").trimEnd();
    const body = exampleMarkdown(example.file, source);
    store.set({
      id: `${EXAMPLES_SECTION}/${example.slug}`,
      data: { title: example.title, description: example.description, order: example.order },
      body,
      rendered: await renderMarkdown(body),
    });
  }

  logger.info(`Generated ${examples.length} example pages`);
}

function exampleMarkdown(file: string, source: string): string {
  return [
    "```nanyx",
    source,
    "```",
    "",
    `This program lives in the repository as [examples/${file}](${EXAMPLES_SOURCE_URL}/${file}).`,
    "",
  ].join("\n");
}

function overviewMarkdown(): string {
  const lines = [
    "Each page in this section is a complete program from the `examples` directory of the",
    "[Nanyx repository](https://github.com/nanyx-lang/nanyx/tree/main/examples), shown as it is",
    "there. They are meant to be read for the shape of idiomatic Nanyx, or copied as a starting",
    "point. For step-by-step walkthroughs with a goal in mind, see the Cookbook instead.",
    "",
  ];
  for (const group of exampleGroups) {
    lines.push(`# ${group.title}`, "", group.description, "");
    for (const example of group.examples) {
      lines.push(`- [${example.title}](./${example.slug}) — ${example.description}`);
    }
    lines.push("");
  }
  return lines.join("\n");
}

const docs = defineCollection({
  loader: docsLoader,
  schema: z.object({
    title: z.string(),
    description: z.string().optional(),
    order: z.number().default(99),
  }),
});

export const collections = { docs };
