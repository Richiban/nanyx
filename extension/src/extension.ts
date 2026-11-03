import * as path from "path";
import * as vscode from "vscode";
import { LanguageClient, LanguageClientOptions, ServerOptions, TransportKind } from "vscode-languageclient/node";

const COMMAND_TYPECHECK = "nanyx.typecheckFile";

let client: LanguageClient | undefined;

export async function activate(context: vscode.ExtensionContext) {
  const serverModule = context.asAbsolutePath(path.join("dist", "server", "server.js"));

  const serverOptions: ServerOptions = {
    run: { module: serverModule, transport: TransportKind.stdio },
    debug: {
      module: serverModule,
      transport: TransportKind.stdio,
      options: { execArgv: ["--nolazy", "--inspect=6009"] }
    }
  };

  const clientOptions: LanguageClientOptions = {
    documentSelector: [{ scheme: "file", language: "nanyx" }],
    synchronize: {
      configurationSection: "nanyx"
    }
  };

  client = new LanguageClient("nanyx", "Nanyx Language Server", serverOptions, clientOptions);
  context.subscriptions.push(client);
  await client.start();

  const typecheckCommand = vscode.commands.registerCommand(COMMAND_TYPECHECK, async () => {
    const editor = vscode.window.activeTextEditor;
    if (!editor) {
      vscode.window.showInformationMessage("No active editor to typecheck.");
      return;
    }

    await client?.sendNotification("nanyx/typecheck", { uri: editor.document.uri.toString() });
  });

  context.subscriptions.push(typecheckCommand);
}

export async function deactivate() {
  if (client) {
    await client.stop();
  }
}
