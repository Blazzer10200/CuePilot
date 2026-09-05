import { describe, expect, it } from "vitest";
import type { Snapshot } from "./engine.svelte";
import fixture from "../../../tests/contracts/bridge-response-v1.json";

describe("shared bridge fixture", () => {
  it("keeps the TypeScript boundary on protocol v1 with required identity fields", () => {
    const message = fixture as { result: Partial<Snapshot> };
    expect(message.result).toMatchObject({ protocolVersion:1, engineVersion:"fixture", routineState:"Stopped", targets:[], setupVerification:null });
  });
});
