# Exact sequence clock v2

Runtime API 1.11 and `animation.sequence.clock-v2` select this timing contract.

## Inputs and interval contract

A sequence has N images in declared order, N >= 1. A key provides its start time S,
initial index I in [0,N), positive frame interval D (zero is also allowed for FixedFrame),
and a mode. Time inputs are finite nonnegative binary32 values, interpreted as their
exact represented rational numbers. Negative zero means zero.

Before S the key contributes no value. At or after S, FixedFrame selects I. Other modes
advance by the unique integer Q satisfying

    Q D <= T - S < (Q+1) D.

Thus a frame owns a half-open interval. There is no tolerance, upward time bias,
rounded intermediate subtraction/division, or machine-sized cast of Q. Q may be
much larger than any native integer. Exact represented times decide the boundary;
the kernel does not reconstruct a user's intended decimal time.

Let K = I+Q as an unbounded integer. The mode's forward result is:

- PlayOnce: min(K,N-1).
- Repeat: the remainder of K divided by N.
- BackAndForthLoop: for N=1 use zero; otherwise let P=2(N-1) and R=K mod P, then min(R,P-R).

For a reverse mode, return N-1 minus the corresponding forward result. This is a
reflection of the same path, not a separate overflowing phase-offset addition.
FixedFrame has no direction. Mode names and image order preserve the existing feature set.
Invalid nonfinite/negative times, intervals, empty sequences and out-of-range initial
indices produce explicit errors; they are not clamped to a plausible image.

## Exact finite arithmetic derived from binary32

Every finite nonnegative binary32 value is an integer multiple of U=2^-149.
For the stored exponent E and fraction F:

    E=0: value/U = F
    1<=E<=254: value/U = (2^23+F) * 2^(E-1).

The largest such integer is below 2^277. Therefore nine base-2^32 limbs (288 bits)
hold each input, their nonnegative difference, and Q. This bound follows from the
input representation, not a project size limit or a cap on elapsed frames.

Form integer A=T/U-S/U by exact subtraction. Write D/U = M*2^d, with M at most
24 bits and d nonnegative. Then

    floor(A/(M*2^d)) = floor(floor(A/2^d)/M).

Proof: write A=2^d B+r, 0<=r<2^d, then B=Mq+s, 0<=s<M. The discarded fraction
(s+r/2^d)/M is strictly less than one. Thus shifting right by d, then ordinary
base-2^32 long division by M, gives Q exactly. Each long-division intermediate
is below 2^56, so u64 suffices. No general-purpose big-integer dependency is needed.

For Repeat/BackAndForthLoop, reduce the nine quotient limbs modulo the period using base-2^32
Horner evaluation. N and I are represented as u64 at this leaf interface; P is below
2^65, so each Horner intermediate is below 2^97 and fits u128. Add I only after
reduction, then reduce once more. PlayOnce compares Q to N-1-I before any small-integer
conversion. These rules work identically on 32- and 64-bit hosts for common inputs.
Actual attachment arrays still obey the existing loader and platform resource limits.

The implementation has fixed stack storage and a fixed bound on limb operations,
independent of elapsed frame count. Time input validation and mode mapping belong
to runtime; serialized feature/profile detection belongs to format. Renderers consume
the selected final image and acquire no timeline or clock responsibilities.

## Independent verification

Before kernel implementation, generate answers from Python Fraction constructed
from exact binary32 values. Use arbitrary integer floor and small explicit walk
tables for ordinary image counts, with direct integer path formulas for huge counts.
Neither old runtime nor upstream output is an answer source.

Cover FixedFrame and all forward/reverse modes, before/start/boundary/adjacent times,
nonzero initial indices, one image, subnormals, maximal finite values, cancellation
between differently scaled times, and counts crossing 32-/64-bit boundaries.
Also test invalid input, return-range and reverse-reflection invariants, exact power
of two rescaling where values stay representable, and unchanged public failure state
after production integration. Measure CPU cost using self-authored data.

## Compatibility

Advancing sequence keys require `animation.sequence.clock-v2` and behavior API 1.11.
FixedFrame-only or empty sequence tracks keep their otherwise required behavior version.
The gate applies to inactive animations and Slider targets as well as current playback.
The current JSON/CANEB 1.1 native mode names are defined in
[Runtime Model](../formats/RUNTIME_MODEL_V1.md). Missing feature declarations are errors.
