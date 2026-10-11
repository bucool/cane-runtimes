import type { RuntimeSequenceModeV1 } from "../contracts.js";
import { RuntimeErrorV1 } from "../errors.js";

const bits = new DataView(new ArrayBuffer(4));
export function binary32Units(value: number): bigint {
  bits.setFloat32(0, value, true);
  const word = bits.getUint32(0, true) & 0x7fffffff;
  const exponent = word >>> 23, fraction = word & 0x7fffff;
  return exponent === 0 ? BigInt(fraction) : BigInt(fraction | 0x800000) << BigInt(exponent - 1);
}

/** Half-open frame intervals measured in exact binary32 units. */
export function sampleSequenceIndex(time: number, start: number, interval: number, count: number, initial: number, mode: RuntimeSequenceModeV1): number | null {
  time = Math.fround(time); start = Math.fround(start); interval = Math.fround(interval);
  if (![time, start, interval].every(Number.isFinite) || time < 0 || start < 0 || interval < 0
    || (interval === 0 && mode !== "fixedFrame") || !Number.isSafeInteger(count) || count < 1
    || !Number.isSafeInteger(initial) || initial < 0 || initial >= count) {
    throw new RuntimeErrorV1("invalidArgument", "sampleSequence", "Invalid sequence interval, index, or time.");
  }
  if (time < start) return null;
  if (mode === "fixedFrame" || count === 1) return initial;
  const steps = (binary32Units(time) - binary32Units(start)) / binary32Units(interval);
  const last = BigInt(count - 1), offset = BigInt(initial);
  let forward: bigint;
  switch (mode) {
    case "playOnce": case "playOnceReverse": forward = steps < last - offset ? offset + steps : last; break;
    case "repeat": case "repeatReverse": forward = (steps % BigInt(count) + offset) % BigInt(count); break;
    case "backAndForthLoop": case "backAndForthLoopReverse": {
      const period = 2n * last, position = (steps % period + offset) % period;
      forward = position < period - position ? position : period - position;
      break;
    }
    default: throw new RuntimeErrorV1("invalidArgument", "sampleSequence", "Unknown sequence mode.");
  }
  const reverse = mode === "playOnceReverse" || mode === "repeatReverse" || mode === "backAndForthLoopReverse";
  return Number(reverse ? last - forward : forward);
}

/** Whether offset + n * period, n >= 0, intersects the open/closed clock interval. */
export function periodicResetCrossed(from:number,to:number,offset:number,period:number):boolean {
  if (!(to>from) || !(period>0) || to<offset) return false;
  if (from<offset) return true;
  const origin=binary32Units(offset),step=binary32Units(period);
  return (binary32Units(to)-origin)/step > (binary32Units(from)-origin)/step;
}
