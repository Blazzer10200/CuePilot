import { describe, expect, it } from "vitest";
import type { Snapshot } from "./engine.svelte";
import fixture from "../../../tests/contracts/bridge-response-v1.json";
// @ts-ignore node:fs has no type declarations in this project
import { readFileSync } from "node:fs";

const bridgeSource: string = readFileSync(new URL("../../../src/Application/UiBridge.cs", import.meta.url), "utf8");
const clientSources = import.meta.glob(["../lib/**/*.{ts,svelte}", "../App.svelte", "../Overlay.svelte", "!../**/*.test.ts"], { query: "?raw", import: "default", eager: true }) as Record<string, string>;

function engineCommands(source: string) {
  const start = source.indexOf("switch (command)");
  const end = source.indexOf("default:", start);
  return [...source.slice(start, end).matchAll(/case "([a-z_]+)":/g)].map(match => match[1]);
}

function clientCommands(sources: Record<string, string>) {
  const found = new Set<string>();
  for (const source of Object.values(sources)) {
    for (const match of source.matchAll(/\bcommand:\s*([^,}\n]+)/g)) {
      for (const literal of match[1].matchAll(/(?<!===\s*)"([a-z_]+)"/g)) found.add(literal[1]);
    }
    for (const match of source.matchAll(/async command\(command: ([^)]+)\)/g)) {
      for (const literal of match[1].matchAll(/"([a-z_]+)"/g)) found.add(literal[1]);
    }
  }
  return [...found].sort();
}

describe("shared bridge fixture", () => {
  it("keeps the TypeScript boundary on protocol v1 with required identity fields", () => {
    const message = fixture as { result: Partial<Snapshot> };
    expect(message.result).toMatchObject({ protocolVersion:1, engineVersion:"fixture", routineState:"Stopped", targets:[], setupVerification:null });
  });
});

describe("bridge command contract", () => {
  it("extracts the engine command switch and the client command names", () => {
    expect(engineCommands(bridgeSource)).toEqual(expect.arrayContaining(["snapshot", "start", "stop", "save_settings"]));
    expect(clientCommands(clientSources)).toEqual(expect.arrayContaining(["snapshot", "start", "stop", "save_settings", "start_pickpocket_observe"]));
  });

  it("only sends commands that UiBridge.cs handles", () => {
    const handled = new Set(engineCommands(bridgeSource));
    const unhandled = clientCommands(clientSources).filter(name => !handled.has(name));
    expect(unhandled).toEqual([]);
  });
});
