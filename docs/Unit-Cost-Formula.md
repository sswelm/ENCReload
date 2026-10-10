# Unit production cost

In vanilla Humankind a unit has a fixed industry cost, and strategic resources are a gate: you either own enough
deposits to build the unit or you do not. ENCReload replaces the fixed cost with a **dynamic cost** that depends on
how large your standing army already is, how many civilians support it, and how much of each strategic resource
you have access to.

There is no single formula. Every cost class has its own, but they all share one shape, and this page explains that
shape, the inputs, and the per-unit tuning. The formulas themselves live in
[`RpnIndustryDefinitionENC.asset`](https://github.com/sswelm/ENCReload/blob/main/Assets/Databases/Settlement/RpnIndustryDefinitionENC.asset)
as `ProductionCost_Unit_*` entries; each unit definition names the one it uses.

---

## The shape

```
Cost = Base × (0.5 + x / (1 + x))        where   x = Burden / Capacity
```

| `x` | Meaning | Cost |
|---|---|---|
| 0 | You have no army at all | **50%** of Base |
| 1 | Your army exactly matches what the empire can support | **100%** of Base |
| large | Your army is far beyond what the empire can support | approaches **150%** of Base, never more |

The cost never explodes. It slides between half and one-and-a-half times the base price.

## Burden

**Burden** is the game's `MilitaryPopulationCount`: the summed population cost of every unit you field, in armies
and in air squadrons. ENC units cost between 1 and 6 population each (an Ironclad is 5), so heavy units weigh more
on the empire than light ones.

## Capacity

**Capacity** is the *smallest* of the following terms.

**Civilian support**, present in every formula:

```
(Population + 5)^0.75 × (1 + Era)^0.75
```

`Population` is the empire's total population in its own (not captured) settlements; `Era` is the empire's era
level. Because of the 0.75 exponent, capacity grows slower than population: a big empire supports a bigger army,
but not proportionally.

**One term per strategic resource** the unit uses:

```
k × (1 + deposits you have access to) × (1 + Era)^0.75
```

The factor `k` says how hungry the unit is for that resource:

| `k` | Use |
|---|---|
| 0.999 | light |
| 0.499 | normal |
| 0.249 | heavy |

Because capacity is the *minimum* of all terms, **your scarcest resource sets the price**. Extra copper does
nothing if iron is the bottleneck.

The resources appear in the formulas by number:

| Property | Resource |
|---|---|
| `Resource01AccessCount` | Horses |
| `Resource02AccessCount` | Copper |
| `Resource03AccessCount` | Iron |
| `Resource04AccessCount` | Coal |
| `Resource05AccessCount` | Saltpetre |
| `Resource06AccessCount` | Oil |
| `Resource07AccessCount` | Aluminium |
| `Resource08AccessCount` | Uranium |

## Resources matter even without a hard requirement

A unit's cost formula and its build requirement are separate. Many units have a resource in their cost formula that
they do **not** need in order to be built. Such a unit can always be built, since the term is `1 + deposits` and so
never reaches zero, but with no access that resource gives very little capacity and the unit gets expensive.

For these units a resource is a discount, not a gate.

---

## Worked example: Ironclad

Base **10140**. Required to build: 2 Iron (`k` = 0.249) and 1 Coal (`k` = 0.499). Not required, but still in the
cost: Copper (`k` = 0.999).

Industrial era (Era = 5), 100 population, access to 3 copper, 2 iron and 1 coal:

- Civilian support = 105^0.75 × 6^0.75 ≈ 125.7
- Copper ≈ 15.3, Iron ≈ 2.9, Coal ≈ 3.8
- Capacity = **2.9**; iron is the bottleneck

| Burden | 3 copper / 2 iron / 1 coal (capacity 2.9) | 6 copper / 11 iron / 7 coal (capacity 11.5) |
|---|---|---|
| 0 | 5,070 | 5,070 |
| 5 | 11,517 | 8,151 |
| 20 | 13,940 | 11,517 |
| 60 | 14,748 | 13,584 |

Iron is still the bottleneck in the richer empire. With no copper at all the Ironclad stays buildable, but copper's
capacity drops to about 3.8, which in the richer empire would make copper the bottleneck instead.

## Worked example: Militia

Base **1370**. No resource is required to build it, yet Iron and Saltpetre (both `k` = 0.999) are in its cost.

Early Modern era (Era = 4), 60 population:

- Civilian support = 65^0.75 × 5^0.75 ≈ 76.5
- Each resource gives 0.999 × (1 + deposits) × 5^0.75

| Burden | 0 iron, 0 saltpetre (capacity 3.3) | 3 of each (capacity 13.4) | 8 of each (capacity 30.1) |
|---|---|---|---|
| 0 | 685 | 685 | 685 |
| 10 | 1,712 | 1,271 | 1,027 |
| 30 | 1,918 | 1,633 | 1,369 |

You can always raise Militia. At a fielded army of 30 population they cost 1,918 with neither resource and 1,369
with 8 of each. It takes about 22 deposits of each before the resources stop being the bottleneck and civilian
support (76.5) takes over; from there, more deposits change nothing and only population and era lower the price.

Other regular land units that work the same way: the Buccaneer (Copper and Saltpetre), the Sea Battalion (Copper
and Coal) and the Caribbean Pirates' emblematic land unit (Iron and Saltpetre).

## Worked example: a unit with no strategic resource

A Scout (Base **100**) only uses the civilian term. In an Ancient empire (Era = 1) with 20 population the capacity
is 18.8, and the Scout costs 50 with no army, 85 at burden 10 and 111 at burden 30.

The era levels in these examples assume the Neolithic is 0, so Ancient is 1 and Industrial is 5.

---

## Per-unit tuning

| Knob | Range |
|---|---|
| **Base** | 64 to 215 in Era 1, up to 6,000 to 37,649 in Era 6 |
| **Resource factor `k`** | 0.999, 0.499 or 0.249 per resource |
| **Population multiplier** | Planes and missiles multiply `Population` by 2 to 5 in the civilian term, so they weigh less on the home front |
| **Flat cost** | A few `_Base` classes keep a constant cost with no scaling |

## Variants in the current data

Not every formula follows the shape above exactly. As of this writing:

- **Where the minimum is taken.** The shape described on this page, `Burden / min(civilian, resources…)`, is used
  by 120 formulas (151 units). Another 65 formulas (86 units, for example `ProductionCost_Unit_Era2_High_Iron`)
  compute `min(Burden / civilian, resources…)` instead. There the resource term only caps `x`, so it matters only
  when the army is large, and fewer deposits give a lower cap and therefore a lower cost.
- **Era exponent.** 13 formulas, mostly Era 3, use `(1 + Era)` without the `^0.75` in the civilian term.
- **Population offset.** Two formulas use `Population + 10` instead of `Population + 5`.
- **Resource factor versus requirement.** `k` usually follows the build requirement (none needed gives 0.999, one
  gives 0.499, two give 0.249), but not for every unit.

## Reading a formula yourself

The formulas are stored as reverse Polish notation. `RpnOperationStack` is a list of 32-bit operation codes; number
constants are taken in order from `ConstantStack` (stored ×1000, so `500` is 0.5) and properties in order from
`PropertyLocalNameStack`.

| Code | Operation |
|---|---|
| 0 | add |
| 2 | multiply |
| 3 | divide |
| 5 | power |
| 6 | max |
| 7 | min |
| 8 | read a property from the target (the settlement) |
| 9 | read a property from the source (the empire) |
| 10 | read the next constant |

The game's own formula viewer prints these without the parentheses around a `min` chain, which makes
`Burden / civilian min resource` look ambiguous. The operation stack is the authority.
