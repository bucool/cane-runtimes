/** Renderer-independent channel composition. Every endpoint reads the same lower-track pose. */
export type PoseValue = number | string | boolean | null | readonly string[] | readonly number[];
export type ChannelKind = "scalar" | "angle" | "bounded" | "vector" | "choice" | "order";
export interface PoseChannel {
  readonly kind: ChannelKind;
  readonly setup: PoseValue;
  readonly minimum?: number;
  readonly maximum?: number;
}
export interface ChannelSample {
  readonly values: ReadonlyMap<string, PoseValue>;
  readonly weight: number;
  readonly additive: boolean;
  readonly orderPatches: readonly (readonly string[])[];
}
export class AnimationWork {
  private remaining = 1_000_000_000;
  use(amount = 1): void {
    if (!Number.isSafeInteger(amount) || amount < 0 || amount > this.remaining) throw new Error("Animation work limit exceeded.");
    this.remaining -= amount;
  }
}
export const channelKey = (owner: string, id: string, field: string): string => JSON.stringify([owner, id, field]);
export function checkedPoseNumber(value: number): number {
  if (!Number.isFinite(value) || Math.abs(value) > 3.4028234663852886e38) throw new Error("Animation channel exceeded binary32 range.");
  return Math.fround(value);
}
function angleDelta(value: number): number { return ((value + 180) % 360 + 360) % 360 - 180; }
function bounded(channel: PoseChannel, value: number): number {
  return checkedPoseNumber(channel.kind === "bounded" ? Math.max(channel.minimum ?? 0, Math.min(channel.maximum ?? 1, value)) : value);
}
function mix(channel: PoseChannel, from: PoseValue, to: PoseValue, weight: number, additive: boolean): PoseValue {
  if (weight === 0) return from;
  if (channel.kind === "choice" || channel.kind === "order") return weight >= 0.5 ? to : from;
  if (channel.kind === "vector") {
    const a = from as readonly number[], b = to as readonly number[], setup = channel.setup as readonly number[];
    if (a.length !== b.length || a.length !== setup.length) throw new Error("Animation vector layout changed.");
    return a.map((v, i) => checkedPoseNumber(additive ? v + weight * (b[i]! - setup[i]!) : (1 - weight) * v + weight * b[i]!));
  }
  const a = from as number, b = to as number, setup = channel.setup as number;
  if (channel.kind === "angle") return checkedPoseNumber(!additive && weight === 1 ? b : a + weight * angleDelta(b - (additive ? setup : a)));
  return bounded(channel, additive ? a + weight * (b - setup) : (1 - weight) * a + weight * b);
}
export function applyChannelSample(schema: ReadonlyMap<string, PoseChannel>, underlay: ReadonlyMap<string, PoseValue>, sample: ChannelSample, work: AnimationWork): Map<string, PoseValue> {
  work.use(schema.size + sample.values.size);
  const result = new Map(underlay);
  if (sample.weight === 0) return result;
  for (const [key, value] of sample.values) {
    const channel = schema.get(key);
    if (channel === undefined) throw new Error("Unknown animation channel.");
    if (channel.kind === "vector") work.use((value as readonly number[]).length);
    result.set(key, mix(channel, underlay.get(key)!, value, sample.weight, sample.additive));
  }
  if (sample.orderPatches.length !== 0 && sample.weight >= 0.5) {
    const key = channelKey("skeleton", "", "order");
    const order = [...(underlay.get(key) as readonly string[])];
    for (const patch of sample.orderPatches) {
      work.use(order.length + patch.length);
      const members = new Set(patch);
      if (members.size !== patch.length || patch.some(id => !order.includes(id))) throw new Error("Invalid animation order patch.");
      let cursor = 0;
      for (let i = 0; i < order.length; i += 1) if (members.has(order[i]!)) order[i] = patch[cursor++]!;
    }
    result.set(key, order);
  }
  return result;
}
export function transitionChannels(schema: ReadonlyMap<string, PoseChannel>, from: ReadonlyMap<string, PoseValue>, to: ReadonlyMap<string, PoseValue>, sample: ChannelSample, progress: number, hold: boolean, work: AnimationWork): Map<string, PoseValue> {
  if (progress === 0) return new Map(from);
  if (progress === 1) return new Map(to);
  work.use(schema.size);
  const result = new Map<string, PoseValue>();
  for (const [key, channel] of schema) {
    const a = from.get(key)!, b = to.get(key)!;
    if (hold && !sample.values.has(key)) { result.set(key, a); continue; }
    if (channel.kind === "vector") work.use((a as readonly number[]).length);
    result.set(key, mix(channel, a, b, progress, false));
  }
  return result;
}
