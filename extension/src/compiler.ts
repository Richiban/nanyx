import { spawnSync } from "child_process";
import * as fs from "fs";
import * as https from "https";
import * as os from "os";
import * as path from "path";
import * as vscode from "vscode";

/**
 * The compiler release the extension downloads when it finds no `nanyx` of its own. Bump it with each
 * compiler release the extension should use (the `<Version>` in NanyxCompiler.Cli.csproj at the time).
 */
export const DEFAULT_COMPILER_VERSION = "0.4.1";

/**
 * Where a release lives. `${version}`, `${rid}` and `${ext}` are substituted; this is what the
 * `nanyx.downloadUrl` setting overrides.
 */
export const DEFAULT_DOWNLOAD_URL =
  "https://github.com/nanyx-lang/nanyx/releases/download/compiler/${version}/nanyx-${version}-${rid}.${ext}";

const EXECUTABLE = process.platform === "win32" ? "nanyx.exe" : "nanyx";

export interface CompilerSettings {
  /** `nanyx.path`: an explicit executable, or empty to search PATH and the downloaded copy. */
  path: string;
  /** `nanyx.compilerVersion`: the release to download, or empty for the extension's default. */
  version: string;
  /** `nanyx.downloadUrl`: the URL template to download from. */
  downloadUrl: string;
}

export function readSettings(): CompilerSettings {
  const config = vscode.workspace.getConfiguration("nanyx");
  return {
    path: config.get<string>("path", "").trim(),
    version: config.get<string>("compilerVersion", "").trim() || DEFAULT_COMPILER_VERSION,
    downloadUrl: config.get<string>("downloadUrl", "").trim() || DEFAULT_DOWNLOAD_URL
  };
}

/**
 * Finds the `nanyx` executable to run the language server with, in this order: the `nanyx.path`
 * setting, PATH, then a copy this extension downloaded earlier. Returns `undefined` when none exists.
 */
export function locateCompiler(context: vscode.ExtensionContext, settings: CompilerSettings): string | undefined {
  if (settings.path) {
    if (fs.existsSync(settings.path)) {
      return settings.path;
    }

    throw new Error(`nanyx.path is set to "${settings.path}", but no file exists there.`);
  }

  return findOnPath() ?? findDownloaded(context, settings);
}

/** The platform's runtime identifier as the compiler release names it, or `undefined` when there is no build for it. */
export function runtimeIdentifier(): string | undefined {
  const arch = process.arch;
  switch (process.platform) {
    case "win32":
      return arch === "x64" ? "win-x64" : undefined;
    case "linux":
      return arch === "x64" ? "linux-x64" : arch === "arm64" ? "linux-arm64" : undefined;
    case "darwin":
      return arch === "x64" ? "osx-x64" : arch === "arm64" ? "osx-arm64" : undefined;
    default:
      return undefined;
  }
}

export function downloadUrl(settings: CompilerSettings, rid: string): string {
  const ext = rid.startsWith("win-") ? "zip" : "tar.gz";
  return settings.downloadUrl
    .replace(/\$\{version\}/g, settings.version)
    .replace(/\$\{rid\}/g, rid)
    .replace(/\$\{ext\}/g, ext);
}

/**
 * Downloads the compiler release for this platform into the extension's global storage and returns
 * the path of the executable. Throws with a readable message when offline or when the release is missing.
 */
export async function downloadCompiler(
  context: vscode.ExtensionContext,
  settings: CompilerSettings,
  progress: vscode.Progress<{ message?: string; increment?: number }>,
  token: vscode.CancellationToken
): Promise<string> {
  const rid = runtimeIdentifier();
  if (!rid) {
    throw new Error(
      `There is no Nanyx compiler build for ${process.platform}/${process.arch}. ` +
        "Build it from source and point the nanyx.path setting at it."
    );
  }

  const url = downloadUrl(settings, rid);
  const targetDir = installDir(context, settings, rid);
  const archive = path.join(os.tmpdir(), `nanyx-${settings.version}-${rid}-${process.pid}.${url.endsWith(".zip") ? "zip" : "tar.gz"}`);

  progress.report({ message: `downloading ${url}` });
  await fetchToFile(url, archive, token);

  try {
    progress.report({ message: "unpacking" });
    const staging = `${targetDir}.partial`;
    fs.rmSync(staging, { recursive: true, force: true });
    fs.mkdirSync(staging, { recursive: true });
    extract(archive, staging);

    const executable = path.join(staging, EXECUTABLE);
    if (!fs.existsSync(executable)) {
      throw new Error(`The archive from ${url} does not contain ${EXECUTABLE}.`);
    }

    if (process.platform !== "win32") {
      fs.chmodSync(executable, 0o755);
    }

    fs.rmSync(targetDir, { recursive: true, force: true });
    fs.renameSync(staging, targetDir);
    return path.join(targetDir, EXECUTABLE);
  } finally {
    fs.rmSync(archive, { force: true });
  }
}

