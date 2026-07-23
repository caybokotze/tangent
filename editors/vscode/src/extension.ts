import * as vscode from 'vscode';
import {
	Executable,
	LanguageClient,
	LanguageClientOptions,
	Location as LspLocation,
	RequestType,
	ServerOptions,
	Trace
} from 'vscode-languageclient/node';

/** Params for the `tangent/contractDefinition` request. */
export interface ContractDefinitionParams {
	/** The contract name as written in `[Tangent("...")]`. */
	contract: string;
}

/**
 * Custom request: given a contract name, resolve the `contract <name> { ... }`
 * declaration in the workspace's `.tan` files. Returns `null` when unknown.
 */
const ContractDefinitionRequest = new RequestType<ContractDefinitionParams, LspLocation | null, void>(
	'tangent/contractDefinition'
);

const INSTALL_COMMAND = 'dotnet tool install -g Tangent.Cli';

/** Matches `[Tangent("Contract")]` / `[TangentAttribute("Contract")]` in C# source. */
const TANGENT_ATTRIBUTE = /\[\s*(?:[A-Za-z_][A-Za-z0-9_]*\s*:\s*)?Tangent(?:Attribute)?\s*\(\s*"((?:[^"\\]|\\.)*)"/g;

let client: LanguageClient | undefined;
let output: vscode.OutputChannel;
let warnedAboutMissingServer = false;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
	output = vscode.window.createOutputChannel('Tangent');
	context.subscriptions.push(output);

	context.subscriptions.push(
		vscode.commands.registerCommand('tangent.restartServer', () => restartServer(context)),
		vscode.commands.registerCommand('tangent.showGraph', showGraph),
		vscode.languages.registerDefinitionProvider(
			{ language: 'csharp', scheme: 'file' },
			new ContractDefinitionProvider()
		)
	);

	await startServer(context);
}

export async function deactivate(): Promise<void> {
	await stopServer();
}

function config(): vscode.WorkspaceConfiguration {
	return vscode.workspace.getConfiguration('tangent');
}

async function startServer(context: vscode.ExtensionContext): Promise<void> {
	const settings = config();
	const command = settings.get<string>('server.command', 'dotnet');
	const args = settings.get<string[]>('server.args', ['tangent', 'serve']);

	const executable: Executable = {
		command,
		args,
		options: { cwd: vscode.workspace.workspaceFolders?.[0]?.uri.fsPath }
	};
	const serverOptions: ServerOptions = { run: executable, debug: executable };

	const clientOptions: LanguageClientOptions = {
		documentSelector: [
			{ language: 'tangent' },
			{ language: 'csharp' }
		],
		synchronize: {
			fileEvents: vscode.workspace.createFileSystemWatcher('**/.tangent/**/*.tan')
		},
		outputChannel: output,
		traceOutputChannel: output
	};

	const next = new LanguageClient('tangent', 'Tangent Language Server', serverOptions, clientOptions);
	context.subscriptions.push(next);

	try {
		await next.start();
		await next.setTrace(traceFromSetting(settings.get<string>('server.trace', 'off')));
		client = next;
		output.appendLine(`Tangent language server started: ${command} ${args.join(' ')}`);
	} catch (error) {
		client = undefined;
		output.appendLine(`Failed to start the Tangent language server: ${describe(error)}`);
		reportMissingServer(command);
	}
}

async function stopServer(): Promise<void> {
	const current = client;
	client = undefined;
	if (!current) {
		return;
	}
	try {
		await current.stop();
	} catch (error) {
		output.appendLine(`Failed to stop the Tangent language server: ${describe(error)}`);
	}
}

async function restartServer(context: vscode.ExtensionContext): Promise<void> {
	warnedAboutMissingServer = false;
	await stopServer();
	await startServer(context);
}

async function showGraph(): Promise<void> {
	const url = config().get<string>('viewerUrl', 'http://localhost:5173');
	await vscode.env.openExternal(vscode.Uri.parse(url));
}

function traceFromSetting(value: string): Trace {
	switch (value) {
		case 'messages':
			return Trace.Messages;
		case 'verbose':
			return Trace.Verbose;
		default:
			return Trace.Off;
	}
}

function describe(error: unknown): string {
	return error instanceof Error ? error.message : String(error);
}

function reportMissingServer(command: string): void {
	if (warnedAboutMissingServer) {
		return;
	}
	warnedAboutMissingServer = true;

	const copy = 'Copy install command';
	const showOutput = 'Show output';
	void vscode.window
		.showWarningMessage(
			`Could not start the Tangent language server ('${command}'). Install it with: ${INSTALL_COMMAND}`,
			copy,
			showOutput
		)
		.then(choice => {
			if (choice === copy) {
				return vscode.env.clipboard.writeText(INSTALL_COMMAND);
			}
			if (choice === showOutput) {
				output.show(true);
			}
			return undefined;
		});
}

/**
 * Resolves go-to-definition from a `[Tangent("Contract")]` attribute argument in
 * C# source to the `contract` declaration in the corresponding `.tan` file.
 */
class ContractDefinitionProvider implements vscode.DefinitionProvider {
	async provideDefinition(
		document: vscode.TextDocument,
		position: vscode.Position,
		token: vscode.CancellationToken
	): Promise<vscode.Location | undefined> {
		const current = client;
		if (!current) {
			return undefined;
		}

		const contract = contractAt(document.lineAt(position.line).text, position.character);
		if (contract === undefined) {
			return undefined;
		}

		try {
			const result = await current.sendRequest(
				ContractDefinitionRequest,
				{ contract },
				token
			);
			if (!result) {
				return undefined;
			}
			return await current.protocol2CodeConverter.asLocation(result);
		} catch (error) {
			output.appendLine(`tangent/contractDefinition failed: ${describe(error)}`);
			return undefined;
		}
	}
}

/**
 * Returns the contract name when `character` sits inside the string argument of a
 * `[Tangent("...")]` attribute on `line`, otherwise `undefined`.
 */
export function contractAt(line: string, character: number): string | undefined {
	TANGENT_ATTRIBUTE.lastIndex = 0;
	let match: RegExpExecArray | null;
	while ((match = TANGENT_ATTRIBUTE.exec(line)) !== null) {
		const value = match[1];
		const end = match.index + match[0].length - 1; // index of the closing quote
		const start = end - value.length; // index of the first content character
		if (character >= start && character <= end) {
			return value;
		}
	}
	return undefined;
}
