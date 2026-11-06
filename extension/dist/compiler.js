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
exports.DEFAULT_DOWNLOAD_URL = exports.DEFAULT_COMPILER_VERSION = void 0;
exports.readSettings = readSettings;
exports.locateCompiler = locateCompiler;
exports.runtimeIdentifier = runtimeIdentifier;
exports.downloadUrl = downloadUrl;
exports.downloadCompiler = downloadCompiler;
const child_process_1 = require("child_process");
const fs = __importStar(require("fs"));
const https = __importStar(require("https"));
const os = __importStar(require("os"));
const path = __importStar(require("path"));
const vscode = __importStar(require("vscode"));
/**
 * The compiler release the extension downloads when it finds no `nanyx` of its own. Bump it with each
 * compiler release the extension should use (the `<Version>` in NanyxCompiler.Cli.csproj at the time).
 */
exports.DEFAULT_COMPILER_VERSION = "0.5.2";
/**
 * Where a release lives. `${version}`, `${rid}` and `${ext}` are substituted; this is what the
 * `nanyx.downloadUrl` setting overrides.
 */
exports.DEFAULT_DOWNLOAD_URL = "https://github.com/nanyx-lang/nanyx/releases/download/compiler/${version}/nanyx-${version}-${rid}.${ext}";
const EXECUTABLE = process.platform === "win32" ? "nanyx.exe" : "nanyx";
function readSettings() {
    const config = vscode.workspace.getConfiguration("nanyx");
    return {
        path: config.get("path", "").trim(),
        version: config.get("compilerVersion", "").trim() || exports.DEFAULT_COMPILER_VERSION,
        downloadUrl: config.get("downloadUrl", "").trim() || exports.DEFAULT_DOWNLOAD_URL
    };
}
/**
 * Finds the `nanyx` executable to run the language server with, in this order: the `nanyx.path`
 * setting, PATH, then a copy this extension downloaded earlier. Returns `undefined` when none exists.
 */
function locateCompiler(context, settings) {
    if (settings.path) {
        if (fs.existsSync(settings.path)) {
            return settings.path;
        }
        throw new Error(`nanyx.path is set to "${settings.path}", but no file exists there.`);
    }
    return findOnPath() ?? findDownloaded(context, settings);
}
/** The platform's runtime identifier as the compiler release names it, or `undefined` when there is no build for it. */
function runtimeIdentifier() {
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
function downloadUrl(settings, rid) {
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
async function downloadCompiler(context, settings, progress, token) {
    const rid = runtimeIdentifier();
    if (!rid) {
        throw new Error(`There is no Nanyx compiler build for ${process.platform}/${process.arch}. ` +
            "Build it from source and point the nanyx.path setting at it.");
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
    }
    finally {
        fs.rmSync(archive, { force: true });
    }
}
function installDir(context, settings, rid) {
    return path.join(context.globalStorageUri.fsPath, `nanyx-${settings.version}-${rid}`);
}
function findDownloaded(context, settings) {
    const rid = runtimeIdentifier();
    if (!rid) {
        return undefined;
    }
    const executable = path.join(installDir(context, settings, rid), EXECUTABLE);
    return fs.existsSync(executable) ? executable : undefined;
}
function findOnPath() {
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
function fetchToFile(url, file, token, redirects = 0) {
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
        request.on("error", (error) => {
            const offline = error.code === "ENOTFOUND" || error.code === "EAI_AGAIN" || error.code === "ECONNREFUSED" || error.code === "ETIMEDOUT";
            reject(offline
                ? new Error(`Could not reach ${new URL(url).host} to download the Nanyx compiler (${error.code}). Connect to the internet and run "Nanyx: Download the compiler", or set nanyx.path to a local build.`)
                : error);
        });
    });
}
/**
 * Unpacks an archive with the platform's `tar`: macOS and Linux have it, and the `tar.exe` that ships
 * with Windows 10 and later also reads zip files.
 */
function extract(archive, into) {
    const result = (0, child_process_1.spawnSync)("tar", ["-xf", archive, "-C", into], { encoding: "utf8" });
    if (result.status === 0) {
        return;
    }
    if (process.platform === "win32" && archive.endsWith(".zip")) {
        const fallback = (0, child_process_1.spawnSync)("powershell", ["-NoProfile", "-NonInteractive", "-Command", `Expand-Archive -LiteralPath '${archive}' -DestinationPath '${into}' -Force`], { encoding: "utf8" });
        if (fallback.status === 0) {
            return;
        }
    }
    const detail = (result.stderr || result.error?.message || `exit code ${result.status}`).trim();
    throw new Error(`Unpacking ${path.basename(archive)} failed: ${detail}`);
}
//# sourceMappingURL=compiler.js.map