function installDir(context: vscode.ExtensionContext, settings: CompilerSettings, rid: string): string {
  return path.join(context.globalStorageUri.fsPath, `nanyx-${settings.version}-${rid}`);
}

function findDownloaded(context: vscode.ExtensionContext, settings: CompilerSettings): string | undefined {
  const rid = runtimeIdentifier();
  if (!rid) {
    return undefined;
  }

  const executable = path.join(installDir(context, settings, rid), EXECUTABLE);
  return fs.existsSync(executable) ? executable : undefined;
}

function findOnPath(): string | undefined {
  const directories = (process.env.PATH ?? "").split(path.delimiter).filter(Boolean);
  for (const directory of directories) {
    const candidate = path.join(directory, EXECUTABLE);
    if (fs.existsSync(candidate)) {
      return candidate;
    }
  }

  return undefined;
}

/** Downloads `url` to `file`, following the redirects GitHub release assets go through. */
function fetchToFile(url: string, file: string, token: vscode.CancellationToken, redirects = 0): Promise<void> {
  return new Promise((resolve, reject) => {
    if (redirects > 10) {
      reject(new Error(`Too many redirects while downloading ${url}.`));
      return;
    }

    const request = https.get(url, { headers: { "User-Agent": "nanyx-vscode" } }, (response) => {
      const status = response.statusCode ?? 0;

      if (status >= 300 && status < 400 && response.headers.location) {
        response.resume();
        const next = new URL(response.headers.location, url).toString();
        fetchToFile(next, file, token, redirects + 1).then(resolve, reject);
        return;
      }

      if (status === 404) {
        response.resume();
        reject(new Error(`No compiler release was found at ${url}. Check the nanyx.compilerVersion and nanyx.downloadUrl settings, or set nanyx.path.`));
        return;
      }

      if (status !== 200) {
        response.resume();
        reject(new Error(`Downloading ${url} failed with HTTP ${status}.`));
        return;
      }

      const output = fs.createWriteStream(file);
      const cancel = token.onCancellationRequested(() => {
        request.destroy(new Error("The download was cancelled."));
      });

      response.pipe(output);
      output.on("finish", () => {
        cancel.dispose();
        output.close(() => resolve());
      });
      output.on("error", (error) => {
        cancel.dispose();
        reject(error);
      });
    });

    request.on("error", (error: NodeJS.ErrnoException) => {
      const offline = error.code === "ENOTFOUND" || error.code === "EAI_AGAIN" || error.code === "ECONNREFUSED" || error.code === "ETIMEDOUT";
      reject(
        offline
          ? new Error(`Could not reach ${new URL(url).host} to download the Nanyx compiler (${error.code}). Connect to the internet and run "Nanyx: Download the compiler", or set nanyx.path to a local build.`)
          : error
      );
    });
  });
}

/**
 * Unpacks an archive with the platform's `tar`: macOS and Linux have it, and the `tar.exe` that ships
 * with Windows 10 and later also reads zip files.
 */
function extract(archive: string, into: string): void {
  const result = spawnSync("tar", ["-xf", archive, "-C", into], { encoding: "utf8" });
  if (result.status === 0) {
    return;
  }

  if (process.platform === "win32" && archive.endsWith(".zip")) {
    const fallback = spawnSync(
      "powershell",
      ["-NoProfile", "-NonInteractive", "-Command", `Expand-Archive -LiteralPath '${archive}' -DestinationPath '${into}' -Force`],
      { encoding: "utf8" }
    );
    if (fallback.status === 0) {
      return;
    }
  }

  const detail = (result.stderr || result.error?.message || `exit code ${result.status}`).trim();
  throw new Error(`Unpacking ${path.basename(archive)} failed: ${detail}`);
}
