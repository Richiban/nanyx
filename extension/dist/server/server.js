"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
const child_process_1 = require("child_process");
const path = __importStar(require("path"));
const url_1 = require("url");
const node_1 = require("vscode-languageserver/node");
const vscode_languageserver_textdocument_1 = require("vscode-languageserver-textdocument");
const defaultSettings = {
    typecheckOnSave: true,
    compilerProject: "compiler/Nanyx.Compiler.Cli/Nanyx.Compiler.Cli.fsproj",
    compilerDotnetPath: "dotnet"
};
const connection = (0, node_1.createConnection)(node_1.ProposedFeatures.all);
const documents = new node_1.TextDocuments(vscode_languageserver_textdocument_1.TextDocument);
let workspaceFolders = [];
let hasConfigurationCapability = false;
connection.onInitialize((params) => {
    const capabilities = params.capabilities;
    hasConfigurationCapability = !!(capabilities.workspace &&
        capabilities.workspace.configuration);
    workspaceFolders =
        params.workspaceFolders?.map((folder) => (0, url_1.fileURLToPath)(folder.uri)) ?? [];
    const result = {
        capabilities: {
            textDocumentSync: node_1.TextDocumentSyncKind.Incremental
        }
    };
    return result;
});
connection.onInitialized(() => {
    if (hasConfigurationCapability) {
        connection.client.register(node_1.DidChangeConfigurationNotification.type, undefined);
    }
});
connection.onDidChangeConfiguration(() => {
    // no-op; settings fetched on demand
});
connection.onNotification("nanyx/typecheck", async (params) => {
    const document = documents.get(params.uri);
    if (document) {
        await runDiagnostics(document, true);
    }
});
connection.onDidSaveTextDocument(async (params) => {
    const document = documents.get(params.textDocument.uri);
    if (document) {
        await runDiagnostics(document, true);
    }
});
connection.onDidOpenTextDocument(async (params) => {
    const document = documents.get(params.textDocument.uri);
    if (document) {
        await runDiagnostics(document, false);
    }
});
connection.onDidChangeTextDocument(async (params) => {
    const document = documents.get(params.textDocument.uri);
    if (document) {
        await runDiagnostics(document, false);
    }
});
documents.onDidClose((event) => {
    connection.sendDiagnostics({ uri: event.document.uri, diagnostics: [] });
});
documents.listen(connection);
connection.listen();
async function runDiagnostics(document, requireSaved) {
    // Typechecking is disabled: do not spawn dotnet or run any diagnostics.
    connection.sendDiagnostics({ uri: document.uri, diagnostics: [] });
}
async function getSettings(_uri) {
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
function resolveWorkspaceRoot(filePath) {
    const normalized = path.normalize(filePath).toLowerCase();
    for (const folder of workspaceFolders) {
        const normalizedFolder = path.normalize(folder).toLowerCase();
        if (normalized.startsWith(normalizedFolder)) {
            return folder;
        }
    }
    return workspaceFolders[0] ?? null;
}
function resolveProjectPath(workspaceRoot, projectSetting) {
    if (!projectSetting) {
        return path.join(workspaceRoot, defaultSettings.compilerProject);
    }
    if (path.isAbsolute(projectSetting)) {
        return projectSetting;
    }
    return path.join(workspaceRoot, projectSetting);
}
function fileUrlToPath(uri) {
    if (!uri.startsWith("file://")) {
        return null;
    }
    try {
        return (0, url_1.fileURLToPath)(uri);
    }
    catch {
        return null;
    }
}
function runCompiler(dotnetPath, projectPath, filePath) {
    const args = ["run", "--project", projectPath, "--", filePath];
    return new Promise((resolve, reject) => {
        const child = (0, child_process_1.spawn)(dotnetPath, args, { cwd: path.dirname(projectPath) });
        let stdout = "";
        let stderr = "";
        child.stdout.on("data", (data) => {
            stdout += data.toString();
        });
        child.stderr.on("data", (data) => {
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
function parseDiagnostics(output) {
    if (!output) {
        return [];
    }
    const diagnostics = [];
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
        const severity = severityText === "warning" ? node_1.DiagnosticSeverity.Warning : node_1.DiagnosticSeverity.Error;
        const lineNumber = lineText ? Math.max(0, Number(lineText) - 1) : 0;
        const colNumber = colText ? Math.max(0, Number(colText) - 1) : 0;
        diagnostics.push({
            severity,
            message,
            range: node_1.Range.create(lineNumber, colNumber, lineNumber, colNumber + 1)
        });
    }
    return diagnostics;
}
//# sourceMappingURL=server.js.map