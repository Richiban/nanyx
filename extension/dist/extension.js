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
exports.activate = activate;
exports.deactivate = deactivate;
const vscode = __importStar(require("vscode"));
const node_1 = require("vscode-languageclient/node");
const compiler_1 = require("./compiler");
const COMMAND_DOWNLOAD = "nanyx.downloadCompiler";
const COMMAND_RESTART = "nanyx.restartServer";
let client;
let outputChannel;
let declinedDownload = false;
async function activate(context) {
    outputChannel = vscode.window.createOutputChannel("Nanyx");
    context.subscriptions.push(outputChannel);
    context.subscriptions.push(vscode.commands.registerCommand(COMMAND_DOWNLOAD, async () => {
        declinedDownload = false;
        const executable = await promptAndDownload(context);
        if (executable) {
            await restartClient(context, executable);
        }
    }), vscode.commands.registerCommand(COMMAND_RESTART, () => startOrRestart(context)), vscode.workspace.onDidChangeConfiguration((event) => {
        if (event.affectsConfiguration("nanyx.path") || event.affectsConfiguration("nanyx.compilerVersion") || event.affectsConfiguration("nanyx.downloadUrl")) {
            void startOrRestart(context);
        }
    }));
    await startOrRestart(context);
}
async function deactivate() {
    await stopClient();
}
/** Starts the language server with whatever `nanyx` can be found, downloading one if the user agrees. */
async function startOrRestart(context) {
    let executable;
    try {
        executable = (0, compiler_1.locateCompiler)(context, (0, compiler_1.readSettings)());
    }
    catch (error) {
        void vscode.window.showErrorMessage(`Nanyx: ${messageOf(error)}`);
        return;
    }
    if (!executable) {
        executable = await promptAndDownload(context);
    }
    if (executable) {
        await restartClient(context, executable);
    }
}
/** Offers to download the compiler once per session; returns the executable if the user agreed and it worked. */
async function promptAndDownload(context) {
    if (declinedDownload) {
        return undefined;
    }
    const settings = (0, compiler_1.readSettings)();
    const download = "Download";
    const choice = await vscode.window.showInformationMessage(`The Nanyx compiler was not found on PATH. Download nanyx ${settings.version} for this platform? ` +
        "(It is kept in the extension's storage; set nanyx.path to use your own build instead.)", download, "Not now");
    if (choice !== download) {
        declinedDownload = true;
        return undefined;
    }
    try {
        return await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: `Downloading nanyx ${settings.version}`, cancellable: true }, (progress, token) => (0, compiler_1.downloadCompiler)(context, settings, progress, token));
    }
    catch (error) {
        outputChannel.appendLine(`Download failed: ${messageOf(error)}`);
        void vscode.window.showErrorMessage(`Nanyx: ${messageOf(error)}`);
        return undefined;
    }
}
async function restartClient(context, executable) {
    await stopClient();
    const serverOptions = {
        run: { command: executable, args: ["lsp"], transport: node_1.TransportKind.stdio },
        debug: { command: executable, args: ["lsp"], transport: node_1.TransportKind.stdio }
    };
    const clientOptions = {
        documentSelector: [{ language: "nanyx" }],
        outputChannel
    };
    outputChannel.appendLine(`Starting the Nanyx language server: ${executable} lsp`);
    client = new node_1.LanguageClient("nanyx", "Nanyx Language Server", serverOptions, clientOptions);
    try {
        await client.start();
    }
    catch (error) {
        void vscode.window.showErrorMessage(`Nanyx: the language server failed to start (${messageOf(error)}). See the Nanyx output channel.`);
        client = undefined;
    }
}
async function stopClient() {
    if (client) {
        const stopping = client;
        client = undefined;
        await stopping.stop().catch(() => undefined);
        await stopping.dispose();
    }
}
function messageOf(error) {
    return error instanceof Error ? error.message : String(error);
}
//# sourceMappingURL=extension.js.map