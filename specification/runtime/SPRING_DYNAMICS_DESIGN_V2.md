# Independent spring dynamics design, revision 2

## Requirements

The authoring requirement is a bone that lags an animated rest pose, oscillates toward it, and
responds to wind and gravity. Translation, rotation, X shear and X scale remain independently
mixable. Reset aligns with the current rest pose. A numerical failure must not publish partial
state. Editor commands, file I/O and rendering remain outside this pure runtime implementation.

The mathematical starting point is the mass/spring/viscous-damper differential equation. Its
underdamped, critically damped and overdamped solutions are standard differential-equation
results, illustrated in [MIT 18.03SC, Damped Harmonic Oscillators](https://ocw.mit.edu/courses/18-03sc-differential-equations-fall-2011/pages/unit-ii-second-order-constant-coefficient-linear-equations/damped-harmonic-oscillators/)
and [MIT, Lecture 04: Damped Oscillations](https://ocw.mit.edu/courses/res-8-009-introduction-to-oscillations-and-waves-summer-2017/mitres_8_009su17_lec4.pdf).
These are mathematical references, not code templates. No lecture code or prose is imported.

## Scalar evolution, derived for this implementation

Normalize `m*q'' + c*q' + k*q = F` to:

```text
q' = v
v' = a - w2*q - d*v
w2 = k/m >= 0
d = c/m >= 0
a = F/m
```

Each integration interval holds coefficients and applied force constant. Solve the linear ODE
over the actual interval; do not approximate it by a position update followed by a velocity
multiplier. Store displacement and velocity, without output-lag or previously rendered-tip state.

For the homogeneous system, write its transition as `H`, with:

```text
H00 = C + (d/2)*S
H01 = S
H10 = -w2*S
H11 = C - (d/2)*S
q_next = H00*q + H01*v + B*a
v_next = H10*q + H11*v + S*a
```

For underdamping, let `g=d/2`, `u=sqrt(w2-g*g)`, then
`C=exp(-g*h)*cos(u*h)` and `S=exp(-g*h)*sin(u*h)/u`.
Integrating the impulse response gives `B=(1-H00)/w2`.
Compute its numerator using `expm1` and `1-cos(z)=2*sin(z/2)^2` to avoid cancellation
near zero decay and full oscillation periods.

For over/critical damping, the characteristic roots are:

```text
r_fast = -g - sqrt(g*g-w2)
r_slow = -w2 / (g + sqrt(g*g-w2))
S = exp(r_slow*h) * h * exprel((r_fast-r_slow)*h)
exprel(z) = expm1(z)/z; exprel(0) = 1
H00 = exp(r_slow*h) - r_slow*S
H11 = exp(r_fast*h) + r_slow*S
B = (h*exprel(r_slow*h) - S) / (-r_fast)
```

The product-of-roots expression for `r_slow` avoids subtracting nearly equal values.
This expression for `B` avoids dividing a nearly zero numerator by a very small stiffness.
Critical damping is the equal-root limit, evaluated by the same `exprel` expression.
For exactly zero stiffness, integrate viscous velocity directly; for zero drag as well,
use the constant-acceleration polynomial.

When both `d*h` and `sqrt(w2)*h` are below 0.5, sum the affine ODE's Taylor series instead.
Start with the first derivative times `h`, then apply the homogeneous system matrix to each
term and multiply by `h/(n+1)`. Force enters only the first derivative. Twenty-four terms
cover this small dimensionless domain beyond double-precision accuracy. This avoids subtracting
nearly equal transition coefficients when obtaining a tiny displacement from a constant force.

Internal arithmetic is `f64`. Invalid coefficients, negative/non-finite duration, non-finite
input or non-finite output return failure without mutating the input. Production integration
must also validate conversion to public `f32` geometry and diagnostics.

## Bone mapping

The state has four scalar springs: world X/Y displacement, angular displacement and
logarithmic X stretch, plus the previous **unconstrained** origin and X-axis vector.
Rest-pose changes induce lag by subtracting their displacement, shortest angular change and
log-length change, weighted by inertia. Pivot translation is projected through the tip
Jacobian to drive angular/stretch lag. Zero-length axes omit undefined angular/length transfer.
The movement limit bounds incoming rest motion, not solver forces or total simulated displacement.

World force is converted to translation force and generalized angular/stretch forces using
the rest tip's tangent and axial Jacobians. This linearization deliberately uses the rest
geometry, not the mixed rendered result. Output influence never feeds back into solver state.
The exact unit, direction, parameter, timestep, activation and error rules are specified in
[the Physics profile section](PATH_PHYSICS_SLIDER_ALGORITHMS_V1.md#3-independent-physics-profile-spring-v2).

Positive stretch is `exp(log_stretch * mix * scale_x_influence)`.
Uniform Y compensation uses the same factor; volume compensation uses its reciprocal.
This preserves signed authored axes and area without an arbitrary compression-floor polynomial.
Out-of-range non-finite output is a reported numerical error, not a hidden clamp.

Host affine motion transports translation state as vectors and rest anchors as geometry.
Angular/log-stretch transport must follow the mapped axis and its differential, including
reflection and nonuniform scale. Clear-inertia removes velocities after transport; teleport
resets state. Stage all affected state before publication.

## Compatibility and migration

The profile changes dynamics, damping/force response, compression and sub-frame timing.
It requires API 1.4 and `constraint.physics.spring-v2`; the player no longer claims to
execute legacy `constraint.physics`. Format codecs continue to read the old data.
Explicit document migration preserves authored values while changing the required profile;
the opt-in RuntimeProject constructor records a migration warning. See
[Versioning](VERSIONING.md#independent-spring-profile-api-14) for the exact entry points.
Native authoring projects remain readable and re-export with the new solver declaration.
Motion review/retuning is required. Old conformance expectations have not been blanket regenerated.

## Independent validation plan

- Free particle with constant force: `q=q0+v0*h+a*h*h/2`, `v=v0+a*h`.
- Undamped quarter-period: initial displacement maps to zero position and known velocity.
- Critical damping: `(A+B*t)*exp(-g*t)` and its directly differentiated velocity.
- Overdamped case using a single chosen characteristic root.
- Constant-force equilibrium and the zero-stiffness viscous solution.
- Very short intervals, tiny stiffness, and the critical-damping limit from both sides.
- Composition of intervals under fixed coefficients, without reading old solver outputs.
- Non-increasing unforced damped energy and undamped energy conservation.
- Invalid inputs and overflowing results fail without changing the caller's state.

After scalar verification, add authored-bone tests for inertia, projection, volume, mirror,
reset, root transport, resource limits, failure atomicity, seek/playback consistency and
performance. Scalar tests alone cannot close the integration task.
