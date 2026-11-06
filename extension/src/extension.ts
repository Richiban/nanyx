import * as vscode from "vscode";
import { LanguageClient, LanguageClientOptions, ServerOptions } from "vscode-languageclient/node";
import {
  CompilerSettings,
  compareVersions,
  downloadCompiler,
  locateCompiler,
  newestRelease,
  readSettings,
  removeDownloadedExcept,
  tracksNewestRelease,
  versionToDownload
} from "./compiler";

const COMMAND_DOWNLOAD = "nanyx.downloadCompiler";
const COMMAND_RESTART = "nanyx.restartServer";

/** When the extension last asked GitHub for a newer compiler release (milliseconds since the epoch). */
const LAST_UPDATE_CHECK = "nanyx.lastUpdateCheck";
const UPDATE_CHECK_INTERVAL = 24 * 60 * 60 * 1000;

let client: LanguageClient | undefined;
let outputChannel: vscode.OutputChannel;
let declinedDownload = false;
let declinedUpdate = false;

export async function activate(context: vscode.ExtensionContext) {
  outputChannel = vscode.window.createOutputChannel("Nanyx");
  context.subscriptions.push(outputChannel);

  context.subscriptions.push(
    vscode.commands.registerCommand(COMMAND_DOWNLOAD, async () => {
      declinedDownload = false;
      const settings = readSettings();
      const version = await versionToDownload(context, settings, log);
      const executable = await downloadAndRecordCheck(context, settings, version);
      if (executable) {
        await restartClient(context, executable);
        if (tracksNewestRelease(settings)) {
          removeDownloadedExcept(context, version);
        }
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

/**
 * Starts the language server with whatever `nanyx` can be found, downloading one if the user agrees.
 * When it is a downloaded copy, a newer compiler release is looked for afterwards, never before.
 */
async function startOrRestart(context: vscode.ExtensionContext): Promise<void> {
  const settings = readSettings();
  let located;
  try {
    located = locateCompiler(context, settings);
  } catch (error) {
    void vscode.window.showErrorMessage(`Nanyx: ${messageOf(error)}`);
    return;
  }

  const executable = located?.executable ?? (await promptAndDownload(context, settings));
  if (executable) {
    await restartClient(context, executable);
  }

  if (located?.downloadedVersion && tracksNewestRelease(settings)) {
    void checkForUpdate(context, settings, located.downloadedVersion);
  }
}

/** Offers to download the compiler once per session; returns the executable if the user agreed and it worked. */
async function promptAndDownload(context: vscode.ExtensionContext, settings: CompilerSettings): Promise<string | undefined> {
  if (declinedDownload) {
    return undefined;
  }

  const version = await versionToDownload(context, settings, log);
  const download = "Download";
  const choice = await vscode.window.showInformationMessage(
    `The Nanyx compiler was not found on PATH. Download nanyx ${version} for this platform? ` +
      "(It is kept in the extension's storage; set nanyx.path to use your own build instead.)",
    download,
    "Not now"
  );

  if (choice !== download) {
    declinedDownload = true;
    return undefined;
  }

  return downloadAndRecordCheck(context, settings, version);
}

async function downloadAndRecordCheck(context: vscode.ExtensionContext, settings: CompilerSettings, version: string): Promise<string | undefined> {
  const executable = await download(context, settings, version);
  if (executable && tracksNewestRelease(settings)) {
    // The version was just chosen from GitHub's list, so there is nothing newer to look for today.
    await context.globalState.update(LAST_UPDATE_CHECK, Date.now());
  }

  return executable;
}

/**
 * Looks for a compiler release newer than the downloaded copy in use, at most once a day, and offers
 * to download it and restart the server. Being offline or rate limited just means no offer.
 */
async function checkForUpdate(context: vscode.ExtensionContext, settings: CompilerSettings, installed: string): Promise<void> {
  if (declinedUpdate || Date.now() - context.globalState.get<number>(LAST_UPDATE_CHECK, 0) < UPDATE_CHECK_INTERVAL) {
    return;
  }

  await context.globalState.update(LAST_UPDATE_CHECK, Date.now());

  let newest: string | undefined;
  try {
    newest = await newestRelease();
  } catch (error) {
    log(`Could not check for a newer compiler release: ${messageOf(error)}`);
    return;
  }

  if (!newest || compareVersions(newest, installed) <= 0) {
    return;
  }

  const update = "Update";
  const choice = await vscode.window.showInformationMessage(
    `Nanyx compiler ${newest} is available (this is ${installed}). Download it and restart the language server?`,
    update,
    "Not now"
  );

  if (choice !== update) {
    declinedUpdate = true;
    return;
  }

  const executable = await download(context, settings, newest);
  if (executable) {
    await restartClient(context, executable);
    removeDownloadedExcept(context, newest);
  }
}

/** Downloads a compiler release with a progress notification; returns the executable, or `undefined` after reporting a failure. */
async function download(context: vscode.ExtensionContext, settings: CompilerSettings, version: string): Promise<string | undefined> {
  try {
    return await vscode.window.withProgress(
      { location: vscode.ProgressLocation.Notification, title: `Downloading nanyx ${version}`, cancellable: true },
      (progress, token) => downloadCompiler(context, settings, version, progress, token)
    );
  } catch (error) {
    log(`Download failed: ${messageOf(error)}`);
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

  log(`Starting the Nanyx language server: ${executable} ${args.join(" ")}`);
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

function log(line: string): void {
  outputChannel.appendLine(line);
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
