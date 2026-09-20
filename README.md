# OtterLogic.Fabrication

[![build](https://github.com/Otter-Logic/Fabrication/actions/workflows/build.yml/badge.svg)](https://github.com/Otter-Logic/Fabrication/actions/workflows/build.yml)

Making the structure, for [OtterLogic](https://github.com/Otter-Logic/Rhino3D):
what a machine can make, how many kinds of it a job needs, and how to lay it out
so as few kinds as possible are needed.

```
Core  ->  MachineLearning  ->  Unsupervised  ->  Fabrication
          (features, PCA,      (clustering      (this repo: what a
           the graph and        and fusion)      fabricator has to make)
           eigen mechanism)
```

A domain toolkit, **sibling** to
[StructuralDesign](https://github.com/Otter-Logic/StructuralDesign) — never a
dependent of it. The line between them is what the question is about: a
structural tool asks how a structure behaves, one here asks what has to be made
and how often the same thing is made twice.

## What is here

**Panel Typology** — surfaces and one rectangle in: the largest panel the machine
can cast. Each surface is laid out in its own frame, the smallest rectangle its
outline fits in, so a panel's length runs along the surface's long edge however the
surface is turned in space.

Nothing is fitted to the edge. The surface is laid out in rows and columns of the
machine's rectangle, and where its edge runs through a panel the panel is simply
**cut** there. A curve is followed as closely as the surface is drawn, a slope
comes out as triangles without being asked for, an L or a doughnut is panelised in
the separate stretches each row is in, and the panels together cover the whole
surface but for the joints.

Where the cuts go is the only choice in the layout, and it is made by **search,
not by a rule**. Each row is a small graph: the nodes are the places a cut could
fall — the ends of the run, where a chain of full panels from either end would
land, and where sharing what is left between a few equal panels would land — and
an edge from one node to another is a panel, allowed only if it is no longer than
the machine makes. The row is the cheapest path across it.

What a panel costs is what the user asked for. **Standardisation**, 0 to 1, is the
whole of the judgement: its share of the price is paid by any panel that is not the
machine's full size, and the rest of the price is `full / length - 1`, which is
nothing for a panel near full size and unbounded for a sliver. At 1 the search buys
as many full panels as it can and lets the last one come out however it comes out;
at 0 it shares the run out evenly instead — fewer types, no slivers, nothing
standard. Between them a leftover is left to itself only while it is longer than
about `(1 - s) / 2s` of a full panel: the lever is where the user draws that line,
not a number in the code.

Which panels are the **same type** is then a grouping question, and it is answered
without anybody deciding in advance what makes two panels alike. `ShapeSignature`,
in [MachineLearning](https://github.com/Otter-Logic/MachineLearning), reads every
panel's outline and learns what this population of panels actually varies by; the
panels are clustered on that. A list of measurements picked by hand — length, width,
area, how far off-centre — cannot tell a panel notched at its corners from one
notched in its middle, because they agree on every one of them. A learned signature
can, and catches a curve or a raked corner the same way.

What the toolkit contributes is the judgement, not the mechanism: a panel has no up
and no down, so turning it half way round is the same mould; turning it to any other
angle is not, because its length has to run along the stock; and its mirror is a
second mould rather than the same one. The panels from every surface are grouped
together, so a shape that turns up on four surfaces is one type made four times
instead of four bespoke panels.

The cut is complete linkage at the fabrication tolerance, and it carries a promise a
fabricator can use: **no two panels of a type are further apart than that
tolerance**, measured around their whole outlines rather than on two overall
dimensions. Raising it merges near-identical panels, which is the rationalisation
question asked one tolerance at a time. Square-cut panels, triangles and other cut
shapes are grouped apart: one is never the other.

Whether the layout has left any panel on a **smaller scale** than the rest is read
from the panels themselves, by scale separation over what had to be cut, rather than
from a smallest size written into the code.

The engine keeps what it **measures** apart from what it **calls** things. Which
panels exist and which of them are the same is measured; the marks P1, T1, C1 and
the order they come in are a house style, and live alone in `PanelNaming`.

## Rules

- Depends on [Unsupervised](https://github.com/Otter-Logic/Unsupervised) (which
  carries [MachineLearning](https://github.com/Otter-Logic/MachineLearning) and
  [Core](https://github.com/Otter-Logic/Core) transitively). Never another domain
  toolkit, never an adaptor.
- No UI. No Grasshopper. Adaptors wrap this; it does not know they exist.

## Build and test

```
dotnet build OtterLogic.slnx
dotnet test OtterLogic.slnx
```

Pure geometry over plain arrays — no Rhino, no licence needed, runs anywhere.

Clone [Core](https://github.com/Otter-Logic/Core),
[MachineLearning](https://github.com/Otter-Logic/MachineLearning) and
[Unsupervised](https://github.com/Otter-Logic/Unsupervised) as sibling folders and
the project references resolve against your working copy; without them the build
falls back to the published packages.

## License

[MIT](LICENSE).
