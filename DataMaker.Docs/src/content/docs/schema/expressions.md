---
title: Expressions & the Fn library
description: The DataMaker expression DSL for visibility, calculated fields, and validation — plus the full Fn function library.
---

Forms can carry expressions in three places:

- **`visibleWhen`** (field or layout group) — a boolean; the element shows when
  it's true.
- **`calculatedExpression`** (field) — derives the field's value; the field
  becomes read-only.
- **validation `expression` rules** — a boolean that must hold.

Expressions reference field **names** as variables and call functions from the
**`Fn`** library. The same expressions run server-side and in the web renderer
(compiled to JS — `compiled.json` in a `.dmf v3`), so results match.

```text
age >= 18 && country == "FR"
Upper(Trim(first_name))
If(LenStr(notes) > 0, "has notes", "empty")
DiffDays(end_date, start_date) > 7
```

## Operators

- Comparison: `== != < <= > >=`
- Logical: `&& || !`
- Arithmetic: `+ - * / %`
- Null-coalesce: `??`
- Ternary: `cond ? a : b`

Both `Fn.Upper(x)` and the bare `Upper(x)` forms are accepted.

## The Fn library

All functions are pure and run identically on the server and in the browser
(the JS port is `fn.js`).

**Strings** — `Concat(a,b)`, `Concat3`, `Concat4`, `Lower(s)`, `Upper(s)`,
`Trim(s)`, `TitleCase(s)`, `Left(s,n)`, `Right(s,n)`, `Pad(s,width,padChar)`,
`PadRight(s,width,padChar)`, `LenStr(s)`.

**Regex** — `Match(input,pattern)`, `Capture(input,pattern,group)`,
`Replace(input,pattern,replacement)` (case-insensitive).

**Convert** — `ToInt(s)`, `ToLong(s)`, `IntToStr0(n)`, `IntToStr(n,format)`.

**Branch** — `If(cond,then,else)`, `Coalesce(a,b)`, `Coalesce3(a,b,c)`,
`Switch(v,case,result,default)`, `Switch2`, `Switch3`.

**Arrays** — `Split(input,sep)`, `Join(arr,sep)`, `At(arr,i)`, `First(arr)`,
`Last(arr)`, `Take(arr,n)`, `Skip(arr,n)`, `Slice(arr,start,count)`,
`IndexOf(arr,v)`, `Where(arr,v)`, `Except(arr,v)`, `Insert(arr,i,v)`,
`Remove(arr,i)`, `Filter(arr,v)`, `Reverse(arr)`, `Sort(arr)`, `Distinct(arr)`,
`Map(arr,prefix,suffix)`, `Union(a,b)`, `Intersect(a,b)`, `SetExcept(a,b)`.

**Aggregates** — `Len(arr)`, `Any(arr,v)`, `All(arr,v)`, `CountOf(arr,v)`,
`Min(a,b)`, `Max(a,b)`, `MinD(a,b)`, `MaxD(a,b)`.

**Dates** (strings `YYYY-MM-DD`, UTC) — `Year(s)`, `Month(s)`, `Day(s)`,
`AddDays(s,days)`, `DiffDays(a,b)`, `Today()`, `Now()`.

:::note
String comparisons in array/branch functions are **case-insensitive** (ordinal
ignore-case). Regex helpers use the case-insensitive flag.
:::

## Building your own evaluator

If you render forms outside .NET/JS, you'll need to evaluate these expressions
to support `visibleWhen`/`calculatedExpression`. Two options:

1. **Use the compiled JS.** A `.dmf v3` ships `compiled.json` mapping each
   expression key to a ready JS function string (`function(v){ return …; }`,
   where `v` is the values bag). Pair it with a port of the `Fn` library
   (`Dm.*`) and you don't need to parse the DSL at all.
2. **Parse the DSL yourself.** Implement the operators above + the `Fn`
   functions you use. Match the case-insensitive string semantics and UTC date
   handling to stay consistent with the server.

If you can't evaluate an expression, **fail open** for `visibleWhen` (show the
field) and leave `calculatedExpression` fields editable — never silently drop
data.
