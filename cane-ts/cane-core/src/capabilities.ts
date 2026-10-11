import type { RuntimeCapabilitiesV1 } from "./contracts.js";
import { deepFreeze } from "./internal.js";
import { SUPPORTED_RUNTIME_FEATURES_V1 } from "./validation.js";

const CAPABILITIES: RuntimeCapabilitiesV1 = deepFreeze<RuntimeCapabilitiesV1>({
  implementationName: "cane-typescript-runtime",
  implementationVersion: "0.1.0",
  runtimeFormat: { major: 1, minor: 1 },
  runtimeApi: { major: 1, minor: 13 },
  numericPrecision: "binary32",
  supportedRuntimeFeatures: [...SUPPORTED_RUNTIME_FEATURES_V1],
  limitations: [
    "decoded texture validation requires the host to supply a complete observed direct-image and atlas-page catalog",
    "PixiJS shaders and browser renderer behavior are adapter concerns and are not certified by the renderer-neutral Core conformance result",
  ],
  conformance: {
    suiteVersion: "1.17.0",
    manifestSha256: "f462e113115ecd60322f6f5d9df451e05d82649d0ded3278137f4b873e3ce0f9",
    status: "passed",
  },
});

export function runtimeCapabilitiesV1(): RuntimeCapabilitiesV1 {
  return CAPABILITIES;
}
