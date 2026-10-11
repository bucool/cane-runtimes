import { RuntimeErrorV1 } from "../errors.js";

function exprel(value: number): number { return value === 0 ? 1 : Math.expm1(value) / value; }

/** Constant-coefficient spring evolution in binary64. */
export function advanceSpring(position: number, velocity: number, seconds: number, acceleration: number, stiffness: number, drag: number): readonly [number, number] {
  if (![position, velocity, seconds, acceleration, stiffness, drag].every(Number.isFinite) || seconds < 0 || stiffness < 0 || drag < 0) fail();
  if (seconds === 0 || (velocity === 0 && acceleration === stiffness * position)) return [position, velocity];
  let nextPosition: number, nextVelocity: number;
  if (Math.max(drag * seconds, Math.sqrt(stiffness) * seconds) < 0.5) {
    let pt = velocity * seconds, vt = (acceleration - stiffness * position - drag * velocity) * seconds;
    let dp = 0, dv = 0;
    for (let order = 1; order <= 24; order += 1) {
      dp += pt; dv += vt;
      const interval = seconds / (order + 1), next = vt * interval;
      vt = (-stiffness * pt - drag * vt) * interval; pt = next;
    }
    nextPosition = position + dp; nextVelocity = velocity + dv;
  } else {
    let h00: number, h01: number, h10: number, h11: number, forced: number;
    if (stiffness === 0) {
      const decay = Math.exp(-drag * seconds), integral = -Math.expm1(-drag * seconds) / drag;
      h00 = 1; h01 = integral; h10 = 0; h11 = decay; forced = (seconds - integral) / drag;
    } else {
      const half = drag * 0.5, discriminant = half * half - stiffness;
      if (discriminant >= 0) {
        const gap = Math.sqrt(discriminant), fast = -half - gap, slow = -stiffness / (half + gap);
        const decay = Math.exp(slow * seconds), response = decay * seconds * exprel((fast - slow) * seconds);
        h00 = decay - slow * response; h01 = response; h10 = -stiffness * response;
        h11 = Math.exp(fast * seconds) + slow * response; forced = (seconds * exprel(slow * seconds) - response) / -fast;
      } else {
        const frequency = Math.sqrt(-discriminant), phase = frequency * seconds, decay = Math.exp(-half * seconds);
        const response = decay * Math.sin(phase) / frequency, cosine = decay * Math.cos(phase);
        const sineHalf = Math.sin(phase * 0.5);
        h00 = cosine + half * response; h01 = response; h10 = -stiffness * response; h11 = cosine - half * response;
        forced = (-Math.expm1(-half * seconds) + decay * 2 * sineHalf * sineHalf - half * response) / stiffness;
      }
    }
    nextPosition = h00 * position + h01 * velocity + forced * acceleration;
    nextVelocity = h10 * position + h11 * velocity + h01 * acceleration;
  }
  if (!Number.isFinite(nextPosition) || !Number.isFinite(nextVelocity)) fail();
  return [nextPosition, nextVelocity];
}

function fail(): never { throw new RuntimeErrorV1("nonFinite", "apply", "Spring state cannot be represented."); }
