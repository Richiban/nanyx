import * as vscode from "vscode";
import { LanguageClient, LanguageClientOptions, ServerOptions } from "vscode-languageclient/node";
import { downloadCompiler, locateCompiler, readSettings } from "./compiler";

const COMMAND_DOWNLOAD = "nanyx.downloadCompiler";
const COMMAND_RESTART = "nanyx.restartServer";

let client: LanguageClient | undefined;
let outputChannel: vscode.OutputChannel;
let declinedDownload = false;

export async function activate(context: vscode.ExtensionContext) {
  outputChannel = vscode.window.createOutputChannel("Nanyx");
  context.subscriptions.push(outputChannel);

  context.subscriptions.push(
    vscode.commands.registerCommand(COMMAND_DOWNLOAD, async () => {
      declinedDownload = false;
      const executable = await promptAndDownload(context);
      if (executable) {
        await restartClient(context, executable);
      }
    }),
    vscode.commands.registerCommand(COMMAND_RESTART, () => startOrRestart(context)),
    vscode.workspace.onDidChangeConfiguration((event) => {
      if (
        event.affectsConfiguration("nanyx.path") ||
        event.affectsConfiguration("nanyx.compilerVersion") ||
        event.affectsConfiguration("nanyx.downloadUrl") ||
        event.affectsConfiguration("nanyx.greekTypeVariables")
      ) {
        void startOrRestart(context);
      }
    })
  );

  await startOrRestart(context);
}

export async function deactivate() {
  await stopClient();
}

/** Starts the language server with whatever `nanyx` can be found, downloading one if the user agrees. */
async function startOrRestart(context: vscode.ExtensionContext): Promise<void> {
  let executable: string | undefined;
  try {
    executable = locateCompiler(context, readSettings());
  } catch (error) {
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
async function promptAndDownload(context: vscode.ExtensionContext): Promise<string | undefined> {
  if (declinedDownload) {
    return undefined;
  }

  const settings = readSettings();
  const download = "Download";
  const choice = await vscode.window.showInformationMessage(
    `The Nanyx compiler was not found on PATH. Download nanyx ${settings.version} for this platform? ` +
      "(It is kept in the extension's storage; set nanyx.path to use your own build instead.)",
    download,
    "Not now"
  );

  if (choice !== download) {
    declinedDownload = true;
    return undefined;
  }

  try {
    return await vscode.window.withProgress(
      { location: vscode.ProgressLocation.Notification, title: `Downloading nanyx ${settings.version}`, cancellable: true },
      (progress, token) => downloadCompiler(context, settings, progress, token)
    );
  } catch (error) {
    outputChannel.appendLine(`Download failed: ${messageOf(error)}`);
    void vscode.window.showErrorMessage(`Nanyx: ${messageOf(error)}`);
    return undefined;
  }
}

async function restartClient(context: vscode.ExtensionContext, executable: string): Promise<void> {
  await stopClient();

  // The switch is passed only when it is on, so turning nothing off still works with compilers that predate it.
  const greek = vscode.workspace.getConfiguration("nanyx").get<boolean>("greekTypeVariables", true);
  const args = greek ? ["lsp"] : ["lsp", "--latin-type-vars"];

  // No `transport`: stdio is the default, and naming it makes the client append `--stdio` to the args.
  const serverOptions: ServerOptions = {
    run: { command: executable, args },
    debug: { command: executable, args }
  };

  const clientOptions: LanguageClientOptions = {
    documentSelector: [{ language: "nanyx" }],
    outputChannel
  };

  outputChannel.appendLine(`Starting the Nanyx language server: ${executable} ${args.join(" ")}`);
  client = new LanguageClient("nanyx", "Nanyx Language Server", serverOptions, clientOptions);

  try {
    await client.start();
  } catch (error) {
    void vscode.window.showErrorMessage(`Nanyx: the language server failed to start (${messageOf(error)}). See the Nanyx output channel.`);
    client = undefined;
  }
}

async function stopClient(): Promise<void> {
  if (client) {
    const stopping = client;
    client = undefined;
    await stopping.stop().catch(() => undefined);
    await stopping.dispose();
  }
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
