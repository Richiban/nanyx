import { spawn } from "child_process";
import * as fs from "fs";
import * as path from "path";
import { fileURLToPath } from "url";
import {
  createConnection,
  ProposedFeatures,
  TextDocumentSyncKind,
  InitializeParams,
  InitializeResult,
  DidChangeConfigurationNotification,
  TextDocuments,
  Diagnostic,
  DiagnosticSeverity,
  Range,
  WorkspaceFolder,
  DidSaveTextDocumentParams,
  DidOpenTextDocumentParams,
  DidChangeTextDocumentParams
} from "vscode-languageserver/node";
import { TextDocument } from "vscode-languageserver-textdocument";

interface NanyxSettings {
  typecheckOnSave: boolean;
  compilerProject: string;
  compilerDotnetPath: string;
}

const defaultSettings: NanyxSettings = {
  typecheckOnSave: true,
  compilerProject: "compiler/Nanyx.Compiler.Cli/Nanyx.Compiler.Cli.fsproj",
  compilerDotnetPath: "dotnet"
};

const connection = createConnection(ProposedFeatures.all);
const documents = new TextDocuments(TextDocument);

let workspaceFolders: string[] = [];
let hasConfigurationCapability = false;

connection.onInitialize((params: InitializeParams): InitializeResult => {
  const capabilities = params.capabilities;
  hasConfigurationCapability = !!(
    capabilities.workspace &&
    capabilities.workspace.configuration
  );

  workspaceFolders =
    params.workspaceFolders?.map((folder: WorkspaceFolder) => fileURLToPath(folder.uri)) ?? [];

  const result: InitializeResult = {
    capabilities: {
      textDocumentSync: TextDocumentSyncKind.Incremental
    }
  };

  return result;
});

connection.onInitialized(() => {
  if (hasConfigurationCapability) {
    connection.client.register(DidChangeConfigurationNotification.type, undefined);
  }
});

connection.onDidChangeConfiguration(() => {
  // no-op; settings fetched on demand
});

connection.onNotification("nanyx/typecheck", async (params: { uri: string }) => {
  const document = documents.get(params.uri);
  if (document) {
    await runDiagnostics(document, true);
  }
});

connection.onDidSaveTextDocument(async (params: DidSaveTextDocumentParams) => {
  const document = documents.get(params.textDocument.uri);
  if (document) {
    await runDiagnostics(document, true);
  }
});

connection.onDidOpenTextDocument(async (params: DidOpenTextDocumentParams) => {
  const document = documents.get(params.textDocument.uri);
  if (document) {
    await runDiagnostics(document, false);
  }
});

connection.onDidChangeTextDocument(async (params: DidChangeTextDocumentParams) => {
  const document = documents.get(params.textDocument.uri);
  if (document) {
    await runDiagnostics(document, false);
  }
});

documents.onDidClose((event: { document: TextDocument }) => {
  connection.sendDiagnostics({ uri: event.document.uri, diagnostics: [] });
});

documents.listen(connection);
connection.listen();

async function runDiagnostics(document: TextDocument, requireSaved: boolean) {
  // Typechecking is disabled: do not spawn dotnet or run any diagnostics.
  connection.sendDiagnostics({ uri: document.uri, diagnostics: [] });
}

async function getSettings(_uri: string): Promise<NanyxSettings> {
  if (!hasConfigurationCapability) {
    return defaultSettings;
  }

  const settings = await connection.workspace.getConfiguration({ section: "nanyx" });
  return {
    typecheckOnSave: settings.typecheckOnSave ?? defaultSettings.typecheckOnSave,
    compilerProject: settings.compilerProject ?? defaultSettings.compilerProject,
    compilerDotnetPath: settings.compilerDotnetPath ?? defaultSettings.compilerDotnetPath
  };
}

function resolveWorkspaceRoot(filePath: string): string | null {
  const normalized = path.normalize(filePath).toLowerCase();
  for (const folder of workspaceFolders) {
    const normalizedFolder = path.normalize(folder).toLowerCase();
    if (normalized.startsWith(normalizedFolder)) {
      return folder;
    }
  }
  return workspaceFolders[0] ?? null;
}

function resolveProjectPath(workspaceRoot: string, projectSetting: string): string {
  if (!projectSetting) {
    return path.join(workspaceRoot, defaultSettings.compilerProject);
  }

  if (path.isAbsolute(projectSetting)) {
    return projectSetting;
  }

  return path.join(workspaceRoot, projectSetting);
}

function fileUrlToPath(uri: string): string | null {
  if (!uri.startsWith("file://")) {
    return null;
  }

  try {
    return fileURLToPath(uri);
  } catch {
    return null;
  }
}

function runCompiler(dotnetPath: string, projectPath: string, filePath: string): Promise<string> {
  const args = ["run", "--project", projectPath, "--", filePath];

  return new Promise((resolve, reject) => {
    const child = spawn(dotnetPath, args, { cwd: path.dirname(projectPath) });

    let stdout = "";
    let stderr = "";

    child.stdout.on("data", (data: Uint8Array) => {
      stdout += data.toString();
    });

    child.stderr.on("data", (data: Uint8Array) => {
      stderr += data.toString();
    });

    child.on("error", (error) => {
      reject(error);
    });

    child.on("close", (code) => {
      const combined = [stdout.trim(), stderr.trim()].filter(Boolean).join("\n");
      if (code !== 0 && !combined) {
        reject(new Error(`Compiler exited with code ${code}`));
        return;
      }

      resolve(combined || stdout);
    });
  });
}

function parseDiagnostics(output: string): Diagnostic[] {
  if (!output) {
    return [];
  }

  const diagnostics: Diagnostic[] = [];
  const lines = output.split(/\r?\n/);

  for (const line of lines) {
    const match = line.match(/^\[(error|warning)\]\s*(?:\((\d+):(\d+)\)\s*)?(.+)$/i);
    if (!match) {
      continue;
    }

    const severityText = match[1].toLowerCase();
    const lineText = match[2];
    const colText = match[3];
    const message = match[4].trim();
    const severity = severityText === "warning" ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error;

    const lineNumber = lineText ? Math.max(0, Number(lineText) - 1) : 0;
    const colNumber = colText ? Math.max(0, Number(colText) - 1) : 0;

    diagnostics.push({
      severity,
      message,
      range: Range.create(lineNumber, colNumber, lineNumber, colNumber + 1)
    });
  }

  return diagnostics;
}
