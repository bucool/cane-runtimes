using System;
using System.Collections.Generic;

namespace Cane
{
    internal sealed class ArcBudget
    {
        private int remaining = 1000000;
        internal void Visit() { if (remaining == 0) throw Limit(); remaining--; }
        internal static RuntimeException Limit() => new RuntimeException(RuntimeErrorCode.ResourceLimit, "apply", "Path subdivision budget is exhausted.");
    }
    internal sealed class CubicArc
    {
        internal static RuntimeException Failure() => new RuntimeException(RuntimeErrorCode.NonFinite, "apply", "Path geometry cannot be represented.");
        internal readonly DoublePoint[] Controls;
        private readonly List<Leaf> leaves = new List<Leaf>();
        internal readonly double Length;
        private readonly double tolerance;
        private readonly struct Node
        {
            internal readonly DoublePoint[] Controls;
            internal readonly double Start, End;
            internal readonly int Depth;
            internal Node(DoublePoint[] controls, double start, double end, int depth) { Controls = controls; Start = start; End = end; Depth = depth; }
        }
        private readonly struct Leaf
        {
            internal readonly Node Node;
            internal readonly double From, To;
            internal Leaf(Node node, double from, double to) { Node = node; From = from; To = to; }
        }
        private static DoublePoint Sub(DoublePoint a, DoublePoint b) => new DoublePoint(a.X - b.X, a.Y - b.Y);
        private static DoublePoint Lerp(DoublePoint a, DoublePoint b, double t) => new DoublePoint(a.X * (1 - t) + b.X * t, a.Y * (1 - t) + b.Y * t);
        private static double Clamp(double value) => Math.Max(0, Math.Min(1, value));
        private static (DoublePoint[] Left, DoublePoint[] Right) Split(DoublePoint[] p, double t)
        {
            DoublePoint a = Lerp(p[0], p[1], t), b = Lerp(p[1], p[2], t), c = Lerp(p[2], p[3], t), d = Lerp(a, b, t), e = Lerp(b, c, t), f = Lerp(d, e, t);
            return (new[] { p[0], a, d, f }, new[] { f, e, c, p[3] });
        }
        private static (double Lower, double Upper) Bounds(DoublePoint[] p)
        {
            double lower = Sub(p[3], p[0]).Length, upper = 0; for (int i = 0; i < 3; i++) upper += Sub(p[i + 1], p[i]).Length; upper = Math.Max(upper, lower);
            if (double.IsNaN(lower) || double.IsInfinity(lower) || double.IsNaN(upper) || double.IsInfinity(upper)) throw Failure(); return (lower, upper);
        }
        private static double PartialLength(DoublePoint[] p, double t) { var bounds = Bounds(Split(p, Clamp(t)).Left); return bounds.Lower + (bounds.Upper - bounds.Lower) * .5; }
        private static double BoxDistance(DoublePoint[] p, DoublePoint target)
        {
            double x0 = double.PositiveInfinity, y0 = x0, x1 = double.NegativeInfinity, y1 = x1;
            foreach (DoublePoint q in p) { x0 = Math.Min(x0, q.X); y0 = Math.Min(y0, q.Y); x1 = Math.Max(x1, q.X); y1 = Math.Max(y1, q.Y); }
            return new DoublePoint(target.X < x0 ? x0 - target.X : target.X > x1 ? target.X - x1 : 0, target.Y < y0 ? y0 - target.Y : target.Y > y1 ? target.Y - y1 : 0).Length;
        }
        internal CubicArc(DoublePoint[] controls, ArcBudget budget)
        {
            if (controls.Length != 4) throw Failure(); foreach (DoublePoint p in controls) if (double.IsNaN(p.X) || double.IsNaN(p.Y) || Math.Abs(p.X) > float.MaxValue || Math.Abs(p.Y) > float.MaxValue) throw Failure();
            Controls = controls; tolerance = Bounds(controls).Upper * 1e-6; double length = 0;
            var pending = new Stack<Node>(); pending.Push(new Node(controls, 0, 1, 0));
            while (pending.Count > 0)
            {
                Node n = pending.Pop(); budget.Visit(); var bounds = Bounds(n.Controls);
                if (bounds.Upper - bounds.Lower <= tolerance * (n.End - n.Start))
                {
                    double next = length + bounds.Lower + (bounds.Upper - bounds.Lower) * .5; if (double.IsNaN(next) || double.IsInfinity(next)) throw Failure();
                    leaves.Add(new Leaf(n, length, next)); length = next;
                }
                else
                {
                    if (n.Depth == 32) throw ArcBudget.Limit(); var split = Split(n.Controls, .5); double middle = (n.Start + n.End) * .5;
                    pending.Push(new Node(split.Right, middle, n.End, n.Depth + 1)); pending.Push(new Node(split.Left, n.Start, middle, n.Depth + 1));
                }
            }
            Length = length;
        }
        internal DoublePoint Point(double t) => Split(Controls, Clamp(t)).Left[3];
        internal DoublePoint Tangent(double t)
        {
            t = Clamp(t); DoublePoint e0 = Sub(Controls[1], Controls[0]), e1 = Sub(Controls[2], Controls[1]), e2 = Sub(Controls[3], Controls[2]);
            DoublePoint first = Lerp(Lerp(e0, e1, t), Lerp(e1, e2, t), t); if (first.Length > 0) return first;
            DoublePoint a = Sub(e1, e0), b = Sub(e2, e1), second = Lerp(a, b, t); if (t == 1) second = new DoublePoint(-second.X, -second.Y);
            if (second.Length > 0) return second; DoublePoint third = Sub(b, a); return third.Length > 0 ? third : new DoublePoint(1, 0);
        }
        internal double ParameterAtDistance(double distance)
        {
            if (distance <= 0 || Length == 0) return 0; if (distance >= Length) return 1;
            int lo = 0, hi = leaves.Count; while (lo < hi) { int middle = (lo + hi) / 2; if (leaves[middle].To < distance) lo = middle + 1; else hi = middle; }
            Leaf leaf = leaves[Math.Min(lo, leaves.Count - 1)]; double requested = distance - leaf.From, low = 0, high = 1;
            for (int i = 0; i < 48; i++) { double middle = (low + high) * .5; if (PartialLength(leaf.Node.Controls, middle) < requested) low = middle; else high = middle; }
            return leaf.Node.Start + (leaf.Node.End - leaf.Node.Start) * (low + high) * .5;
        }
        internal double DistanceAtParameter(double t)
        {
            if (t <= 0) return 0; if (t >= 1) return Length;
            int lo = 0, hi = leaves.Count; while (lo < hi) { int middle = (lo + hi) / 2; if (leaves[middle].Node.End < t) lo = middle + 1; else hi = middle; }
            Leaf leaf = leaves[Math.Min(lo, leaves.Count - 1)]; double fraction = (t - leaf.Node.Start) / (leaf.Node.End - leaf.Node.Start);
            return leaf.From + Math.Max(0, Math.Min(leaf.To - leaf.From, PartialLength(leaf.Node.Controls, fraction)));
        }
        internal double ClosestParameter(DoublePoint target, ArcBudget budget)
        {
            if (double.IsNaN(target.X) || double.IsNaN(target.Y) || double.IsInfinity(target.X) || double.IsInfinity(target.Y)) throw Failure();
            double bestT = 0, bestDistance = Sub(Controls[0], target).Length, endDistance = Sub(Controls[3], target).Length;
            if (endDistance < bestDistance) { bestT = 1; bestDistance = endDistance; } if (double.IsInfinity(bestDistance)) throw Failure();
            var pending = new Stack<Node>(); pending.Push(new Node(Controls, 0, 1, 0));
            while (pending.Count > 0)
            {
                Node n = pending.Pop(); budget.Visit(); if (bestDistance - BoxDistance(n.Controls, target) <= tolerance) continue;
                var split = Split(n.Controls, .5); double middle = (n.Start + n.End) * .5, distance = Sub(split.Left[3], target).Length;
                if (distance < bestDistance || (distance == bestDistance && middle < bestT)) { bestT = middle; bestDistance = distance; }
                if (bestDistance - BoxDistance(n.Controls, target) <= tolerance) continue; if (n.Depth == 32) throw ArcBudget.Limit();
                pending.Push(new Node(split.Right, middle, n.End, n.Depth + 1)); pending.Push(new Node(split.Left, n.Start, middle, n.Depth + 1));
            }
            return bestT;
        }
    }
}
